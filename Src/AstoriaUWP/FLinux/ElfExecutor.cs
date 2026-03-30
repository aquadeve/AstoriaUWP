// ElfExecutor - Top-level orchestrator for FLinux ELF binary execution.
// Combines ElfBinary, LinuxMemory, LinuxProcess, LinuxVfs, SyscallDispatcher,
// and the appropriate CPU interpreter to fully execute a Linux ELF binary
// inside the AstoriaUWP Android compatibility layer.
//
// Usage:
//   var exec = new ElfExecutor(mode, appRoot);
//   await exec.LoadAndRunAsync(elfBytes, argv, envp);
//
// The executor is used by DalvikCPU.ScanNativeLibraries() to actually run
// Android native (.so) code instead of just parsing the ELF metadata.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;

namespace DalvikUWPCSharp.FLinux
{
    using DalvikUWPCSharp.FLinux.Core;
    using DalvikUWPCSharp.FLinux.Cpu;
    using DalvikUWPCSharp.FLinux.Syscalls;
    using DalvikUWPCSharp.Classes; // JniEnvironment, ElfLoader

    /// <summary>
    /// Orchestrates the full lifecycle of loading and running a Linux ELF binary.
    /// Mirrors the startup sequence from FLinux src/main.c:
    ///   init_shared → init_process → init_vfs → do_execve → run().
    /// </summary>
    public class ElfExecutor
    {
        private struct ParsedJniName
        {
            public string ClassName;
            public string MethodName;
            public string Signature;
        }

        // ── Configuration ─────────────────────────────────────────────────────

        /// <summary>Architecture / execution mode selected by the user.</summary>
        public ExecutionMode Mode { get; private set; }

        /// <summary>
        /// Whether to use the JNI bridge (managed C# stubs) when the ELF
        /// exports Java_* functions, instead of actually running the ELF CPU loop.
        /// This is the primary path for Android .so libraries: we route JNI calls
        /// through the existing JniBridge and fall back to the CPU interpreter only
        /// for non-JNI native code.
        /// </summary>
        public bool UseJniBridgeForJni { get; set; } = true;

        // ── Core subsystems ───────────────────────────────────────────────────

        public LinuxMemory  Memory  { get; }
        public LinuxProcess Process { get; }
        public LinuxVfs     Vfs     { get; }

        private readonly SyscallDispatcher _syscalls;

        // ── Loaded ELF state ──────────────────────────────────────────────────

        private LoadedElf _loadedExe;
        private ICpuInterpreter _cpu;

        // ── Host-side resources ───────────────────────────────────────────────

        private StorageFolder _appRoot;

        /// <summary>
        /// Fired whenever the guest writes to stdout / stderr so the UI can display it.
        /// </summary>
        public event Action<string> GuestOutput;

        // ── Constructor ───────────────────────────────────────────────────────

