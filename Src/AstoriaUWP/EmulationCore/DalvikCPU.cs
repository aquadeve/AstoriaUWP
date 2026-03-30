// DalvikCPU - PC-based Dalvik bytecode interpreter for AstoriaUWP
// Implements concepts from referenceBridge (android_init, linker, JNI bridge stubs)
// into a functional managed C# Dalvik VM.
// Native .so execution is now handled by ElfExecutor (FLinux C# port),
// which provides ARM32 / ARM64 / x64 software CPU interpreters.
//
// Debug logging: Extended tracing is compiled only in DEBUG builds via
// #if DEBUG / [Conditional("DEBUG")] so release builds carry no overhead.

using AndroidInteropLib;
using AndroidInteropLib.android.content;
using AndroidInteropLib.android.view;
using DalvikUWPCSharp.Applet;
using DalvikUWPCSharp.FLinux;
using DalvikUWPCSharp.FLinux.Cpu;
using DalvikUWPCSharp.Reassembly;
using DalvikUWPCSharp.Reassembly.UI;
using dex.net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml;

namespace DalvikUWPCSharp.Classes
{
    // Sentinel value to signal method return from ExecuteInstruction
    // Positive values are branch targets (opcode byte offsets).
    // JUMP_RETURN signals early exit from the execution loop.
    internal static class ExecutionSignals
    {
        public const long CONTINUE = -1L;   // advance PC normally
        public const long JUMP_RETURN = long.MinValue; // return from method
    }

    // DalvikCPU class
    public class DalvikCPU
    {
        object[] Registers = new object[16];
        object result;
        public Dex dex;
        string packageName;
        public EmuPage hostPage;
        DroidApp da;

        private Context appContext;
        private AndroidInteropLib.android.view.Window droidWindow;

        // JNI bridge for native method calls (apkenv-inspired)
        public JniEnvironment JniEnv { get; private set; }

        // Android system properties (from referenceBridge android_init.cpp)
        public AndroidProperties Properties { get; private set; }

        // Dynamic linker stubs (from referenceBridge linker.cpp)
        public DynamicLinker Linker { get; private set; }

        // ELF loader for parsing native .so libraries
        private Dictionary<string, ElfLoader> loadedLibraries = new Dictionary<string, ElfLoader>();

        // FLinux ELF executor – handles actual ELF binary execution with CPU emulation.
        // Execution mode (ARM32 / ARM64 / x64) is set from the UI selection in EmuPage.
        public ElfExecutor NativeExecutor { get; private set; }

        // Instance field storage: object -> (fieldName -> value)
        private Dictionary<int, Dictionary<string, object>> instanceFields =
            new Dictionary<int, Dictionary<string, object>>();

        // Static field storage: "ClassName.fieldName" -> value
        private Dictionary<string, object> staticFields =
            new Dictionary<string, object>();

        // Object identity counter for instanceFields keys
        private int nextObjectId = 1;
        private Dictionary<object, int> objectIds = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);

        // Instruction execution count for debugging
        private int instructionCount;

        // Per-call-frame saved registers (to support nested RunMethod calls)
        private Stack<object[]> registerStack = new Stack<object[]>();

        // Call depth counter for debug logging indentation
        private int callDepth;

        // String pool cache: resource ID -> string value (populated from DEX string table)
        private Dictionary<int, string> stringResources = new Dictionary<int, string>();

        public DalvikCPU(Dex d, string pName, EmuPage hostPg)
        {
            dex = d;
            packageName = pName;
            hostPage = hostPg;
            da = hostPage.RunningApp;
            da.cpu = this;

            // Initialize JNI environment and register system library stubs
            JniEnv = new JniEnvironment();
            AndroidSystemLibraryStubs.RegisterAll(JniEnv);

            // Initialize Android system properties (from referenceBridge android_init.cpp)
            Properties = new AndroidProperties();

            // Initialize dynamic linker stubs (from referenceBridge linker.cpp)
            Linker = new DynamicLinker(Properties);

            // Log platform information for Xbox/ARM diagnostics
            XboxPlatform.LogPlatformInfo();

            // set preload status "Setting up app environment"
            hostPage.setPreloadStatusText("Setting up app environment...");

            // Pre-populate string resources from the DEX string table for getString() calls
            PopulateStringResources();
        }

