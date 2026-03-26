// DalvikCPU

using AndroidInteropLib;
using AndroidInteropLib.android.content;
using AndroidInteropLib.android.view;
using DalvikUWPCSharp.Applet;
using DalvikUWPCSharp.Reassembly;
using dex.net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

// DalvikUWPCSharp.Classes
namespace DalvikUWPCSharp.Classes
{

    // DalvikCPU class
    public class DalvikCPU
    {
        //List<object> Registers = new List<object>();
        object[] Registers = new object[16];
        object result;
        public Dex dex;
        string packageName;
        public EmuPage hostPage;
        DroidApp da;
        //int LastRegisterModified;

        private Context appContext;
        private Window droidWindow;

        // JNI bridge for native method calls (apkenv-inspired)
        public JniEnvironment JniEnv { get; private set; }

        // ELF loader for parsing native .so libraries
        private Dictionary<string, ElfLoader> loadedLibraries = new Dictionary<string, ElfLoader>();

        // Instruction execution count for debugging
        private int instructionCount;


        // DalvikCPU(dex, pName, host emupage)
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

            // Log platform information for Xbox/ARM diagnostics
            XboxPlatform.LogPlatformInfo();

            // set preload status "Setting up app environment"
            hostPage.setPreloadStatusText("Setting up app environment...");

        }//DalvikCPU end

        // Start 
        public async void Start()
        {
            if (appContext == null)
            {
                appContext = new AstoriaContext(da, await AstoriaResources.CreateAsync(da));
                
                // form Astoria Window
                droidWindow = new AstoriaWindow(appContext, hostPage);
            }

            // Scan for native libraries in the APK (apkenv-inspired)
            await ScanNativeLibraries();

            // for each dex classes...
            foreach(Class cl in dex.GetClasses())
            {
                //...check if package name contains MainActivity
                if(cl.Name.Equals(packageName + ".MainActivity"))
                {
                    // foreach methods...
                    foreach(Method m in cl.GetMethods())
                    {
                        //...check if method's name contains onCreate
                        if(m.Name.Equals("onCreate"))
                        {
                            // run method m of class cl
                            RunMethod(m, cl);
                        }
                    }
                }
            }

            hostPage.preloadDone();

        }//Start end


        // GoBack event handler
        public async void GoBack()
        {
            var dialog = new Windows.UI.Popups.MessageDialog("Back event initiated.", "Dalvik CPU");

            await dialog.ShowAsync();

        }// GoBack end


        // RunMethod (m, c, obj)
        public object RunMethod(Method m, Class cl, params object[] obj)
        {
            if(!TryNativeMethod(m, cl, obj))
            {
                foreach (OpCode o in m.GetInstructions())
                {
                    //try
                    //{
                        ExecuteInstruction(o, cl);
                    //}
                    //catch (Exception ex)
                    //{
                    //    Debug.WriteLine("[ex] Dalvik CPU - RunMethod Exception : "
                    //        + ex.Message);
                    //}
                }
            }

            return result;
            //dynamic MyD = new DynamicObject()

        }//RunMethod end