        public ElfExecutor(ExecutionMode mode, StorageFolder appRoot = null)
        {
            Mode     = mode;
            _appRoot = appRoot;

            Memory  = new LinuxMemory();
            Process = new LinuxProcess();
            Vfs     = new LinuxVfs();
            _syscalls = new SyscallDispatcher(Memory, Vfs, mode);

            if (appRoot != null)
            {
                Vfs.SetDataRoot(appRoot);
                Vfs.Mount("/data", appRoot);
            }

            Debug.WriteLine($"[ElfExecutor] Created executor mode={mode}");
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Set the execution mode (ARM32 / ARM64 / x64).
        /// Can be changed before calling LoadAndRunAsync.
        /// </summary>
        public void SetMode(ExecutionMode mode)
        {
            Mode = mode;
            Debug.WriteLine($"[ElfExecutor] Mode changed to {mode}");
        }

        /// <summary>
        /// Load an ELF binary from <paramref name="elfData"/> and prepare it for execution.
        /// Does not start the CPU; call <see cref="RunAsync"/> separately.
        /// </summary>
        public bool Load(byte[] elfData, string[] argv = null, string[] envp = null)
        {
            if (argv == null) argv = new[] { "app" };
            if (envp == null) envp = BuildDefaultEnv();

            // Validate that the ELF architecture matches the chosen execution mode.
            var preCheck = new ElfLoader();
            if (!preCheck.Load(elfData))
            {
                Debug.WriteLine("[ElfExecutor] Failed to parse ELF header.");
                return false;
            }

            if (!ArchMatchesMode(preCheck.Machine))
            {
                Debug.WriteLine($"[ElfExecutor] ELF arch {preCheck.GetArchitectureName()} " +
                                $"does not match mode {Mode}. Auto-switching mode.");
                AutoSwitchMode(preCheck.Machine);
            }

            // Map ELF segments into virtual memory.
            var elfBinary = new ElfBinary(Memory);
            _loadedExe = elfBinary.Load(elfData);
            if (_loadedExe == null)
            {
                Debug.WriteLine("[ElfExecutor] ELF segment loading failed.");
                return false;
            }

            // Allocate guest stack and write initial argc/argv/envp/auxv.
            ulong stackTop = Memory.AllocateStack();
            ulong sp = elfBinary.SetupStack(stackTop, _loadedExe.Machine == 183 || _loadedExe.Machine == 62,
                                             _loadedExe, _loadedExe.Interpreter, argv, envp);

            // Create the CPU interpreter.
            _cpu = CreateCpu();
            _cpu.SP = sp;

            // Set PC to the entry point (adjusted for PIE).
            ulong entry = _loadedExe.Type == ElfType.Dyn
                ? _loadedExe.LoadBase + _loadedExe.EntryPoint
                : _loadedExe.EntryPoint;
            _cpu.PC = entry;

            // For ARM32 Thumb entry points (bit 0 set), align and enable Thumb mode.
            if (Mode == ExecutionMode.Arm32 && (entry & 1) != 0)
            {
                _cpu.PC = entry & ~1UL;
                // ArmInterpreter uses register index 16 as the CPSR pseudo-register (beyond R0–R15).
                // Setting bit 5 (0x20) of CPSR enables Thumb instruction decoding.
                if (_cpu is ArmInterpreter arm)
                    arm.SetRegister(16, 0x20); // index 16 = CPSR; bit 5 = Thumb (T) flag
            }

            Debug.WriteLine($"[ElfExecutor] Loaded. Entry=0x{entry:X} SP=0x{sp:X} Mode={Mode}");
            return true;
        }

        /// <summary>
        /// Start the CPU interpreter and run until the process exits or faults.
        /// </summary>
        public Task RunAsync()
        {
            if (_cpu == null) throw new InvalidOperationException("Call Load() first.");
            Debug.WriteLine($"[ElfExecutor] Starting CPU at PC=0x{_cpu.PC:X}");
            return _cpu.RunAsync();
        }

        /// <summary>
        /// Convenience: load + run in one call.
        /// </summary>
        public async Task<bool> LoadAndRunAsync(byte[] elfData,
                                                string[] argv = null,
                                                string[] envp = null)
        {
            if (!Load(elfData, argv, envp)) return false;
            await RunAsync();
            return true;
        }

        /// <summary>
        /// Load a .so library: parse it, register its JNI exports via the JNI bridge,
        /// and return the ElfLoader for symbol resolution.
        /// This is called by DalvikCPU.ScanNativeLibraries() for each .so in the APK.
        /// </summary>
        public ElfLoader LoadLibrary(byte[] soData, string libName, JniEnvironment jniEnv)
        {
            var loader = new ElfLoader();
            if (!loader.Load(soData))
            {
                Debug.WriteLine($"[ElfExecutor] {libName}: ELF parse failed.");
                return null;
            }

            Debug.WriteLine($"[ElfExecutor] {libName}: arch={loader.GetArchitectureName()} " +
                            $"exports={loader.ExportedFunctions.Count}");

            if (!ArchMatchesMode(loader.Machine))
            {
                Debug.WriteLine($"[ElfExecutor] {libName}: arch mismatch, auto-switching mode.");
                AutoSwitchMode(loader.Machine);
            }

            // Register JNI exports so DalvikCPU can call them via the JNI bridge.
            foreach (string jniFunc in loader.GetJniExports())
            {
                var parsed = ParseJniName(jniFunc);
                if (parsed != null)
                {
                    var parsedValue = parsed.Value;
                    string cls = parsedValue.ClassName;
                    string method = parsedValue.MethodName;
                    string sig = parsedValue.Signature;
                    string capturedFunc = jniFunc; // capture for lambda
                    // Register a stub that logs the call and returns null.
                    // A full implementation would: map ELF segments into LinuxMemory,
                    // set up the JNI_Env pointer, then jump to symbol.Value in the CPU.
                    jniEnv.RegisterNativeMethod(cls, method, sig,
                        (env2, thisObj, args2) =>
                        {
                            Debug.WriteLine($"[ElfExecutor] JNI call: {capturedFunc}");
                            return JniValue.FromObject(null);
                        });
                    Debug.WriteLine($"[ElfExecutor]   Registered JNI: {cls}#{method}{sig}");
                }
            }

            return loader;
        }

        /// <summary>
        /// Execute a single step of the loaded binary (useful for debugging).
        /// </summary>
        public bool Step()
        {
            return _cpu?.Step() ?? false;
        }

        /// <summary>Human-readable register dump for the current CPU state.</summary>
        public string DumpRegisters() => _cpu?.DumpRegisters() ?? "(no CPU)";

        /// <summary>Human-readable virtual-memory map.</summary>
        public string DumpMaps() => Memory.DumpMaps();

        // ── Private helpers ───────────────────────────────────────────────────

        private ICpuInterpreter CreateCpu()
        {
            var syscalls = new SyscallDispatcher(Memory, Vfs, Mode);
            switch (Mode)
            {
                case ExecutionMode.Arm32:  return new ArmInterpreter(Memory,   syscalls, Process);
                case ExecutionMode.Arm64:  return new Arm64Interpreter(Memory, syscalls, Process);
                default:                   return new X64Interpreter(Memory,   syscalls, Process);
            }
        }

        private bool ArchMatchesMode(ushort machine)
        {
            switch (Mode)
            {
                case ExecutionMode.Arm32:  return machine == 40;  // EM_ARM
                case ExecutionMode.Arm64:  return machine == 183; // EM_AARCH64
                case ExecutionMode.X64:    return machine == 62;  // EM_X86_64
                default: return false;
            }
        }

        private void AutoSwitchMode(ushort machine)
        {
            switch (machine)
            {
                case 40:  Mode = ExecutionMode.Arm32;  break;
                case 183: Mode = ExecutionMode.Arm64;  break;
                case 62:  Mode = ExecutionMode.X64;    break;
                case 3:   Mode = ExecutionMode.X64;    break; // x86 → x64 interpreter
            }
        }

        private static string[] BuildDefaultEnv()
        {
            return new[]
            {
                "PATH=/system/bin:/system/xbin",
                "LD_LIBRARY_PATH=/system/lib:/vendor/lib:/data/app-lib",
                "ANDROID_ROOT=/system",
                "ANDROID_DATA=/data",
                "ANDROID_BOOTLOGO=1",
                "ANDROID_ASSETS=/system/app",
                "EXTERNAL_STORAGE=/sdcard",
                "BOOTCLASSPATH=/system/framework/core.jar",
                "LOOP_MOUNTPOINT=/mnt/obb"
            };
        }

        /// <summary>
        /// Parse a JNI function name like "Java_com_example_Foo_method__sig"
        /// into (className, methodName, signature).
        /// </summary>
        private static ParsedJniName? ParseJniName(string name)
        {
            if (!name.StartsWith("Java_")) return null;
            // Strip "Java_" prefix.
            string rest = name.Substring(5);
            // Find the last two underscores that delimit class/method boundary.
            // Convention: Java_<class>_<method>[__<sig>]
            // Class parts use underscores as separators (/ → _).
            // Double-underscore (__) escapes an underscore inside a class/method name.
            int sep = rest.LastIndexOf('_');
            if (sep <= 0) return null;
            string method = rest.Substring(sep + 1).Replace("_1", "_");
            string cls    = rest.Substring(0, sep).Replace('_', '/').Replace("1/", "_");
            return new ParsedJniName
            {
                ClassName = cls,
                MethodName = method,
                Signature = string.Empty
            };
        }
    }
}