        /// <summary>
        /// Pre-loads string resources from the DEX string table so that getString(resId)
        /// can return meaningful values.  This mirrors android.content.res.Resources.getString().
        /// </summary>
        private void PopulateStringResources()
        {
            try
            {
                // Cache all DEX strings keyed by their index – this provides a best-effort
                // mapping for getString(int) where the int is a string-table index.
                int count = 0;
                try { foreach (string s in dex.GetStrings()) count++; } catch { }
                int idx = 0;
                foreach (string s in dex.GetStrings())
                {
                    try { stringResources[idx] = s; }
                    catch { /* skip */ }
                    idx++;
                }
#if DEBUG
                Debug.WriteLine($"[DalvikCPU] Populated {stringResources.Count} string resources from DEX.");
#endif
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DalvikCPU] PopulateStringResources error: " + ex.Message);
            }
        }

        /// <summary>Debug-only helper to emit a trace line with call-depth indentation.</summary>
        [Conditional("DEBUG")]
        private void TraceInstruction(string message)
        {
            Debug.WriteLine(new string(' ', callDepth * 2) + "[DalvikCPU] " + message);
        }

        public async void Start()
        {
            if (appContext == null)
            {
                appContext = new AstoriaContext(da, await AstoriaResources.CreateAsync(da));
                droidWindow = new AstoriaWindow(appContext, hostPage);
                Debug.WriteLine("[DalvikCPU] App context and window created.");
            }

            // Scan for native libraries in the APK (apkenv-inspired)
            await ScanNativeLibraries();

            // Determine the main activity class to launch.
            // Prefer the launcher activity from AndroidManifest; fall back to "<package>.MainActivity".
            string mainActivityClass = packageName + ".MainActivity";
            if (da.metadata?.mainActivity != null)
            {
                string raw = da.metadata.mainActivity.Trim();
                if (raw.StartsWith("."))
                    mainActivityClass = packageName + raw;          // relative: ".MyActivity" -> "com.example.MyActivity"
                else if (!raw.Contains("."))
                    mainActivityClass = packageName + "." + raw;    // unqualified: "MyActivity" -> "com.example.MyActivity"
                else
                    mainActivityClass = raw;                        // fully-qualified
            }
            Debug.WriteLine("[DalvikCPU] Looking for launcher activity: " + mainActivityClass);

            // Find and execute the app's main activity onCreate
            bool foundActivity = false;
            foreach (Class cl in dex.GetClasses())
            {
                if (cl.Name.Equals(mainActivityClass))
                {
                    foundActivity = true;
                    Debug.WriteLine("[DalvikCPU] Found MainActivity: " + cl.Name);
                    foreach (Method m in cl.GetMethods())
                    {
                        if (m.Name.Equals("onCreate"))
                        {
                            Debug.WriteLine("[DalvikCPU] Calling MainActivity.onCreate...");
                            RunMethod(m, cl);
                            Debug.WriteLine("[DalvikCPU] MainActivity.onCreate returned.");
                        }
                    }
                }
            }

            if (!foundActivity)
                Debug.WriteLine("[DalvikCPU] WARNING: MainActivity (" + mainActivityClass + ") not found in DEX.");

            hostPage.preloadDone();
        }

        public async void GoBack()
        {
            var dialog = new Windows.UI.Popups.MessageDialog("Back event initiated.", "Dalvik CPU");
            await dialog.ShowAsync();
        }

        // RunMethod with PC-based execution loop supporting branches
        public object RunMethod(Method m, Class cl, params object[] obj)
        {
            if (!TryNativeMethod(m, cl, obj))
            {
                // Save current registers and push a new frame
                registerStack.Push(Registers);

                // Allocate register file based on the method's declared register count.
                // Dalvik methods declare their register count in the code header.
                // Fall back to 16 if the count is somehow zero (e.g. abstract/native stubs).
                uint regCount = 0;
                try { regCount = m.GetRegisterCount(); } catch { }
                if (regCount < 1) regCount = 16;
                Registers = new object[regCount];

                // Copy arguments into low registers
                if (obj != null)
                {
                    for (int i = 0; i < obj.Length && i < Registers.Length; i++)
                        Registers[i] = obj[i];
                }

                callDepth++;
#if DEBUG
                string mTypeName = "(unknown)";
                try { mTypeName = dex.GetTypeName(m.ClassIndex); } catch { }
                TraceInstruction($">>> Enter {mTypeName}.{m.Name} regs={regCount} args={obj?.Length ?? 0}");
#endif

                try
                {
                    var instructions = m.GetInstructions().ToList();

                    // Build opcode-offset -> instruction-index map for branch resolution
                    var offsetToIndex = new Dictionary<long, int>(instructions.Count);
                    for (int i = 0; i < instructions.Count; i++)
                        offsetToIndex[instructions[i].OpCodeOffset] = i;

                    int pc = 0;
                    while (pc >= 0 && pc < instructions.Count)
                    {
                        instructionCount++;
                        long signal = ExecuteInstruction(instructions[pc], cl);

                        if (signal == ExecutionSignals.JUMP_RETURN)
                            break;
                        else if (signal != ExecutionSignals.CONTINUE)
                        {
                            // signal is a branch target offset
                            if (offsetToIndex.TryGetValue(signal, out int idx))
                                pc = idx;
                            else
                                pc++;
                        }
                        else
                            pc++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[DalvikCPU] RunMethod exception in " + m.Name + ": " + ex.Message);
#if DEBUG
                    Debug.WriteLine("[DalvikCPU]   Stack trace: " + ex.StackTrace);
#endif
                }
                finally
                {
#if DEBUG
                    TraceInstruction($"<<< Exit {m.Name} result={result}");
#endif
                    callDepth--;
                    // Restore previous frame's registers
                    Registers = registerStack.Pop();
                }
            }

            return result;
        }

        // ExecuteInstruction returns:
        //   ExecutionSignals.CONTINUE (-1) → advance PC normally
        //   ExecutionSignals.JUMP_RETURN   → exit method (return opcode)
        //   >= 0 long value               → absolute byte-offset branch target
        public long ExecuteInstruction(OpCode op, Class cl)
        {
            switch (op.Instruction)
            {
                // ── NOP ──────────────────────────────────────────────────────
                case Instructions.Nop:
                    break;

                // ── CONST ────────────────────────────────────────────────────
                case Instructions.Const:
                    ConstOpCode ConstOP = (ConstOpCode)op;
                    Registers[ConstOP.Destination] = ConstOP.Value;
                    break;

                case Instructions.Const4:
                    var c4 = (Const4OpCode)op;
                    Registers[c4.Destination] = (int)c4.Value;
                    break;

                case Instructions.Const16:
                    var c16 = (Const16OpCode)op;
                    Registers[c16.Destination] = (int)c16.Value;
                    break;

                case Instructions.ConstHigh:
                    var cH = (ConstHighOpCode)op;
                    Registers[cH.Destination] = cH.Value;
                    break;

                case Instructions.ConstWide16:
                    var cw16 = (ConstWide16OpCode)op;
                    Registers[cw16.Destination] = cw16.Value;
                    break;

                case Instructions.ConstWide32:
                    var cw32 = (ConstWide32OpCode)op;
                    Registers[cw32.Destination] = cw32.Value;
                    break;

                case Instructions.ConstWide:
                    var cw = (ConstWideOpCode)op;
                    Registers[cw.Destination] = cw.Value;
                    break;

                case Instructions.ConstWideHigh:
                    var cwH = (ConstWideHighOpCode)op;
                    Registers[cwH.Destination] = cwH.Value;
                    break;

                case Instructions.ConstString:
                    var cs = (ConstStringOpCode)op;
                    try { Registers[cs.Destination] = dex.GetString(cs.StringIndex); }
                    catch { Registers[cs.Destination] = ""; }
                    break;

                case Instructions.ConstStringJumbo:
                    var csj = (ConstStringJumboOpCode)op;
                    try { Registers[csj.Destination] = dex.GetString(csj.StringIndex); }
                    catch { Registers[csj.Destination] = ""; }
                    break;

                case Instructions.ConstClass:
                    var cc = (ConstClassOpCode)op;
                    try
                    {
                        string typeName = dex.GetTypeName(cc.TypeIndex);
                        string managed = "AndroidInteropLib." + ConvertClassName(typeName);
                        Type t = Type.GetType(managed);
                        Registers[cc.Destination] = t ?? (object)typeName;
                    }
                    catch { Registers[cc.Destination] = null; }
                    break;

                // ── MOVE ─────────────────────────────────────────────────────
                case Instructions.Move:
                    var mov = (MoveOpCode)op;
                    Registers[mov.To] = Registers[mov.From];
                    break;

                case Instructions.MoveFrom16:
                    var mf16 = (MoveFrom16OpCode)op;
                    Registers[mf16.To] = Registers[mf16.From];
                    break;

                case Instructions.Move16:
                    var m16 = (Move16OpCode)op;
                    Registers[m16.To] = Registers[m16.From];
                    break;

                case Instructions.MoveWide:
                    var mw = (MoveWideOpCode)op;
                    Registers[mw.To] = Registers[mw.From];
                    break;

                case Instructions.MoveWideFrom16:
                    var mwf16 = (MoveWideFrom16OpCode)op;
                    Registers[mwf16.To] = Registers[mwf16.From];
                    break;

                case Instructions.MoveObject:
                    var mo = (MoveObjectOpCode)op;
                    Registers[mo.To] = Registers[mo.From];
                    break;

                case Instructions.MoveObjectFrom16:
                    var mof16 = (MoveObjectFrom16OpCode)op;
                    Registers[mof16.To] = Registers[mof16.From];
                    break;

                case Instructions.MoveResult:
                    MoveResultOpCode movR = (MoveResultOpCode)op;
                    Registers[movR.Destination] = result;
                    break;

                case Instructions.MoveResultWide:
                    var mrw = (MoveResultWideOpCode)op;
                    Registers[mrw.Destination] = result;
                    break;

                case Instructions.MoveResultObject:
                    var mro = (MoveResultObjectOpCode)op;
                    Registers[mro.Destination] = result;
                    break;

                case Instructions.MoveException:
                    var me = (MoveExceptionOpCode)op;
                    Registers[me.Destination] = null;
                    break;

                // ── RETURN ───────────────────────────────────────────────────
                case Instructions.ReturnVoid:
                    result = null;
                    return ExecutionSignals.JUMP_RETURN;

                case Instructions.ReturnValue:
                    var rv = (ReturnValueOpCode)op;
                    result = Registers[rv.Value];
                    return ExecutionSignals.JUMP_RETURN;

                case Instructions.ReturnWide:
                    var rw = (ReturnWideOpCode)op;
                    result = Registers[rw.Value];
                    return ExecutionSignals.JUMP_RETURN;

                case Instructions.ReturnObject:
                    var ro = (ReturnObjectOpCode)op;
                    result = Registers[ro.Value];
                    return ExecutionSignals.JUMP_RETURN;

                // ── GOTO ─────────────────────────────────────────────────────
                case Instructions.Goto:
                case Instructions.Goto16:
                case Instructions.Goto32:
                    return ((IGoto)op).GetTargetAddress();

                // ── THROW ────────────────────────────────────────────────────
                case Instructions.Throw:
                    var thr = (ThrowOpCode)op;
                    Debug.WriteLine("[DalvikCPU] throw v" + thr.Destination);
                    return ExecutionSignals.JUMP_RETURN;

                // ── MONITOR ─────────────────────────────────────────────────
                case Instructions.MonitorEnter:
                case Instructions.MonitorExit:
                    // Stub: synchronization not implemented
                    break;

                // ── CHECK-CAST ───────────────────────────────────────────────
                case Instructions.CheckCast:
                    // Stub: trust the cast succeeds
                    break;

                // ── INSTANCE-OF ──────────────────────────────────────────────
                case Instructions.InstanceOf:
                    var instOf = (InstanceOfOpCode)op;
                    try
                    {
                        string ioTypeName = dex.GetTypeName(instOf.TypeIndex);
                        string ioManaged = "AndroidInteropLib." + ConvertClassName(ioTypeName);
                        Type ioType = Type.GetType(ioManaged);
                        object ioObj = Registers[instOf.Reference];
                        Registers[instOf.Destination] = (ioType != null && ioObj != null && ioType.IsInstanceOfType(ioObj)) ? 1 : 0;
                    }
                    catch { Registers[instOf.Destination] = 0; }
                    break;

                // ── ARRAY-LENGTH ─────────────────────────────────────────────
                case Instructions.ArrayLength:
                    var al = (ArrayLengthOpCode)op;
                    var alArr = Registers[al.ArrayReference];
                    if (alArr is Array alA)
                        Registers[al.Destination] = alA.Length;
                    else
                        Registers[al.Destination] = 0;
                    break;

                // ── NEW-INSTANCE ─────────────────────────────────────────────
                case Instructions.NewInstance:
                    var ni = (NewInstanceOpCode)op;
                    try
                    {
                        string typeName = dex.GetTypeName(ni.TypeIndex);
                        string managedName = "AndroidInteropLib." + ConvertClassName(typeName);
                        Type t = Type.GetType(managedName);
                        if (t != null)
                            Registers[ni.Destination] = Activator.CreateInstance(t);
                        else
                            Registers[ni.Destination] = new DalvikObject(typeName);
#if DEBUG
                        TraceInstruction("new-instance: " + typeName + (t != null ? " [managed]" : " [dalvik]"));
#endif
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[DalvikCPU] new-instance error: " + ex.Message);
                        Registers[ni.Destination] = new DalvikObject("java.lang.Object");
                    }
                    break;

                // ── NEW-ARRAY ────────────────────────────────────────────────
                case Instructions.NewArrayOf:
                    var na = (NewArrayOfOpCode)op;
                    try
                    {
                        int size = ToInt(Registers[na.Size]);
                        Registers[na.Destination] = new object[Math.Max(0, size)];
#if DEBUG
                        TraceInstruction("new-array size=" + size);
#endif
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[DalvikCPU] new-array error: " + ex.Message);
                        Registers[na.Destination] = new object[0];
                    }
                    break;

                // ── FILLED-NEW-ARRAY ────────────────────────────────────────
                case Instructions.FilledNewArrayOf:
                    var fna = (FilledNewArrayOpCode)op;
                    try
                    {
                        // fna.Values contains the register indices that hold the element values
                        byte[] regIndices = fna.Values;
                        object[] filledArr = new object[regIndices != null ? regIndices.Length : 0];
                        for (int i = 0; i < filledArr.Length; i++)
                        {
                            int rIdx = regIndices[i];
                            filledArr[i] = rIdx < Registers.Length ? Registers[rIdx] : null;
                        }
                        result = filledArr;
#if DEBUG
                        TraceInstruction("filled-new-array len=" + filledArr.Length);
#endif
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[DalvikCPU] filled-new-array error: " + ex.Message);
                        result = new object[0];
                    }
                    break;

                // ── FILLED-NEW-ARRAY/RANGE ──────────────────────────────────
                case Instructions.FilledNewArrayRange:
                    var fnar = (FilledNewArrayRangeOpCode)op;
                    try
                    {
                        ushort[] fnarRegs = fnar.Values;
                        object[] filledRangeArr = new object[fnarRegs != null ? fnarRegs.Length : 0];
                        for (int i = 0; i < filledRangeArr.Length; i++)
                        {
                            int rIdx = fnarRegs[i];
                            filledRangeArr[i] = rIdx < Registers.Length ? Registers[rIdx] : null;
                        }
                        result = filledRangeArr;
#if DEBUG
                        TraceInstruction("filled-new-array/range count=" + filledRangeArr.Length);
#endif
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[DalvikCPU] filled-new-array/range error: " + ex.Message);
                        result = new object[0];
                    }
                    break;

                // ── FILL-ARRAY-DATA ─────────────────────────────────────────
                case Instructions.FillArrayData:
                    var fad = (FillArrayDataOpCode)op;
                    try
                    {
                        var arrRef = Registers[fad.Destination] as object[];
                        if (arrRef != null && fad.Values != null)
                        {
                            for (int i = 0; i < fad.Values.Length && i < arrRef.Length; i++)
                                arrRef[i] = fad.Values[i];
                        }
#if DEBUG
                        TraceInstruction("fill-array-data len=" + (fad.Values?.Length ?? 0));
#endif
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[DalvikCPU] fill-array-data error: " + ex.Message);
                    }
                    break;

                // ── PACKED-SWITCH ────────────────────────────────────────────
                case Instructions.PackedSwitch:
                    var ps = (PackedSwitchOpCode)op;
                    try
                    {
                        int psVal = ToInt(Registers[ps.Destination]);
                        int psIdx = psVal - ps.FirstKey;
                        if (psIdx >= 0 && psIdx < ps.Targets.Length)
                            return ps.GetTargetAddress(psIdx);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[DalvikCPU] packed-switch error: " + ex.Message);
                    }
                    break;

                // ── SPARSE-SWITCH ─────────────────────────────────────────────
                case Instructions.SparseSwitch:
                    var ss = (SparseSwitchOpCode)op;
                    try
                    {
                        int ssVal = ToInt(Registers[ss.Destination]);
                        int[] ssKeys = ss.GetKeys();
                        for (int i = 0; i < ssKeys.Length; i++)
                        {
                            if (ssKeys[i] == ssVal)
                                return ss.GetTargetAddress(i);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[DalvikCPU] sparse-switch error: " + ex.Message);
                    }
                    break;

                // ── COMPARE ──────────────────────────────────────────────────
                case Instructions.CmplFloat:
                case Instructions.CmpgFloat:
                {
                    var cmp = (CmplOpCode)op;
                    float fa = ToFloat(Registers[cmp.First]);
                    float fb = ToFloat(Registers[cmp.Second]);
                    if (float.IsNaN(fa) || float.IsNaN(fb))
                        Registers[cmp.Destination] = (op.Instruction == Instructions.CmpgFloat) ? 1 : -1;
                    else
                        Registers[cmp.Destination] = fa.CompareTo(fb);
                    break;
                }
                case Instructions.CmplDouble:
                case Instructions.CmpgDouble:
                {
                    var cmp = (CmplOpCode)op;
                    double da2 = ToDouble(Registers[cmp.First]);
                    double db2 = ToDouble(Registers[cmp.Second]);
                    if (double.IsNaN(da2) || double.IsNaN(db2))
                        Registers[cmp.Destination] = (op.Instruction == Instructions.CmpgDouble) ? 1 : -1;
                    else
                        Registers[cmp.Destination] = da2.CompareTo(db2);
                    break;
                }
                case Instructions.CmpLong:
                {
                    var cmp = (CmplOpCode)op;
                    long la = ToLong(Registers[cmp.First]);
                    long lb = ToLong(Registers[cmp.Second]);
                    Registers[cmp.Destination] = la.CompareTo(lb);
                    break;
                }

                // ── IF (two-register) ────────────────────────────────────────
                case Instructions.IfEq:
                {
                    var ifop = (IfEqOpCode)op;
                    if (CompareValues(Registers[ifop.First], Registers[ifop.Second]) == 0)
                        return ifop.GetTargetAddress();
                    break;
                }
                case Instructions.IfNe:
                {
                    var ifop = (IfNeOpCode)op;
                    if (CompareValues(Registers[ifop.First], Registers[ifop.Second]) != 0)
                        return ifop.GetTargetAddress();
                    break;
                }
                case Instructions.IfLt:
                {
                    var ifop = (IfLtOpCode)op;
                    if (CompareValues(Registers[ifop.First], Registers[ifop.Second]) < 0)
                        return ifop.GetTargetAddress();
                    break;
                }
                case Instructions.IfGe:
                {
                    var ifop = (IfGeOpCode)op;
                    if (CompareValues(Registers[ifop.First], Registers[ifop.Second]) >= 0)
                        return ifop.GetTargetAddress();
                    break;
                }
                case Instructions.IfGt:
                {
                    var ifop = (IfGtOpCode)op;
                    if (CompareValues(Registers[ifop.First], Registers[ifop.Second]) > 0)
                        return ifop.GetTargetAddress();
                    break;
                }
                case Instructions.IfLe:
                {
                    var ifop = (IfLeOpCode)op;
                    if (CompareValues(Registers[ifop.First], Registers[ifop.Second]) <= 0)
                        return ifop.GetTargetAddress();
                    break;
                }

                // ── IF-ZERO (one-register) ───────────────────────────────────
                case Instructions.IfEqz:
                {
                    var ifz = (IfEqzOpCode)op;
                    if (IsZeroOrNull(Registers[ifz.Destination]))
                        return ifz.GetTargetAddress();
                    break;
                }
                case Instructions.IfNez:
                {
                    var ifz = (IfNezOpCode)op;
                    if (!IsZeroOrNull(Registers[ifz.Destination]))
                        return ifz.GetTargetAddress();
                    break;
                }
                case Instructions.IfLtz:
                {
                    var ifz = (IfLtzOpCode)op;
                    if (ToInt(Registers[ifz.Destination]) < 0)
                        return ifz.GetTargetAddress();
                    break;
                }
                case Instructions.IfGez:
                {
                    var ifz = (IfGezOpCode)op;
                    if (ToInt(Registers[ifz.Destination]) >= 0)
                        return ifz.GetTargetAddress();
                    break;
                }
                case Instructions.IfGtz:
                {
                    var ifz = (IfGtzOpCode)op;
                    if (ToInt(Registers[ifz.Destination]) > 0)
                        return ifz.GetTargetAddress();
                    break;
                }
                case Instructions.IfLez:
                {
                    var ifz = (IfLezOpCode)op;
                    if (ToInt(Registers[ifz.Destination]) <= 0)
                        return ifz.GetTargetAddress();
                    break;
                }

                // ── ARRAY GET ────────────────────────────────────────────────
                case Instructions.Aget:
                case Instructions.AgetWide:
                case Instructions.AgetObject:
                case Instructions.AgetBoolean:
                case Instructions.AgetByte:
                case Instructions.AgetChar:
                case Instructions.AgetShort:
                {
                    var ag = (ArrayOpOpCode)op;
                    try
                    {
                        var arr = Registers[ag.Array] as object[];
                        int idx = ToInt(Registers[ag.Index]);
                        Registers[ag.Destination] = (arr != null && idx >= 0 && idx < arr.Length) ? arr[idx] : null;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[DalvikCPU] aget error: " + ex.Message);
                        Registers[ag.Destination] = null;
                    }
                    break;
                }

                // ── ARRAY PUT ────────────────────────────────────────────────
                case Instructions.Aput:
                case Instructions.AputWide:
                case Instructions.AputObject:
                case Instructions.AputBoolean:
                case Instructions.AputByte:
                case Instructions.AputChar:
                case Instructions.AputShort:
                {
                    var ap = (ArrayOpOpCode)op;
                    try
                    {
                        var arr = Registers[ap.Array] as object[];
                        int idx = ToInt(Registers[ap.Index]);
                        if (arr != null && idx >= 0 && idx < arr.Length)
                            arr[idx] = Registers[ap.Destination];
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[DalvikCPU] aput error: " + ex.Message);
                    }
                    break;
                }

                // ── INSTANCE GET ─────────────────────────────────────────────
                case Instructions.Iget:
                case Instructions.IgetWide:
                case Instructions.IgetObject:
                case Instructions.IgetBoolean:
                case Instructions.IgetByte:
                case Instructions.IgetChar:
                case Instructions.IgetShort:
                {
                    var ig = (IinstanceOpOpCode)op;
                    try
                    {
                        Field f = dex.GetField(ig.Index);
                        object obj2 = Registers[ig.Object];
                        Registers[ig.Destination] = GetInstanceField(obj2, f.Name);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[DalvikCPU] iget error: " + ex.Message);
                        Registers[ig.Destination] = null;
                    }
                    break;
                }

                // ── INSTANCE PUT ─────────────────────────────────────────────
                case Instructions.Iput:
                case Instructions.IputWide:
                case Instructions.IputObject:
                case Instructions.IputBoolean:
                case Instructions.IputByte:
                case Instructions.IputChar:
                case Instructions.IputShort:
                {
                    var ip = (IinstanceOpOpCode)op;
                    try
                    {
                        Field f = dex.GetField(ip.Index);
                        object obj2 = Registers[ip.Object];
                        SetInstanceField(obj2, f.Name, Registers[ip.Destination]);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[DalvikCPU] iput error: " + ex.Message);
                    }
                    break;
                }

                // ── STATIC GET ───────────────────────────────────────────────
                case Instructions.Sget:
                case Instructions.SgetWide:
                case Instructions.SgetObject:
                case Instructions.SgetBoolean:
                case Instructions.SgetByte:
                case Instructions.SgetChar:
                case Instructions.SgetShort:
                {
                    var sg = (StaticOpOpCode)op;
                    try
                    {
                        Field f = dex.GetField(sg.Index);
                        string className = dex.GetTypeName(f.ClassIndex);
                        string key = className + "." + f.Name;
                        Registers[sg.Destination] = staticFields.TryGetValue(key, out object val) ? val : null;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[DalvikCPU] sget error: " + ex.Message);
                        Registers[sg.Destination] = null;
                    }
                    break;
                }

                // ── STATIC PUT ───────────────────────────────────────────────
                case Instructions.Sput:
                case Instructions.SputWide:
                case Instructions.SputObject:
                case Instructions.SputBoolean:
                case Instructions.SputByte:
                case Instructions.SputChar:
                case Instructions.SputShort:
                {
                    var sp = (StaticOpOpCode)op;
                    try
                    {
                        Field f = dex.GetField(sp.Index);
                        string className = dex.GetTypeName(f.ClassIndex);
                        string key = className + "." + f.Name;
                        staticFields[key] = Registers[sp.Destination];
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[DalvikCPU] sput error: " + ex.Message);
                    }
                    break;
                }

                // ── INVOKE ───────────────────────────────────────────────────
                case Instructions.InvokeVirtual:
                    ExecuteInvoke((InvokeVirtualOpCode)op, cl);
                    break;

                case Instructions.InvokeSuper:
                    ExecuteInvoke((InvokeSuperOpCode)op, cl);
                    break;

                case Instructions.InvokeDirect:
                    ExecuteInvoke((InvokeDirectOpCode)op, cl);
                    break;

                case Instructions.InvokeStatic:
                    ExecuteInvoke((InvokeStaticOpCode)op, cl);
                    break;

                case Instructions.InvokeInterface:
                    ExecuteInvoke((InvokeInterfaceOpCode)op, cl);
                    break;

                // ── INVOKE RANGE ─────────────────────────────────────────────
                case Instructions.InvokeVirtualRange:
                case Instructions.InvokeSuperRange:
                case Instructions.InvokeDirectRange:
                case Instructions.InvokeStaticRange:
                case Instructions.InvokeInterfaceRange:
                    ExecuteInvokeRange((InvokeRangeOpCode)op, cl);
                    break;

                // ── ARITHMETIC (int) ─────────────────────────────────────────
                case Instructions.AddInt:
                    ExecuteBinaryOp(op, (a, b) => ToInt(a) + ToInt(b));
                    break;
                case Instructions.SubInt:
                    ExecuteBinaryOp(op, (a, b) => ToInt(a) - ToInt(b));
                    break;
                case Instructions.MulInt:
                    ExecuteBinaryOp(op, (a, b) => ToInt(a) * ToInt(b));
                    break;
                case Instructions.DivInt:
                    ExecuteBinaryOp(op, (a, b) => ToInt(b) != 0 ? ToInt(a) / ToInt(b) : 0);
                    break;
                case Instructions.RemInt:
                    ExecuteBinaryOp(op, (a, b) => ToInt(b) != 0 ? ToInt(a) % ToInt(b) : 0);
                    break;
                case Instructions.AndInt:
                    ExecuteBinaryOp(op, (a, b) => ToInt(a) & ToInt(b));
                    break;
                case Instructions.OrInt:
                    ExecuteBinaryOp(op, (a, b) => ToInt(a) | ToInt(b));
                    break;
                case Instructions.XorInt:
                    ExecuteBinaryOp(op, (a, b) => ToInt(a) ^ ToInt(b));
                    break;
                case Instructions.ShlInt:
                    ExecuteBinaryOp(op, (a, b) => ToInt(a) << ToInt(b));
                    break;
                case Instructions.ShrInt:
                    ExecuteBinaryOp(op, (a, b) => ToInt(a) >> ToInt(b));
                    break;
                case Instructions.UshrInt:
                    ExecuteBinaryOp(op, (a, b) => (int)((uint)ToInt(a) >> ToInt(b)));
                    break;

                // ── ARITHMETIC (int 2addr) ───────────────────────────────────
                case Instructions.AddInt2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToInt(a) + ToInt(b));
                    break;
                case Instructions.SubInt2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToInt(a) - ToInt(b));
                    break;
                case Instructions.MulInt2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToInt(a) * ToInt(b));
                    break;
                case Instructions.DivInt2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToInt(b) != 0 ? ToInt(a) / ToInt(b) : 0);
                    break;
                case Instructions.RemInt2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToInt(b) != 0 ? ToInt(a) % ToInt(b) : 0);
                    break;
                case Instructions.AndInt2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToInt(a) & ToInt(b));
                    break;
                case Instructions.OrInt2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToInt(a) | ToInt(b));
                    break;
                case Instructions.XorInt2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToInt(a) ^ ToInt(b));
                    break;
                case Instructions.ShlInt2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToInt(a) << ToInt(b));
                    break;
                case Instructions.ShrInt2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToInt(a) >> ToInt(b));
                    break;
                case Instructions.UshrInt2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => (int)((uint)ToInt(a) >> ToInt(b)));
                    break;

                // ── ARITHMETIC (int lit8) ────────────────────────────────────
                case Instructions.AddIntLit8:
                    ExecuteLitOp8(op, (a, c) => ToInt(a) + c);
                    break;
                case Instructions.RsubIntLit8:
                    ExecuteLitOp8(op, (a, c) => c - ToInt(a));
                    break;
                case Instructions.MulIntLit8:
                    ExecuteLitOp8(op, (a, c) => ToInt(a) * c);
                    break;
                case Instructions.DivIntLit8:
                    ExecuteLitOp8(op, (a, c) => c != 0 ? ToInt(a) / c : 0);
                    break;
                case Instructions.RemIntLit8:
                    ExecuteLitOp8(op, (a, c) => c != 0 ? ToInt(a) % c : 0);
                    break;
                case Instructions.AndIntLit8:
                    ExecuteLitOp8(op, (a, c) => ToInt(a) & c);
                    break;
                case Instructions.OrIntLit8:
                    ExecuteLitOp8(op, (a, c) => ToInt(a) | c);
                    break;
                case Instructions.XorIntLit8:
                    ExecuteLitOp8(op, (a, c) => ToInt(a) ^ c);
                    break;
                case Instructions.ShlIntLit8:
                    ExecuteLitOp8(op, (a, c) => ToInt(a) << c);
                    break;
                case Instructions.ShrIntLit8:
                    ExecuteLitOp8(op, (a, c) => ToInt(a) >> c);
                    break;
                case Instructions.UshrIntLit8:
                    ExecuteLitOp8(op, (a, c) => (int)((uint)ToInt(a) >> c));
                    break;

                // ── ARITHMETIC (int lit16) ───────────────────────────────────
                case Instructions.AddIntLit16:
                    ExecuteLitOp16(op, (a, c) => ToInt(a) + c);
                    break;
                case Instructions.RsubInt:
                    ExecuteLitOp16(op, (a, c) => c - ToInt(a));
                    break;
                case Instructions.MulIntLit16:
                    ExecuteLitOp16(op, (a, c) => ToInt(a) * c);
                    break;
                case Instructions.DivIntLit16:
                    ExecuteLitOp16(op, (a, c) => c != 0 ? ToInt(a) / c : 0);
                    break;
                case Instructions.RemIntLit16:
                    ExecuteLitOp16(op, (a, c) => c != 0 ? ToInt(a) % c : 0);
                    break;
                case Instructions.AndIntLit16:
                    ExecuteLitOp16(op, (a, c) => ToInt(a) & c);
                    break;
                case Instructions.OrIntLit16:
                    ExecuteLitOp16(op, (a, c) => ToInt(a) | c);
                    break;
                case Instructions.XorIntLit16:
                    ExecuteLitOp16(op, (a, c) => ToInt(a) ^ c);
                    break;

                // ── ARITHMETIC (long) ────────────────────────────────────────
                case Instructions.AddLong:
                    ExecuteBinaryOp(op, (a, b) => ToLong(a) + ToLong(b));
                    break;
                case Instructions.SubLong:
                    ExecuteBinaryOp(op, (a, b) => ToLong(a) - ToLong(b));
                    break;
                case Instructions.MulLong:
                    ExecuteBinaryOp(op, (a, b) => ToLong(a) * ToLong(b));
                    break;
                case Instructions.DivLong:
                    ExecuteBinaryOp(op, (a, b) => ToLong(b) != 0 ? ToLong(a) / ToLong(b) : 0L);
                    break;
                case Instructions.RemLong:
                    ExecuteBinaryOp(op, (a, b) => ToLong(b) != 0 ? ToLong(a) % ToLong(b) : 0L);
                    break;
                case Instructions.AndLong:
                    ExecuteBinaryOp(op, (a, b) => ToLong(a) & ToLong(b));
                    break;
                case Instructions.OrLong:
                    ExecuteBinaryOp(op, (a, b) => ToLong(a) | ToLong(b));
                    break;
                case Instructions.XorLong:
                    ExecuteBinaryOp(op, (a, b) => ToLong(a) ^ ToLong(b));
                    break;
                case Instructions.ShlLong:
                    ExecuteBinaryOp(op, (a, b) => ToLong(a) << ToInt(b));
                    break;
                case Instructions.ShrLong:
                    ExecuteBinaryOp(op, (a, b) => ToLong(a) >> ToInt(b));
                    break;
                case Instructions.UshrLong:
                    ExecuteBinaryOp(op, (a, b) => (long)((ulong)ToLong(a) >> ToInt(b)));
                    break;

                // ── ARITHMETIC (long 2addr) ──────────────────────────────────
                case Instructions.AddLong2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToLong(a) + ToLong(b));
                    break;
                case Instructions.SubLong2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToLong(a) - ToLong(b));
                    break;
                case Instructions.MulLong2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToLong(a) * ToLong(b));
                    break;
                case Instructions.DivLong2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToLong(b) != 0 ? ToLong(a) / ToLong(b) : 0L);
                    break;
                case Instructions.RemLong2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToLong(b) != 0 ? ToLong(a) % ToLong(b) : 0L);
                    break;
                case Instructions.AndLong2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToLong(a) & ToLong(b));
                    break;
                case Instructions.OrLong2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToLong(a) | ToLong(b));
                    break;
                case Instructions.XorLong2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToLong(a) ^ ToLong(b));
                    break;
                case Instructions.ShlLong2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToLong(a) << ToInt(b));
                    break;
                case Instructions.ShrLong2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToLong(a) >> ToInt(b));
                    break;
                case Instructions.UshrLong2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => (long)((ulong)ToLong(a) >> ToInt(b)));
                    break;

                // ── ARITHMETIC (float) ───────────────────────────────────────
                case Instructions.AddFloat:
                    ExecuteBinaryOp(op, (a, b) => ToFloat(a) + ToFloat(b));
                    break;
                case Instructions.SubFloat:
                    ExecuteBinaryOp(op, (a, b) => ToFloat(a) - ToFloat(b));
                    break;
                case Instructions.MulFloat:
                    ExecuteBinaryOp(op, (a, b) => ToFloat(a) * ToFloat(b));
                    break;
                case Instructions.DivFloat:
                    ExecuteBinaryOp(op, (a, b) => Math.Abs(ToFloat(b)) > float.Epsilon ? ToFloat(a) / ToFloat(b) : 0f);
                    break;
                case Instructions.RemFloat:
                    ExecuteBinaryOp(op, (a, b) => ToFloat(a) % ToFloat(b));
                    break;

                // ── ARITHMETIC (float 2addr) ─────────────────────────────────
                case Instructions.AddFloat2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToFloat(a) + ToFloat(b));
                    break;
                case Instructions.SubFloat2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToFloat(a) - ToFloat(b));
                    break;
                case Instructions.MulFloat2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToFloat(a) * ToFloat(b));
                    break;
                case Instructions.DivFloat2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => Math.Abs(ToFloat(b)) > float.Epsilon ? ToFloat(a) / ToFloat(b) : 0f);
                    break;
                case Instructions.RemFloat2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToFloat(a) % ToFloat(b));
                    break;

                // ── ARITHMETIC (double) ──────────────────────────────────────
                case Instructions.AddDouble:
                    ExecuteBinaryOp(op, (a, b) => ToDouble(a) + ToDouble(b));
                    break;
                case Instructions.SubDouble:
                    ExecuteBinaryOp(op, (a, b) => ToDouble(a) - ToDouble(b));
                    break;
                case Instructions.MulDouble:
                    ExecuteBinaryOp(op, (a, b) => ToDouble(a) * ToDouble(b));
                    break;
                case Instructions.DivDouble:
                    ExecuteBinaryOp(op, (a, b) => Math.Abs(ToDouble(b)) > double.Epsilon ? ToDouble(a) / ToDouble(b) : 0.0);
                    break;
                case Instructions.RemDouble:
                    ExecuteBinaryOp(op, (a, b) => ToDouble(a) % ToDouble(b));
                    break;

                // ── ARITHMETIC (double 2addr) ────────────────────────────────
                case Instructions.AddDouble2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToDouble(a) + ToDouble(b));
                    break;
                case Instructions.SubDouble2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToDouble(a) - ToDouble(b));
                    break;
                case Instructions.MulDouble2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToDouble(a) * ToDouble(b));
                    break;
                case Instructions.DivDouble2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => Math.Abs(ToDouble(b)) > double.Epsilon ? ToDouble(a) / ToDouble(b) : 0.0);
                    break;
                case Instructions.RemDouble2Addr:
                    ExecuteBinaryOp2Addr(op, (a, b) => ToDouble(a) % ToDouble(b));
                    break;

                // ── UNARY OPS ────────────────────────────────────────────────
                case Instructions.NegInt:
                    ExecuteUnaryOp(op, a => -ToInt(a));
                    break;
                case Instructions.NotInt:
                    ExecuteUnaryOp(op, a => ~ToInt(a));
                    break;
                case Instructions.NegLong:
                    ExecuteUnaryOp(op, a => -ToLong(a));
                    break;
                case Instructions.NotLong:
                    ExecuteUnaryOp(op, a => ~ToLong(a));
                    break;
                case Instructions.NegFloat:
                    ExecuteUnaryOp(op, a => -ToFloat(a));
                    break;
                case Instructions.NegDouble:
                    ExecuteUnaryOp(op, a => -ToDouble(a));
                    break;

                // ── TYPE CONVERSIONS ─────────────────────────────────────────
                case Instructions.IntToLong:
                    ExecuteUnaryOp(op, a => (long)ToInt(a));
                    break;
                case Instructions.IntToFloat:
                    ExecuteUnaryOp(op, a => (float)ToInt(a));
                    break;
                case Instructions.IntToDouble:
                    ExecuteUnaryOp(op, a => (double)ToInt(a));
                    break;
                case Instructions.LongToInt:
                    ExecuteUnaryOp(op, a => (int)ToLong(a));
                    break;
                case Instructions.LongToFloat:
                    ExecuteUnaryOp(op, a => (float)ToLong(a));
                    break;
                case Instructions.LongToDouble:
                    ExecuteUnaryOp(op, a => (double)ToLong(a));
                    break;
                case Instructions.FloatToInt:
                    ExecuteUnaryOp(op, a => (int)ToFloat(a));
                    break;
                case Instructions.FloatToLong:
                    ExecuteUnaryOp(op, a => (long)ToFloat(a));
                    break;
                case Instructions.FloatToDouble:
                    ExecuteUnaryOp(op, a => (double)ToFloat(a));
                    break;
                case Instructions.DoubleToInt:
                    ExecuteUnaryOp(op, a => (int)ToDouble(a));
                    break;
                case Instructions.DoubleToLong:
                    ExecuteUnaryOp(op, a => (long)ToDouble(a));
                    break;
                case Instructions.DoubleToFloat:
                    ExecuteUnaryOp(op, a => (float)ToDouble(a));
                    break;
                case Instructions.IntToByte:
                    ExecuteUnaryOp(op, a => (int)(byte)ToInt(a));
                    break;
                case Instructions.IntToChar:
                    ExecuteUnaryOp(op, a => (int)(char)ToInt(a));
                    break;
                case Instructions.IntToShort:
                    ExecuteUnaryOp(op, a => (int)(short)ToInt(a));
                    break;

                default:
#if DEBUG
                    TraceInstruction("Unhandled instruction: " + op.Instruction + " at offset 0x" + op.OpCodeOffset.ToString("X"));
#endif
                    break;
            }

            return ExecutionSignals.CONTINUE;
        }

        // TryNativeMethod — intercepts calls to known Android framework methods.
        // Returns true if the method was handled, false if the caller should fall through
        // to DEX bytecode execution.
        private bool TryNativeMethod(Method m, Class c, params object[] obj)
        {
            // ── setContentView ───────────────────────────────────────────
            if (m.Name.Contains("setContentView"))
            {
                try
                {
                    object arg0 = obj != null && obj.Length > 0 ? obj[0] : null;
                    if (arg0 is int layoutResID)
                    {
                        Debug.WriteLine("[DalvikCPU] setContentView(int=" + layoutResID + ")");
                        droidWindow.setContentView(layoutResID);
                    }
                    else if (arg0 is AndroidInteropLib.android.view.View viewArg)
                    {
                        Debug.WriteLine("[DalvikCPU] setContentView(View=" + viewArg.GetType().Name + ")");
                        droidWindow.setContentView(viewArg);
                    }
                    else if (arg0 is DalvikObject dalvikView)
                    {
                        Debug.WriteLine("[DalvikCPU] setContentView(DalvikObject=" + dalvikView.TypeName + ") - creating native render surface.");
                        var surface = new AndroidRenderSurface();
                        surface.HorizontalAlignment = HorizontalAlignment.Stretch;
                        surface.VerticalAlignment = VerticalAlignment.Stretch;
                        hostPage.SetNativeRenderSurface(surface);
                    }
                    else if (arg0 != null)
                    {
                        Debug.WriteLine("[DalvikCPU] setContentView(arg=" + arg0.GetType().Name + " value=" + arg0 + ")");
                        int resId = ToInt(arg0);
                        if (resId != 0)
                            droidWindow.setContentView(resId);
                    }
                    else
                    {
                        Debug.WriteLine("[DalvikCPU] setContentView called with null argument, skipping.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[DalvikCPU] setContentView error: " + ex.Message);
                }
                return true;
            }

            // ── Resolve the class name for further dispatch ──────────────
            string className;
            try { className = dex.GetTypeName(m.ClassIndex); }
            catch { className = c.Name; }

            string fullMethodKey = className + "." + m.Name;

            // ── getString(int) — return string resource by ID ────────────
            if (m.Name == "getString")
            {
                int resId = obj != null && obj.Length > 0 ? ToInt(obj[0]) : 0;
                if (stringResources.TryGetValue(resId, out string strVal))
                    result = strVal;
                else
                    result = "res_" + resId;
#if DEBUG
                TraceInstruction($"getString({resId}) => \"{result}\"");
#endif
                return true;
            }

            // ── getResources() — return a stub resources object ──────────
            if (m.Name == "getResources")
            {
                result = new DalvikObject("android.content.res.Resources");
#if DEBUG
                TraceInstruction("getResources() => stub Resources");
#endif
                return true;
            }

            // ── getApplicationContext() — return our context proxy ────────
            if (m.Name == "getApplicationContext" || m.Name == "getBaseContext")
            {
                result = appContext ?? (object)new DalvikObject("android.content.Context");
#if DEBUG
                TraceInstruction(m.Name + "() => appContext");
#endif
                return true;
            }

            // ── getPackageName() ─────────────────────────────────────────
            if (m.Name == "getPackageName")
            {
                result = packageName;
                return true;
            }

            // ── getWindowManager() ───────────────────────────────────────
            if (m.Name == "getWindowManager")
            {
                result = new DalvikObject("android.view.WindowManager");
                return true;
            }

            // ── getWindow() ──────────────────────────────────────────────
            if (m.Name == "getWindow")
            {
                result = droidWindow ?? (object)new DalvikObject("android.view.Window");
                return true;
            }

            // ── getSystemService(String) ─────────────────────────────────
            if (m.Name == "getSystemService")
            {
                string service = obj != null && obj.Length > 0 ? obj[0] as string ?? "" : "";
#if DEBUG
                TraceInstruction("getSystemService(\"" + service + "\")");
#endif
                result = new DalvikObject("android.os." + service);
                return true;
            }

            // ── getAssets() ──────────────────────────────────────────────
            if (m.Name == "getAssets")
            {
                result = new DalvikObject("android.content.res.AssetManager");
                return true;
            }

            // ── getFilesDir() / getCacheDir() / getExternalFilesDir() ────
            if (m.Name == "getFilesDir" || m.Name == "getCacheDir" || m.Name == "getExternalFilesDir" ||
                m.Name == "getExternalCacheDir" || m.Name == "getCodeCacheDir" || m.Name == "getNoBackupFilesDir" ||
                m.Name == "getDataDir")
            {
                string path = da.localAppRoot != null ? da.localAppRoot.Path : "/data/data/" + packageName;
                result = new DalvikObject("java.io.File");
                SetInstanceField(result, "path", path);
#if DEBUG
                TraceInstruction(m.Name + "() => " + path);
#endif
                return true;
            }

            // ── getApplicationInfo() ─────────────────────────────────────
            if (m.Name == "getApplicationInfo")
            {
                var appInfo = new DalvikObject("android.content.pm.ApplicationInfo");
                string dataDir = da.localAppRoot != null ? da.localAppRoot.Path : "/data/data/" + packageName;
                SetInstanceField(appInfo, "dataDir", dataDir);
                SetInstanceField(appInfo, "nativeLibraryDir", dataDir + "/lib");
                SetInstanceField(appInfo, "sourceDir", dataDir + "/base.apk");
                SetInstanceField(appInfo, "packageName", packageName);
                SetInstanceField(appInfo, "targetSdkVersion", 28);
                SetInstanceField(appInfo, "flags", 0);
                result = appInfo;
                return true;
            }

            // ── getPackageManager() ──────────────────────────────────────
            if (m.Name == "getPackageManager")
            {
                result = new DalvikObject("android.content.pm.PackageManager");
                return true;
            }

            // ── getSharedPreferences(String, int) ────────────────────────
            if (m.Name == "getSharedPreferences")
            {
                string name = obj != null && obj.Length > 0 ? obj[0] as string ?? "prefs" : "prefs";
                result = new DalvikObject("android.content.SharedPreferences");
#if DEBUG
                TraceInstruction("getSharedPreferences(\"" + name + "\")");
#endif
                return true;
            }

            // ── getClassLoader() ─────────────────────────────────────────
            if (m.Name == "getClassLoader")
            {
                result = new DalvikObject("java.lang.ClassLoader");
                return true;
            }

            // ── getContentResolver() ─────────────────────────────────────
            if (m.Name == "getContentResolver")
            {
                result = new DalvikObject("android.content.ContentResolver");
                return true;
            }

            // ── Activity lifecycle stubs ─────────────────────────────────
            if (m.Name == "finish")
            {
                Debug.WriteLine("[DalvikCPU] Activity.finish() called");
                return true;
            }
            if (m.Name == "runOnUiThread")
            {
                Debug.WriteLine("[DalvikCPU] runOnUiThread (stub - sync execution)");
                return true;
            }

            // ── Resources methods ────────────────────────────────────────
            if (m.Name == "getDisplayMetrics")
            {
                var dm = new DalvikObject("android.util.DisplayMetrics");
                SetInstanceField(dm, "widthPixels", 1280);
                SetInstanceField(dm, "heightPixels", 720);
                SetInstanceField(dm, "density", 1.0f);
                SetInstanceField(dm, "densityDpi", 160);
                SetInstanceField(dm, "scaledDensity", 1.0f);
                SetInstanceField(dm, "xdpi", 160.0f);
                SetInstanceField(dm, "ydpi", 160.0f);
                result = dm;
                return true;
            }

            if (m.Name == "getConfiguration")
            {
                result = new DalvikObject("android.content.res.Configuration");
                return true;
            }

            // ── Display methods ──────────────────────────────────────────
            if (m.Name == "getDefaultDisplay")
            {
                var display = new DalvikObject("android.view.Display");
                SetInstanceField(display, "width", 1280);
                SetInstanceField(display, "height", 720);
                result = display;
                return true;
            }
            if (m.Name == "getMetrics" && obj != null && obj.Length > 0)
            {
                // Populate the DisplayMetrics parameter
                object dmObj = obj[0];
                if (dmObj != null)
                {
                    SetInstanceField(dmObj, "widthPixels", 1280);
                    SetInstanceField(dmObj, "heightPixels", 720);
                    SetInstanceField(dmObj, "density", 1.0f);
                    SetInstanceField(dmObj, "densityDpi", 160);
                    SetInstanceField(dmObj, "scaledDensity", 1.0f);
                    SetInstanceField(dmObj, "xdpi", 160.0f);
                    SetInstanceField(dmObj, "ydpi", 160.0f);
                }
                return true;
            }
            if (m.Name == "getWidth" || m.Name == "getHeight")
            {
                // Display or View dimension queries
                result = m.Name == "getWidth" ? (object)1280 : (object)720;
                return true;
            }
            if (m.Name == "getRotation")
            {
                result = 0; // ROTATION_0
                return true;
            }

            // ── Window methods ───────────────────────────────────────────
            if (m.Name == "getDecorView")
            {
                result = new DalvikObject("android.view.View");
                return true;
            }
            if (m.Name == "setFlags" || m.Name == "addFlags" || m.Name == "clearFlags")
            {
#if DEBUG
                TraceInstruction("Window." + m.Name + " (stub)");
#endif
                return true;
            }
            if (m.Name == "setFormat")
            {
                return true; // stub: pixel format
            }
            if (m.Name == "requestWindowFeature" || m.Name == "requestFeature")
            {
                result = true;
                return true;
            }

            // ── View/ViewGroup methods ───────────────────────────────────
            if (m.Name == "addView" || m.Name == "removeView" || m.Name == "removeAllViews")
            {
#if DEBUG
                TraceInstruction("ViewGroup." + m.Name + " (stub)");
#endif
                return true;
            }
            if (m.Name == "setVisibility" || m.Name == "setEnabled" || m.Name == "setClickable" ||
                m.Name == "setFocusable" || m.Name == "setFocusableInTouchMode")
            {
                return true; // View property stubs
            }
            if (m.Name == "setLayoutParams" || m.Name == "getLayoutParams")
            {
                if (m.Name == "getLayoutParams")
                    result = new DalvikObject("android.view.ViewGroup$LayoutParams");
                return true;
            }
            if (m.Name == "setId" || m.Name == "getId")
            {
                if (m.Name == "getId")
                    result = 0;
                return true;
            }
            if (m.Name == "setBackgroundColor" || m.Name == "setBackgroundResource" || m.Name == "setBackground")
            {
                return true;
            }

            // ── GLSurfaceView methods ────────────────────────────────────
            if (m.Name == "setRenderer")
            {
                Debug.WriteLine("[DalvikCPU] GLSurfaceView.setRenderer called");
                return true;
            }
            if (m.Name == "setEGLContextClientVersion")
            {
#if DEBUG
                int ver = obj != null && obj.Length > 0 ? ToInt(obj[0]) : 0;
                TraceInstruction("GLSurfaceView.setEGLContextClientVersion(" + ver + ")");
#endif
                return true;
            }
            if (m.Name == "setEGLConfigChooser" || m.Name == "setPreserveEGLContextOnPause" ||
                m.Name == "setRenderMode")
            {
                return true;
            }
            if (m.Name == "requestRender" || m.Name == "queueEvent")
            {
                return true;
            }

            // ── Audio stubs ──────────────────────────────────────────────
            if (m.Name == "getMinBufferSize" || m.Name == "getMaxVolume" || m.Name == "getStreamVolume")
            {
                result = 4096; // reasonable default buffer/volume
                return true;
            }

            // ── System.loadLibrary (already handled by JNI but intercept for safety)
            if (m.Name == "loadLibrary")
            {
                string libName = obj != null && obj.Length > 0 ? obj[0] as string ?? "" : "";
                Debug.WriteLine("[DalvikCPU] System.loadLibrary(\"" + libName + "\") - stub");
                return true;
            }

            // ── Log methods ──────────────────────────────────────────────
            if (className != null && className.Contains("android.util.Log"))
            {
                string tag = obj != null && obj.Length > 0 ? obj[0] as string ?? "" : "";
                string msg = obj != null && obj.Length > 1 ? obj[1] as string ?? "" : "";
                Debug.WriteLine("[Android.Log." + m.Name + "] " + tag + ": " + msg);
                result = 0;
                return true;
            }

            // ── StringBuilder methods ────────────────────────────────────
            if (className != null && className.Contains("StringBuilder"))
            {
                if (m.Name == "append" || m.Name == "toString" || m.Name == "<init>")
                    return false; // let DEX handle via DalvikObject fields
            }

            // ── SharedPreferences stubs ──────────────────────────────────
            if (m.Name == "edit")
            {
                if (className != null && className.Contains("SharedPreferences"))
                {
                    result = new DalvikObject("android.content.SharedPreferences$Editor");
                    return true;
                }
            }
            if (m.Name == "putString" || m.Name == "putInt" || m.Name == "putBoolean" ||
                m.Name == "putFloat" || m.Name == "putLong" || m.Name == "putStringSet")
            {
                // SharedPreferences.Editor methods — return self for chaining
                if (obj != null && obj.Length > 0)
                    result = obj[0]; // 'this' reference
                else
                    result = new DalvikObject("android.content.SharedPreferences$Editor");
                return true;
            }
            if (m.Name == "commit" || m.Name == "apply")
            {
                result = true;
                return true;
            }
            if (m.Name == "getBoolean") { result = false; return true; }
            if (m.Name == "getInt") { result = 0; return true; }
            if (m.Name == "getFloat") { result = 0.0f; return true; }
            if (m.Name == "getLong") { result = 0L; return true; }

            // ── java.io.File methods ─────────────────────────────────────
            if (m.Name == "getAbsolutePath" || m.Name == "getPath" || m.Name == "toString")
            {
                if (className != null && className.Contains("java.io.File"))
                {
                    result = GetInstanceField(obj != null && obj.Length > 0 ? obj[0] : null, "path") ?? "/data/data/" + packageName;
                    return true;
                }
            }
            if (m.Name == "exists" || m.Name == "isDirectory" || m.Name == "isFile")
            {
                if (className != null && className.Contains("java.io.File"))
                {
                    result = true;
                    return true;
                }
            }
            if (m.Name == "mkdirs" || m.Name == "mkdir")
            {
                if (className != null && className.Contains("java.io.File"))
                {
                    result = true;
                    return true;
                }
            }

            // ── onCreate/onResume/onPause lifecycle stubs ────────────────
            if (m.Name == "onResume" || m.Name == "onPause" || m.Name == "onDestroy" ||
                m.Name == "onStop" || m.Name == "onStart" || m.Name == "onRestart")
            {
                // Activity lifecycle methods on framework classes — stub them
                if (className != null && !className.StartsWith(packageName))
                {
#if DEBUG
                    TraceInstruction("Lifecycle stub: " + fullMethodKey);
#endif
                    return true;
                }
            }

            // ── Fallback to managed reflection (existing logic) ──────────
            string convertedName = ConvertClassName(c.Name);
            if (convertedName.StartsWith(packageName))
                return false;

            Type myType = Type.GetType("AndroidInteropLib." + convertedName);
            if (myType != null)
            {
                TypeInfo info = myType.GetTypeInfo();
                MethodInfo mi = info.GetDeclaredMethod(m.Name);
                if (mi != null)
                {
                    try
                    {
                        mi.Invoke(this, obj);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }

            // ── Constructor calls (<init>) on framework classes ──────────
            // If a constructor is called on a DalvikObject for a known framework class,
            // just return true to avoid crashing on missing DEX code for the constructor.
            if (m.Name == "<init>" && className != null && !className.StartsWith(packageName))
            {
#if DEBUG
                TraceInstruction("Constructor stub: " + className + ".<init>");
#endif
                return true;
            }

            return false;
        }

        // ConvertClassName
        private string ConvertClassName(string s)
        {
            return s.Replace("internal", "_internal");
        }

        // party rockers in the house tonight!!!!
        private void ExecuteInvoke(InvokeOpCode invokeOp, Class cl)
        {
            try
            {
                Method m = dex.GetMethod(invokeOp.MethodIndex);
                object[] args = new object[Math.Max(0, invokeOp.ArgumentRegisters.Length - 1)];
                for (int i = 1; i < invokeOp.ArgumentRegisters.Length; i++)
                {
                    int regIdx = invokeOp.ArgumentRegisters[i];
                    args[i - 1] = regIdx < Registers.Length ? Registers[regIdx] : null;
                }

#if DEBUG
                string invokeTypeName;
                try { invokeTypeName = dex.GetTypeName(m.ClassIndex); } catch { invokeTypeName = "(unknown)"; }
                TraceInstruction(invokeOp.Instruction + " " + invokeTypeName + "." + m.Name + " args=" + args.Length);
#endif
                if (!TryNativeMethod(m, cl, args))
                    result = RunMethod(m, cl, args);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DalvikCPU] " + invokeOp.Instruction + " exception: " + ex.Message);
#if DEBUG
                Debug.WriteLine("[DalvikCPU]   Stack: " + ex.StackTrace);
#endif
            }
        }

        private void ExecuteInvokeRange(InvokeRangeOpCode rangeOp, Class cl)
        {
            try
            {
                Method m = dex.GetMethod(rangeOp.MethodIndex);
                int count = Math.Max(0, rangeOp.ArgumentCount - 1);
                object[] args = new object[count];
                for (int i = 0; i < count; i++)
                {
                    int regIdx = rangeOp.FirstArgument + 1 + i;
                    args[i] = regIdx < Registers.Length ? Registers[regIdx] : null;
                }

#if DEBUG
                string rangeTypeName;
                try { rangeTypeName = dex.GetTypeName(m.ClassIndex); } catch { rangeTypeName = "(unknown)"; }
                TraceInstruction("invoke-range " + rangeTypeName + "." + m.Name + " args=" + count);
#endif

                if (!TryNativeMethod(m, cl, args))
                    result = RunMethod(m, cl, args);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DalvikCPU] invoke-range exception: " + ex.Message);
#if DEBUG
                Debug.WriteLine("[DalvikCPU]   Stack: " + ex.StackTrace);
#endif
            }
        }

        // Execute binary arithmetic 3-register opcodes (Destination = FirstSource op SecondSource)
        private void ExecuteBinaryOp(OpCode op, Func<object, object, object> operation)
        {
            try
            {
                var bop = (BinaryOpOpCode)op;
                Registers[bop.Destination] = operation(Registers[bop.First], Registers[bop.Second]);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DalvikCPU] BinaryOp error: " + ex.Message);
            }
        }

        // Execute binary arithmetic 2-address opcodes (Destination op= Source)
        private void ExecuteBinaryOp2Addr(OpCode op, Func<object, object, object> operation)
        {
            try
            {
                var bop = (BinaryOp2OpCode)op;
                Registers[bop.Destination] = operation(Registers[bop.Destination], Registers[bop.Source]);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DalvikCPU] BinaryOp2Addr error: " + ex.Message);
            }
        }

        // Execute int/lit8 operations (Destination = Source op sbyte-literal)
        private void ExecuteLitOp8(OpCode op, Func<object, int, object> operation)
        {
            try
            {
                var lit = (BinaryOpLit8OpCode)op;
                Registers[lit.Destination] = operation(Registers[lit.Source], (int)lit.Constant);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DalvikCPU] LitOp8 error: " + ex.Message);
            }
        }

        // Execute int/lit16 operations (Destination = Source op short-literal)
        private void ExecuteLitOp16(OpCode op, Func<object, int, object> operation)
        {
            try
            {
                var lit = (BinaryOpLit16OpCode)op;
                Registers[lit.Destination] = operation(Registers[lit.Source], (int)lit.Constant);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DalvikCPU] LitOp16 error: " + ex.Message);
            }
        }

        // Execute unary operations
        private void ExecuteUnaryOp(OpCode op, Func<object, object> operation)
        {
            try
            {
                var uop = (UnaryOpOpCode)op;
                Registers[uop.Destination] = operation(Registers[uop.Source]);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DalvikCPU] UnaryOp error: " + ex.Message);
            }
        }

        // ── Type conversion helpers ──────────────────────────────────────
        private static int ToInt(object o)
        {
            if (o == null) return 0;
            if (o is int i) return i;
            if (o is long l) return (int)l;
            if (o is float f) return (int)f;
            if (o is double d) return (int)d;
            if (o is short s) return s;
            if (o is byte b) return b;
            if (o is bool bo) return bo ? 1 : 0;
            try { return Convert.ToInt32(o); } catch { return 0; }
        }

        private static long ToLong(object o)
        {
            if (o == null) return 0L;
            if (o is long l) return l;
            if (o is int i) return i;
            if (o is double d) return (long)d;
            if (o is float f) return (long)f;
            try { return Convert.ToInt64(o); } catch { return 0L; }
        }

        private static float ToFloat(object o)
        {
            if (o == null) return 0f;
            if (o is float f) return f;
            if (o is int i) return i;
            if (o is double d) return (float)d;
            if (o is long l) return l;
            try { return Convert.ToSingle(o); } catch { return 0f; }
        }

        private static double ToDouble(object o)
        {
            if (o == null) return 0.0;
            if (o is double d) return d;
            if (o is float f) return f;
            if (o is int i) return i;
            if (o is long l) return l;
            try { return Convert.ToDouble(o); } catch { return 0.0; }
        }

        // Compare two register values for if-* instructions
        private static int CompareValues(object a, object b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return -1;
            if (b == null) return 1;
            if (a is int ai && b is int bi) return ai.CompareTo(bi);
            if (a is long al && b is long bl) return al.CompareTo(bl);
            if (a is float af && b is float bf) return af.CompareTo(bf);
            if (a is double ad && b is double bd) return ad.CompareTo(bd);
            // Reference comparison
            return ReferenceEquals(a, b) ? 0 : 1;
        }

        // Check if register value is zero/null for if-*z instructions
        private static bool IsZeroOrNull(object o)
        {
            if (o == null) return true;
            if (o is int i) return i == 0;
            if (o is long l) return l == 0;
            if (o is float f) return f == 0f;
            if (o is double d) return d == 0.0;
            if (o is bool b) return !b;
            return false;
        }

        // ── Instance field storage helpers ───────────────────────────────
        private int GetObjectId(object obj)
        {
            if (obj == null) return 0;
            if (!objectIds.TryGetValue(obj, out int id))
            {
                id = nextObjectId++;
                objectIds[obj] = id;
            }
            return id;
        }

        private object GetInstanceField(object obj, string fieldName)
        {
            int id = GetObjectId(obj);
            if (id == 0) return null;

            // DalvikObject has its own property storage — check that first
            if (obj is DalvikObject dobj)
            {
                if (dobj.Properties.TryGetValue(fieldName, out object dVal))
                    return dVal;
            }

            // Try managed .NET reflection
            if (obj != null && !(obj is DalvikObject))
            {
                try
                {
                    var fi = obj.GetType().GetField(fieldName);
                    if (fi != null) return fi.GetValue(obj);
                    var pi = obj.GetType().GetProperty(fieldName);
                    if (pi != null) return pi.GetValue(obj);
                }
                catch { }
            }

            // Fall back to our dalvik field dictionary
            if (instanceFields.TryGetValue(id, out var fields) && fields.TryGetValue(fieldName, out object val))
                return val;
            return null;
        }

        private void SetInstanceField(object obj, string fieldName, object value)
        {
            int id = GetObjectId(obj);
            if (id == 0) return;

            // DalvikObject has its own property storage — use it directly
            if (obj is DalvikObject dobj)
            {
                dobj.Properties[fieldName] = value;
                return;
            }

            // Try managed .NET reflection
            if (obj != null)
            {
                try
                {
                    var fi = obj.GetType().GetField(fieldName);
                    if (fi != null) { fi.SetValue(obj, value); return; }
                    var pi = obj.GetType().GetProperty(fieldName);
                    if (pi != null) { pi.SetValue(obj, value); return; }
                }
                catch { }
            }

            // Fall back to our dalvik field dictionary
            if (!instanceFields.TryGetValue(id, out var fields))
            {
                fields = new Dictionary<string, object>();
                instanceFields[id] = fields;
            }
            fields[fieldName] = value;
        }

        // ── Native library scanning (FLinux ElfExecutor + apkenv-inspired) ────
        private Task ScanNativeLibraries()
        {
            if (da.localAppRoot == null)
                return Task.CompletedTask;

            // Determine ABI directory and execution mode.
            string abiName = XboxPlatform.GetAndroidAbiName();
            string libPath = Path.Combine(da.localAppRoot.Path, "lib", abiName);

            // Determine the best execution mode based on what ABI directories exist.
            ExecutionMode execMode = hostPage.SelectedExecutionMode;

            try
            {
                if (!Directory.Exists(libPath))
                {
                    // Try to find a suitable ABI directory and auto-set execution mode.
                    var abiCandidates = new[]
                    {
                        new KeyValuePair<string, ExecutionMode>("arm64-v8a", ExecutionMode.Arm64),
                        new KeyValuePair<string, ExecutionMode>("armeabi-v7a", ExecutionMode.Arm32),
                        new KeyValuePair<string, ExecutionMode>("armeabi", ExecutionMode.Arm32),
                        new KeyValuePair<string, ExecutionMode>("x86_64", ExecutionMode.X64),
                        new KeyValuePair<string, ExecutionMode>("x86", ExecutionMode.X64),
                    };
                    foreach (var abiCandidate in abiCandidates)
                    {
                        string fallback = abiCandidate.Key;
                        ExecutionMode mode = abiCandidate.Value;
                        string altPath = Path.Combine(da.localAppRoot.Path, "lib", fallback);
                        if (Directory.Exists(altPath))
                        {
                            libPath = altPath;
                            // Auto-switch the execution mode only when the current mode was not
                            // explicitly changed by the user (i.e. it still holds the default ARM32
                            // value).  A user-selected mode (set via the EmuPage ComboBox) is
                            // honoured regardless of which ABI folders are available.
                            if (execMode == ExecutionMode.Arm32 && mode != ExecutionMode.Arm32)
                            {
                                execMode = mode;
                                Debug.WriteLine($"[DalvikCPU] Auto-selected execution mode: {mode} (ABI: {fallback})");
                            }
                            Debug.WriteLine("[DalvikCPU] Using fallback ABI: " + fallback);
                            break;
                        }
                    }
                }

                // Create the FLinux ElfExecutor for this execution mode.
                NativeExecutor = new ElfExecutor(execMode, da.localAppRoot);
                Debug.WriteLine($"[DalvikCPU] ElfExecutor created, mode={execMode}");

                if (Directory.Exists(libPath))
                {
                    foreach (string soFile in Directory.GetFiles(libPath, "*.so"))
                    {
                        try
                        {
                            byte[] soData = File.ReadAllBytes(soFile);
                            string libName = Path.GetFileName(soFile);

                            // Use ElfExecutor.LoadLibrary to parse + register JNI exports.
                            var loader = NativeExecutor.LoadLibrary(soData, libName, JniEnv);
                            if (loader != null)
                            {
                                loadedLibraries[libName] = loader;
                                Linker.RegisterLoadedLibrary(libName, loader);

                                Debug.WriteLine("[DalvikCPU] Loaded native library: " + libName +
                                    " (" + loader.GetArchitectureName() +
                                    ", " + loader.ExportedFunctions.Count + " exports, " +
                                    loader.NeededLibraries.Count + " dependencies)");

                                foreach (string jniFunc in loader.GetJniExports())
                                    Debug.WriteLine("[DalvikCPU]   JNI export: " + jniFunc);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("[DalvikCPU] Failed to load " + soFile + ": " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DalvikCPU] Native library scan error: " + ex.Message);
            }

            return Task.CompletedTask;
        }

    }

    // Minimal heap-allocated object to represent a Dalvik class instance when no managed type exists.
    // Carries its own property bag so field get/set operations on framework stubs work correctly.
    public class DalvikObject
    {
        public string TypeName { get; }

        /// <summary>
        /// Per-instance property storage for iget/iput operations when no managed .NET field exists.
        /// </summary>
        public Dictionary<string, object> Properties { get; } = new Dictionary<string, object>();

        public DalvikObject(string typeName) { TypeName = typeName; }
        public override string ToString() => "[DalvikObject: " + TypeName + "]";
    }

    // Reference equality comparer for object identity keys
    internal sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
        private ReferenceEqualityComparer() { }
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

}