        // ExecuteInstruction (operand, code)
        public void ExecuteInstruction(OpCode op, Class cl)
        {
            instructionCount++;

            switch(op.Instruction)
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
                    try { Registers[csj.Destination] = dex.GetString((int)csj.StringIndex); }
                    catch { Registers[csj.Destination] = ""; }
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

                case Instructions.MoveWide:
                    var mw = (MoveWideOpCode)op;
                    Registers[mw.To] = Registers[mw.From];
                    break;

                case Instructions.MoveObject:
                    var mo = (MoveObjectOpCode)op;
                    Registers[mo.To] = Registers[mo.From];
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
                    Registers[me.Destination] = null; // Exception handling stub
                    break;

                // ── RETURN ───────────────────────────────────────────────────
                case Instructions.ReturnVoid:
                    result = null;
                    break;

                case Instructions.ReturnValue:
                    var rv = (ReturnValueOpCode)op;
                    result = Registers[rv.Value];
                    break;

                case Instructions.ReturnWide:
                    var rw = (ReturnWideOpCode)op;
                    result = Registers[rw.Value];
                    break;

                case Instructions.ReturnObject:
                    var ro = (ReturnObjectOpCode)op;
                    result = Registers[ro.Value];
                    break;

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
                case Instructions.FloatToInt:
                    ExecuteUnaryOp(op, a => (int)ToFloat(a));
                    break;
                case Instructions.DoubleToInt:
                    ExecuteUnaryOp(op, a => (int)ToDouble(a));
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

                // ── NEW INSTANCE ─────────────────────────────────────────────
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
                            Registers[ni.Destination] = new object();
                        Debug.WriteLine("[DalvikCPU] new-instance: " + typeName);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[DalvikCPU] new-instance error: " + ex.Message);
                        Registers[ni.Destination] = new object();
                    }
                    break;

