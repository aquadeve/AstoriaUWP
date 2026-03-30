// ICpuInterpreter - Common interface for all guest CPU implementations.
// Implemented by ArmInterpreter (ARM32), Arm64Interpreter (AArch64), and X64Interpreter (x86-64).

namespace DalvikUWPCSharp.FLinux.Cpu
{
    using DalvikUWPCSharp.FLinux.Core;
    using System.Threading.Tasks;

    /// <summary>
    /// Execution mode – determines which CPU interpreter is used when running ELF binaries.
    /// </summary>
    public enum ExecutionMode
    {
        /// <summary>ARM 32-bit software interpreter (emulates ARM/Thumb on any host).</summary>
        Arm32,
        /// <summary>ARM 64-bit (AArch64) software interpreter.</summary>
        Arm64,
        /// <summary>x86-64 software interpreter.</summary>
        X64
    }

    /// <summary>
    /// Common interface that every CPU interpreter must implement.
    /// Mirrors the conceptual "thread context" used in FLinux platform/*/context.h.
    /// </summary>
    public interface ICpuInterpreter
    {
        /// <summary>Guest program counter.</summary>
        ulong PC { get; set; }

        /// <summary>Guest stack pointer.</summary>
        ulong SP { get; set; }

        /// <summary>The virtual memory space this CPU operates on.</summary>
        LinuxMemory Memory { get; }

        /// <summary>
        /// Run the CPU from the current PC until the process calls exit() or an
        /// unrecoverable fault occurs.
        /// </summary>
        Task RunAsync();

        /// <summary>
        /// Execute a single instruction at the current PC.
        /// Returns false if execution should stop (exit, fault, etc.).
        /// </summary>
        bool Step();

        /// <summary>
        /// Read a general-purpose register by index (0-based, architecture-specific).
        /// </summary>
        ulong GetRegister(int index);

        /// <summary>
        /// Write a general-purpose register by index.
        /// </summary>
        void SetRegister(int index, ulong value);

        /// <summary>Human-readable dump of all registers.</summary>
        string DumpRegisters();
    }
}