                default:
                    Debug.WriteLine("[DalvikCPU] Unhandled instruction: " + op.Instruction);
                    break;
            }

        }//ExecuteInstruction end


        // TryNativeMethod (m, c, obj)
        private bool TryNativeMethod(Method m, Class c, params object[] obj)
        {
            // if method name contains "setContentView"..
            if (m.Name.Contains("setContentView"))
            {
                // ...set contentview
                droidWindow.setContentView((int)obj[0]);

                return true;
            }

            string className = ConvertClassName(c.Name);
            if (className.StartsWith(packageName))
                return false;
            
            Type myType = Type.GetType("AndroidInteropLib." + className);
            if(myType != null)
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

            return false;

        }//TryNativeMethod end


        // ConvertClassName
        private string ConvertClassName(string s)
        {
            return s.Replace("internal", "_internal");

        }//ConvertClassName end


        // ── Helper: Execute an invoke-family opcode ──────────────────────
        private void ExecuteInvoke(InvokeOpCode invokeOp, Class cl)
        {
            try
            {
                Method m = dex.GetMethod(invokeOp.MethodIndex);

                // Build argument array from registers
                object[] args = new object[Math.Max(0, invokeOp.ArgumentRegisters.Length - 1)];
                for (int i = 1; i < invokeOp.ArgumentRegisters.Length; i++)
                {
                    int regIdx = invokeOp.ArgumentRegisters[i];
                    args[i - 1] = regIdx < Registers.Length ? Registers[regIdx] : null;
                }

                // First try: check if it's a native method that we can handle via JNI
                if (!TryNativeMethod(m, cl, args))
                {
                    // Fall back to running the method through the Dalvik VM
                    result = RunMethod(m, cl, args);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DalvikCPU] " + invokeOp.Instruction + " exception: " + ex.Message);
            }
        }

        // ── Helper: Execute binary arithmetic 3-register opcodes ─────────
        private void ExecuteBinaryOp(OpCode op, Func<object, object, object> operation)
        {
            try
            {
                // BinaryOpOpCode has Destination, FirstSource, SecondSource
                var bop = (dynamic)op;
                byte dest = (byte)bop.Destination;
                byte srcA = (byte)bop.FirstSource;
                byte srcB = (byte)bop.SecondSource;
                Registers[dest] = operation(Registers[srcA], Registers[srcB]);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DalvikCPU] BinaryOp error: " + ex.Message);
            }
        }

        // ── Helper: Execute binary arithmetic 2-address opcodes ──────────
        private void ExecuteBinaryOp2Addr(OpCode op, Func<object, object, object> operation)
        {
            try
            {
                var bop = (dynamic)op;
                byte dest = (byte)bop.Destination;
                byte src = (byte)bop.Source;
                Registers[dest] = operation(Registers[dest], Registers[src]);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DalvikCPU] BinaryOp2Addr error: " + ex.Message);
            }
        }

        // ── Helper: Execute unary operations ─────────────────────────────
        private void ExecuteUnaryOp(OpCode op, Func<object, object> operation)
        {
            try
            {
                var uop = (dynamic)op;
                byte dest = (byte)uop.Destination;
                byte src = (byte)uop.Source;
                Registers[dest] = operation(Registers[src]);
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
            try { return Convert.ToInt32(o); } catch { return 0; }
        }

        private static long ToLong(object o)
        {
            if (o == null) return 0L;
            if (o is long l) return l;
            if (o is int i) return i;
            if (o is double d) return (long)d;
            try { return Convert.ToInt64(o); } catch { return 0L; }
        }

        private static float ToFloat(object o)
        {
            if (o == null) return 0f;
            if (o is float f) return f;
            if (o is int i) return i;
            if (o is double d) return (float)d;
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

        // ── Native library scanning (apkenv-inspired) ───────────────────
        private async Task ScanNativeLibraries()
        {
            if (da.localAppRoot == null)
                return;

            string abiName = XboxPlatform.GetAndroidAbiName();
            string libPath = Path.Combine(da.localAppRoot.Path, "lib", abiName);

            try
            {
                if (!Directory.Exists(libPath))
                {
                    // Try fallback ABI paths
                    string[] fallbacks = { "armeabi-v7a", "armeabi", "x86", "x86_64", "arm64-v8a" };
                    foreach (string fallback in fallbacks)
                    {
                        string altPath = Path.Combine(da.localAppRoot.Path, "lib", fallback);
                        if (Directory.Exists(altPath))
                        {
                            libPath = altPath;
                            Debug.WriteLine("[DalvikCPU] Using fallback ABI: " + fallback);
                            break;
                        }
                    }
                }

                if (Directory.Exists(libPath))
                {
                    foreach (string soFile in Directory.GetFiles(libPath, "*.so"))
                    {
                        try
                        {
                            var loader = new ElfLoader();
                            byte[] soData = File.ReadAllBytes(soFile);
                            if (loader.Load(soData))
                            {
                                string libName = Path.GetFileName(soFile);
                                loadedLibraries[libName] = loader;

                                Debug.WriteLine("[DalvikCPU] Loaded native library: " + libName +
                                    " (" + loader.GetArchitectureName() +
                                    ", " + loader.ExportedFunctions.Count + " exports, " +
                                    loader.NeededLibraries.Count + " dependencies)");

                                // Log JNI exports
                                var jniExports = loader.GetJniExports();
                                foreach (string jniFunc in jniExports)
                                {
                                    Debug.WriteLine("[DalvikCPU]   JNI export: " + jniFunc);
                                }
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

            await Task.CompletedTask;
        }

    }//DalvikCPU class end


    /*
    public class DalvikClass
    {
        Type super;
        //object super;
        Class c;
        DalvikCPU cpu;

        public DalvikClass(Class c, DalvikCPU dc)
        {
            this.c = c;
            cpu = dc;

            if("AndroidInteropLib" + c.SuperClass == "")
            {
                //set super to native class
            }
        }

        public void SetInheritence(Type t)
        {
            super = t;
        }

        public object RunMethod(string name, params object[] obj)
        {
            //Check if current class has method. If not, check super.
            var meth = c.GetMethods().FirstOrDefault(x => x.Name.Equals(name));
            if (meth != null)
                return cpu.RunMethod(meth, c);

            if (super != null)
            {
                TypeInfo info = super.GetTypeInfo();
                MethodInfo mi = info.GetDeclaredMethod(name);
                if (mi != null)
                {
                    try
                    {
                        return mi.Invoke(this, obj);
                    }
                    catch
                    {
                        return null;
                    }
                }
            }

            return null;
        }

        // GetSuperType
        private Type GetSuperType()
        {
            return super;
        }

    }//DalvikClass end
    */

}//DalvikUWPCSharp.Classes namespace end
