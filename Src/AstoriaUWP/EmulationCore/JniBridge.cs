// JniBridge - Provides a managed JNI-like interface for Android native method calls.
// Inspired by apkenv's JNI implementation that bridges native Android libraries.
//
// Debug logging: Extended tracing is compiled only in DEBUG builds.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DalvikUWPCSharp.Classes
{
    /// <summary>
    /// Represents a JNI value that can hold different types matching JNI value union.
    /// </summary>
    public struct JniValue
    {
        public bool Z; // jboolean
        public byte B;  // jbyte
        public char C;  // jchar
        public short S;  // jshort
        public int I;  // jint
        public long J;  // jlong
        public float F;  // jfloat
        public double D;  // jdouble
        public object L;  // jobject

        public static JniValue FromInt(int val) => new JniValue { I = val };
        public static JniValue FromLong(long val) => new JniValue { J = val };
        public static JniValue FromFloat(float val) => new JniValue { F = val };
        public static JniValue FromDouble(double val) => new JniValue { D = val };
        public static JniValue FromBool(bool val) => new JniValue { Z = val };
        public static JniValue FromObject(object val) => new JniValue { L = val };
        public static JniValue Void() => new JniValue { I = 0 };
    }

    /// <summary>
    /// Delegate for native method implementations registered via JNI.
    /// </summary>
    public delegate JniValue JniNativeMethod(JniEnvironment env, object thisObj, JniValue[] args);

    /// <summary>
    /// Provides a JNI (Java Native Interface) compatible environment for Android native code.
    /// Maps Android JNI calls to managed C# implementations, enabling native library
    /// interoperability without actual native code execution on UWP.
    /// Based on apkenv's approach of providing stub JNI functions.
    /// </summary>
    public class JniEnvironment
    {
        private Dictionary<string, JniNativeMethod> registeredMethods =
            new Dictionary<string, JniNativeMethod>();

        private Dictionary<int, object> globalRefs = new Dictionary<int, object>();
        private Dictionary<int, object> localRefs = new Dictionary<int, object>();
        private int nextGlobalRef = 1;
        private int nextLocalRef = 1;
        private Dictionary<string, Type> classMap = new Dictionary<string, Type>();

        /// <summary>
        /// Registers a native method implementation for a given class and method name.
        /// </summary>
        public void RegisterNativeMethod(string className, string methodName, string signature, JniNativeMethod implementation)
        {
            string key = className + "." + methodName + signature;
            registeredMethods[key] = implementation;
            Debug.WriteLine("[JNI] Registered native method: " + key);
        }

        /// <summary>
        /// Calls a registered native method. Returns null if not found.
        /// </summary>
        public JniValue? CallNativeMethod(string className, string methodName, string signature, object thisObj, JniValue[] args)
        {
            string key = className + "." + methodName + signature;
            if (registeredMethods.TryGetValue(key, out JniNativeMethod method))
            {
                try
                {
                    return method(this, thisObj, args);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[JNI] Native method exception: " + key + " - " + ex.Message);
                    return null;
                }
            }

#if DEBUG
            Debug.WriteLine("[JNI] Native method not found: " + key);
#endif
            return null;
        }

        /// <summary>
        /// Creates a new global reference to an object.
        /// </summary>
        public int NewGlobalRef(object obj)
        {
            int refId = nextGlobalRef++;
            globalRefs[refId] = obj;
            return refId;
        }

        /// <summary>
        /// Deletes a global reference.
        /// </summary>
        public void DeleteGlobalRef(int refId)
        {
            globalRefs.Remove(refId);
        }

        /// <summary>
        /// Creates a new local reference to an object.
        /// </summary>
        public int NewLocalRef(object obj)
        {
            int refId = nextLocalRef++;
            localRefs[refId] = obj;
            return refId;
        }

        /// <summary>
        /// Deletes a local reference.
        /// </summary>
        public void DeleteLocalRef(int refId)
        {
            localRefs.Remove(refId);
        }

        /// <summary>
        /// Resolves a reference ID back to its object.
        /// </summary>
        public object ResolveRef(int refId)
        {
            if (globalRefs.TryGetValue(refId, out object gObj))
                return gObj;
            if (localRefs.TryGetValue(refId, out object lObj))
                return lObj;
            return null;
        }

        /// <summary>
        /// Clears all local references (called at the end of a JNI method).
        /// </summary>
        public void ClearLocalRefs()
        {
            localRefs.Clear();
            nextLocalRef = 1;
        }

        /// <summary>
        /// Finds a class by its JNI name (e.g., "com/example/MyClass").
        /// </summary>
        public int FindClass(string jniClassName)
        {
            string dotName = jniClassName.Replace('/', '.');
            if (classMap.TryGetValue(dotName, out Type t))
                return NewLocalRef(t);

            // Try to find in loaded assemblies
            Type found = Type.GetType("AndroidInteropLib." + dotName);
            if (found != null)
            {
                classMap[dotName] = found;
                return NewLocalRef(found);
            }

#if DEBUG
            Debug.WriteLine("[JNI] FindClass failed: " + jniClassName);
#endif
            // Return a placeholder so native code doesn't crash on null class
            return NewLocalRef(new DalvikClassRef(dotName));
        }

        /// <summary>
        /// Creates a new Java string from a UTF-8 C string.
        /// </summary>
        public int NewStringUTF(string str)
        {
            return NewLocalRef(str);
        }

        /// <summary>
        /// Gets the UTF-8 string from a Java string reference.
        /// </summary>
        public string GetStringUTFChars(int stringRef)
        {
            object obj = ResolveRef(stringRef);
            return obj as string ?? string.Empty;
        }

        /// <summary>
        /// Returns the length of a Java array.
        /// </summary>
        public int GetArrayLength(int arrayRef)
        {
            object obj = ResolveRef(arrayRef);
            if (obj is Array arr)
                return arr.Length;
            return 0;
        }

        /// <summary>
        /// Returns the number of registered native methods (useful for diagnostics).
        /// </summary>
        public int RegisteredMethodCount => registeredMethods.Count;

        // ── Additional JNI functions from ExAndroidNativeEmu jni_env.py ──────────

        /// <summary>
        /// IsSameObject (JNI slot 24) – returns true if both refs point to the same object.
        /// </summary>
        public bool IsSameObject(int ref1, int ref2)
        {
            if (ref1 == ref2) return true;
            object o1 = ResolveRef(ref1);
            object o2 = ResolveRef(ref2);
            return ReferenceEquals(o1, o2);
        }

        /// <summary>GetObjectClass (JNI slot 31) – returns the class of an object ref.</summary>
        public int GetObjectClass(int objRef)
        {
            object obj = ResolveRef(objRef);
            if (obj == null) return 0;
            Type t = obj.GetType();
            return NewLocalRef(t);
        }

        /// <summary>IsInstanceOf (JNI slot 32) – checks instanceof.</summary>
        public bool IsInstanceOf(int objRef, int classRef)
        {
            object obj = ResolveRef(objRef);
            object cls = ResolveRef(classRef);
            if (obj == null || cls == null) return false;
            if (cls is Type t) return t.IsInstanceOfType(obj);
            return false;
        }

        // ── Exception handling (JNI slots 13-18) ─────────────────────────────────

        private object _pendingException;

        /// <summary>ExceptionOccurred (slot 15) – returns ref to pending exception, or 0.</summary>
        public int ExceptionOccurred()
        {
            if (_pendingException == null) return 0;
            return NewLocalRef(_pendingException);
        }

        /// <summary>ExceptionDescribe (slot 16) – print pending exception to debug log.</summary>
        public void ExceptionDescribe()
        {
            if (_pendingException != null)
                Debug.WriteLine("[JNI] Pending exception: " + _pendingException);
        }

        /// <summary>ExceptionClear (slot 17) – clear pending exception.</summary>
        public void ExceptionClear()
        {
            _pendingException = null;
        }

        /// <summary>ExceptionCheck (slot 228) – returns true if there is a pending exception.</summary>
        public bool ExceptionCheck() => _pendingException != null;

        /// <summary>Throw (slot 13) – sets a pending exception from an existing throwable ref.</summary>
        public int Throw(int throwableRef)
        {
            _pendingException = ResolveRef(throwableRef);
            return 0;
        }

        /// <summary>ThrowNew (slot 14) – construct and set a new exception.</summary>
        public int ThrowNew(int classRef, string message)
        {
            object cls = ResolveRef(classRef);
            _pendingException = new Exception($"[JNI] ThrowNew {cls}: {message}");
            Debug.WriteLine($"[JNI] ThrowNew: {_pendingException}");
            return 0;
        }

        /// <summary>FatalError (slot 18) – print message and abort.</summary>
        public void FatalError(string message)
        {
            throw new Exception("[JNI] FatalError: " + message);
        }

        // ── Local frame management (JNI slots 19-20, 26) ─────────────────────────

        private readonly System.Collections.Generic.Stack<int> _localFrameStack =
            new System.Collections.Generic.Stack<int>();

        /// <summary>PushLocalFrame (slot 19) – create a new local reference frame.</summary>
        public int PushLocalFrame(int capacity)
        {
            _localFrameStack.Push(nextLocalRef);
            return 0;
        }

        /// <summary>PopLocalFrame (slot 20) – pop the top local frame, returning a result ref.</summary>
        public int PopLocalFrame(int resultRef)
        {
            if (_localFrameStack.Count > 0)
            {
                int savedNext = _localFrameStack.Pop();
                // Free locals allocated since push.
                for (int id = savedNext; id < nextLocalRef; id++)
                    localRefs.Remove(id);
                nextLocalRef = savedNext;
            }
            if (resultRef != 0)
            {
                object result = ResolveRef(resultRef);
                return result != null ? NewLocalRef(result) : 0;
            }
            return 0;
        }

        /// <summary>EnsureLocalCapacity (slot 26) – stub; always succeeds.</summary>
        public int EnsureLocalCapacity(int capacity) => 0;

        // ── Object creation (JNI slots 27-30) ────────────────────────────────────

        /// <summary>AllocObject (slot 27) – allocate an uninitialised object of the given class.</summary>
        public int AllocObject(int classRef)
        {
            object cls = ResolveRef(classRef);
            string className = cls is DalvikClassRef dcr ? dcr.ClassName
                             : cls is Type t ? t.FullName : cls?.ToString() ?? "Unknown";
            Debug.WriteLine($"[JNI] AllocObject {className}");
            return NewLocalRef(new DalvikClassRef(className));
        }

        // ── Method / field lookup (JNI slots 33, 94, 113, 144) ───────────────────

        /// <summary>
        /// GetMethodId (slot 33) – look up a virtual method ID.
        /// Returns a token that CallXxxMethod can use.
        /// </summary>
        public int GetMethodId(int classRef, string name, string sig)
        {
            string className = ClassNameFromRef(classRef);
            string key = $"{className}.{name}{sig}";
            Debug.WriteLine($"[JNI] GetMethodId {key}");
            return NewLocalRef(new JniMethodId(className, name, sig, isStatic: false));
        }

        /// <summary>GetStaticMethodId (slot 113) – look up a static method ID.</summary>
        public int GetStaticMethodId(int classRef, string name, string sig)
        {
            string className = ClassNameFromRef(classRef);
            string key = $"{className}.{name}{sig}";
            Debug.WriteLine($"[JNI] GetStaticMethodId {key}");
            return NewLocalRef(new JniMethodId(className, name, sig, isStatic: true));
        }

        /// <summary>GetFieldId (slot 94) – look up an instance field ID.</summary>
        public int GetFieldId(int classRef, string name, string sig)
        {
            string className = ClassNameFromRef(classRef);
            Debug.WriteLine($"[JNI] GetFieldId {className}.{name} {sig}");
            return NewLocalRef(new JniFieldId(className, name, sig, isStatic: false));
        }

        /// <summary>GetStaticFieldId (slot 144) – look up a static field ID.</summary>
        public int GetStaticFieldId(int classRef, string name, string sig)
        {
            string className = ClassNameFromRef(classRef);
            Debug.WriteLine($"[JNI] GetStaticFieldId {className}.{name} {sig}");
            return NewLocalRef(new JniFieldId(className, name, sig, isStatic: true));
        }

        // ── Call*Method helpers (slots 34-143) ───────────────────────────────────

        /// <summary>
        /// Call a virtual or static method identified by a JniMethodId ref.
        /// Falls back to the registered native method table.
        /// </summary>
        public JniValue CallMethod(int objRef, int methodIdRef, JniValue[] args)
        {
            var mid = ResolveRef(methodIdRef) as JniMethodId;
            if (mid == null) return JniValue.Void();

            var result = CallNativeMethod(mid.ClassName, mid.MethodName, mid.Signature,
                                          ResolveRef(objRef), args);
            return result ?? JniValue.Void();
        }

        /// <summary>CallVoidMethod / CallStaticVoidMethod convenience overload.</summary>
        public void CallVoidMethod(int objRef, int methodIdRef, JniValue[] args)
            => CallMethod(objRef, methodIdRef, args);

        /// <summary>CallIntMethod / CallStaticIntMethod convenience overload.</summary>
        public int CallIntMethod(int objRef, int methodIdRef, JniValue[] args)
            => CallMethod(objRef, methodIdRef, args).I;

        /// <summary>CallBooleanMethod convenience overload.</summary>
        public bool CallBooleanMethod(int objRef, int methodIdRef, JniValue[] args)
            => CallMethod(objRef, methodIdRef, args).Z;

        /// <summary>CallLongMethod convenience overload.</summary>
        public long CallLongMethod(int objRef, int methodIdRef, JniValue[] args)
            => CallMethod(objRef, methodIdRef, args).J;

        /// <summary>CallObjectMethod convenience overload.</summary>
        public int CallObjectMethod(int objRef, int methodIdRef, JniValue[] args)
        {
            var v = CallMethod(objRef, methodIdRef, args);
            return v.L != null ? NewLocalRef(v.L) : 0;
        }

        // ── Field get/set helpers ─────────────────────────────────────────────────

        private readonly Dictionary<string, object> _instanceFields = new Dictionary<string, object>();
        private readonly Dictionary<string, object> _staticFields   = new Dictionary<string, object>();

        /// <summary>GetObjectField (slot 95).</summary>
        public int GetObjectField(int objRef, int fieldIdRef)
        {
            var fid = ResolveRef(fieldIdRef) as JniFieldId;
            if (fid == null) return 0;
            string key = fid.ClassName + "." + fid.FieldName;
            _instanceFields.TryGetValue(key, out object val);
            return val != null ? NewLocalRef(val) : 0;
        }

        /// <summary>SetObjectField (slot 104).</summary>
        public void SetObjectField(int objRef, int fieldIdRef, int valueRef)
        {
            var fid = ResolveRef(fieldIdRef) as JniFieldId;
            if (fid == null) return;
            string key = fid.ClassName + "." + fid.FieldName;
            _instanceFields[key] = ResolveRef(valueRef);
        }

        /// <summary>GetStaticObjectField (slot 145).</summary>
        public int GetStaticObjectField(int classRef, int fieldIdRef)
        {
            var fid = ResolveRef(fieldIdRef) as JniFieldId;
            if (fid == null) return 0;
            string key = fid.ClassName + "." + fid.FieldName;
            _staticFields.TryGetValue(key, out object val);
            return val != null ? NewLocalRef(val) : 0;
        }

        /// <summary>SetStaticObjectField (slot 154).</summary>
        public void SetStaticObjectField(int classRef, int fieldIdRef, int valueRef)
        {
            var fid = ResolveRef(fieldIdRef) as JniFieldId;
            if (fid == null) return;
            string key = fid.ClassName + "." + fid.FieldName;
            _staticFields[key] = ResolveRef(valueRef);
        }

        // ── Array operations (slots 172-228) ─────────────────────────────────────

        /// <summary>NewObjectArray (slot 186).</summary>
        public int NewObjectArray(int length, int classRef, int initRef)
        {
            var arr = new object[length];
            object initVal = ResolveRef(initRef);
            for (int i = 0; i < length; i++) arr[i] = initVal;
            return NewLocalRef(arr);
        }

        /// <summary>GetObjectArrayElement (slot 187).</summary>
        public int GetObjectArrayElement(int arrayRef, int index)
        {
            if (ResolveRef(arrayRef) is object[] arr && index >= 0 && index < arr.Length)
                return arr[index] != null ? NewLocalRef(arr[index]) : 0;
            return 0;
        }

        /// <summary>SetObjectArrayElement (slot 188).</summary>
        public void SetObjectArrayElement(int arrayRef, int index, int valueRef)
        {
            if (ResolveRef(arrayRef) is object[] arr && index >= 0 && index < arr.Length)
                arr[index] = ResolveRef(valueRef);
        }

        /// <summary>NewByteArray (slot 196).</summary>
        public int NewByteArray(int length) => NewLocalRef(new byte[length]);

        /// <summary>NewIntArray (slot 199).</summary>
        public int NewIntArray(int length) => NewLocalRef(new int[length]);

        /// <summary>NewCharArray (slot 197).</summary>
        public int NewCharArray(int length) => NewLocalRef(new char[length]);

        /// <summary>GetByteArrayElements (slot 209) – returns the backing array reference.</summary>
        public byte[] GetByteArrayElements(int arrayRef)
            => ResolveRef(arrayRef) as byte[];

        /// <summary>
        /// GetByteArrayRegion (slot 219) – copy a range from a byte array into a managed buffer.
        /// </summary>
        public byte[] GetByteArrayRegion(int arrayRef, int start, int len)
        {
            if (ResolveRef(arrayRef) is byte[] arr)
            {
                var result = new byte[Math.Min(len, arr.Length - start)];
                Array.Copy(arr, start, result, 0, result.Length);
                return result;
            }
            return new byte[0];
        }

        /// <summary>SetByteArrayRegion (slot 220) – copy bytes into a Java byte array.</summary>
        public void SetByteArrayRegion(int arrayRef, int start, byte[] src)
        {
            if (ResolveRef(arrayRef) is byte[] arr)
                Array.Copy(src, 0, arr, start, Math.Min(src.Length, arr.Length - start));
        }

        // ── String helpers (slots 163-170) ───────────────────────────────────────

        /// <summary>NewString (slot 163) – creates a Java string from a char array.</summary>
        public int NewString(char[] chars, int length)
            => NewLocalRef(new string(chars, 0, length));

        /// <summary>GetStringLength (slot 164) – returns the length of a Java string.</summary>
        public int GetStringLength(int stringRef)
        {
            if (ResolveRef(stringRef) is string s) return s.Length;
            return 0;
        }

        /// <summary>GetStringUTFLength (slot 168).</summary>
        public int GetStringUTFLength(int stringRef)
        {
            if (ResolveRef(stringRef) is string s)
                return System.Text.Encoding.UTF8.GetByteCount(s);
            return 0;
        }

        /// <summary>GetStringChars (slot 165) – returns the char buffer of a Java string.</summary>
        public char[] GetStringChars(int stringRef)
        {
            if (ResolveRef(stringRef) is string s) return s.ToCharArray();
            return new char[0];
        }

        /// <summary>ReleaseStringChars stub (slot 166).</summary>
        public void ReleaseStringChars(int stringRef, char[] chars) { }

        /// <summary>GetStringUTFChars via ref (slot 169).</summary>
        public string GetStringUTFCharsFromRef(int stringRef)
            => GetStringUTFChars(stringRef);

        /// <summary>ReleaseStringUTFChars stub (slot 170).</summary>
        public void ReleaseStringUTFChars(int stringRef, string chars) { }

        // ── Native method registration (JNI slot 215) ─────────────────────────────

        /// <summary>
        /// RegisterNatives (JNI slot 215) – bulk-register native method implementations.
        /// Equivalent to ExAndroidNativeEmu jni_env.py register_natives.
        /// </summary>
        public int RegisterNatives(int classRef, JniNativeMethodDescriptor[] methods)
        {
            string className = ClassNameFromRef(classRef);
            foreach (var m in methods)
            {
                RegisterNativeMethod(className, m.Name, m.Signature, m.Implementation);
            }
            Debug.WriteLine($"[JNI] RegisterNatives {className} count={methods.Length}");
            return 0;
        }

        // ── JavaVM helpers ────────────────────────────────────────────────────────

        /// <summary>GetJavaVM (JNI slot 219) – returns a reference to the JavaVM.</summary>
        public int GetJavaVM() => 1; // stub handle

        /// <summary>GetVersion (JNI slot 4) – returns JNI_VERSION_1_6.</summary>
        public int GetVersion() => 0x00010006;

        // ── Weak references (slots 226-227) ───────────────────────────────────────

        private readonly Dictionary<int, WeakReference> _weakRefs = new Dictionary<int, WeakReference>();
        private int _nextWeakRef = 0x8000;

        /// <summary>NewWeakGlobalRef (slot 226).</summary>
        public int NewWeakGlobalRef(int objRef)
        {
            object obj = ResolveRef(objRef);
            if (obj == null) return 0;
            int id = _nextWeakRef++;
            _weakRefs[id] = new WeakReference(obj);
            return id;
        }

        /// <summary>DeleteWeakGlobalRef (slot 227).</summary>
        public void DeleteWeakGlobalRef(int weakRef) => _weakRefs.Remove(weakRef);

        /// <summary>GetObjectRefType (slot 232) – returns reference type.</summary>
        public int GetObjectRefType(int objRef)
        {
            if (globalRefs.ContainsKey(objRef)) return 2; // JNIGlobalRefType
            if (localRefs.ContainsKey(objRef))  return 1; // JNILocalRefType
            if (_weakRefs.ContainsKey(objRef))  return 3; // JNIWeakGlobalRefType
            return 0; // JNIInvalidRefType
        }

        // ── Internal helpers ──────────────────────────────────────────────────────

        private string ClassNameFromRef(int classRef)
        {
            object cls = ResolveRef(classRef);
            if (cls is DalvikClassRef dcr) return dcr.ClassName;
            if (cls is Type t) return t.FullName ?? t.Name;
            return cls?.ToString() ?? "Unknown";
        }
    }

    /// <summary>
    /// Placeholder for a JNI class reference when no managed Type is found.
    /// Used by FindClass to return a non-null reference that won't be
    /// misinterpreted as a string or other value.
    /// </summary>
    public sealed class DalvikClassRef
    {
        public string ClassName { get; }
        public DalvikClassRef(string className) { ClassName = className; }
        public override string ToString() => "[ClassRef: " + ClassName + "]";
    }

    /// <summary>
    /// Represents a JNI method identifier returned by GetMethodId / GetStaticMethodId.
    /// Equivalent to jmethodID in the JNI spec.
    /// </summary>
    public sealed class JniMethodId
    {
        public string ClassName  { get; }
        public string MethodName { get; }
        public string Signature  { get; }
        public bool   IsStatic   { get; }

        public JniMethodId(string className, string methodName, string sig, bool isStatic)
        {
            ClassName  = className;
            MethodName = methodName;
            Signature  = sig;
            IsStatic   = isStatic;
        }

        public override string ToString() =>
            $"[MethodId: {ClassName}.{MethodName}{Signature} static={IsStatic}]";
    }

    /// <summary>
    /// Represents a JNI field identifier returned by GetFieldId / GetStaticFieldId.
    /// Equivalent to jfieldID in the JNI spec.
    /// </summary>
    public sealed class JniFieldId
    {
        public string ClassName { get; }
        public string FieldName { get; }
        public string Signature { get; }
        public bool   IsStatic  { get; }

        public JniFieldId(string className, string fieldName, string sig, bool isStatic)
        {
            ClassName = className;
            FieldName = fieldName;
            Signature = sig;
            IsStatic  = isStatic;
        }

        public override string ToString() =>
            $"[FieldId: {ClassName}.{FieldName} {Signature} static={IsStatic}]";
    }

    /// <summary>
    /// Describes a single native method binding passed to RegisterNatives.
    /// Mirrors the JNINativeMethod struct from jni.h.
    /// </summary>
    public sealed class JniNativeMethodDescriptor
    {
        public string Name           { get; }
        public string Signature      { get; }
        public JniNativeMethod Implementation { get; }

        public JniNativeMethodDescriptor(string name, string sig, JniNativeMethod impl)
        {
            Name           = name;
            Signature      = sig;
            Implementation = impl;
        }
    }

    /// <summary>
    /// Provides stub implementations of common Android system libraries.
    /// Maps Android libc/liblog/libandroid calls to UWP equivalents.
    /// Inspired by apkenv's approach of hooking system library functions.
    ///
    /// Reference: https://github.com/nicknisi/android-7.0.0_r1
    /// The stubs here cover the subset of the Android framework that games
    /// like Angry Birds call during startup and rendering.
    /// </summary>
    public static class AndroidSystemLibraryStubs
    {
        /// <summary>
        /// Registers all system library stub methods with the JNI environment.
        /// </summary>
        public static void RegisterAll(JniEnvironment env)
        {
            RegisterLogStubs(env);
            RegisterSystemStubs(env);
            RegisterActivityStubs(env);
            RegisterGLStubs(env);
            RegisterResourceStubs(env);
            RegisterMediaStubs(env);
            RegisterMiscStubs(env);
        }

        private static void RegisterLogStubs(JniEnvironment env)
        {
            // android/util/Log.d(String, String) -> int
            env.RegisterNativeMethod("android.util.Log", "d", "(Ljava/lang/String;Ljava/lang/String;)I",
                (jniEnv, thisObj, args) =>
                {
                    string tag = args.Length > 0 ? args[0].L as string ?? "" : "";
                    string msg = args.Length > 1 ? args[1].L as string ?? "" : "";
                    Debug.WriteLine("[Android.Log.d] " + tag + ": " + msg);
                    return JniValue.FromInt(0);
                });

            // android/util/Log.i(String, String) -> int
            env.RegisterNativeMethod("android.util.Log", "i", "(Ljava/lang/String;Ljava/lang/String;)I",
                (jniEnv, thisObj, args) =>
                {
                    string tag = args.Length > 0 ? args[0].L as string ?? "" : "";
                    string msg = args.Length > 1 ? args[1].L as string ?? "" : "";
                    Debug.WriteLine("[Android.Log.i] " + tag + ": " + msg);
                    return JniValue.FromInt(0);
                });

            // android/util/Log.e(String, String) -> int
            env.RegisterNativeMethod("android.util.Log", "e", "(Ljava/lang/String;Ljava/lang/String;)I",
                (jniEnv, thisObj, args) =>
                {
                    string tag = args.Length > 0 ? args[0].L as string ?? "" : "";
                    string msg = args.Length > 1 ? args[1].L as string ?? "" : "";
                    Debug.WriteLine("[Android.Log.e] " + tag + ": " + msg);
                    return JniValue.FromInt(0);
                });

            // android/util/Log.w(String, String) -> int
            env.RegisterNativeMethod("android.util.Log", "w", "(Ljava/lang/String;Ljava/lang/String;)I",
                (jniEnv, thisObj, args) =>
                {
                    string tag = args.Length > 0 ? args[0].L as string ?? "" : "";
                    string msg = args.Length > 1 ? args[1].L as string ?? "" : "";
                    Debug.WriteLine("[Android.Log.w] " + tag + ": " + msg);
                    return JniValue.FromInt(0);
                });

            // android/util/Log.v(String, String) -> int
            env.RegisterNativeMethod("android.util.Log", "v", "(Ljava/lang/String;Ljava/lang/String;)I",
                (jniEnv, thisObj, args) =>
                {
                    string tag = args.Length > 0 ? args[0].L as string ?? "" : "";
                    string msg = args.Length > 1 ? args[1].L as string ?? "" : "";
                    Debug.WriteLine("[Android.Log.v] " + tag + ": " + msg);
                    return JniValue.FromInt(0);
                });

            // android/util/Log.isLoggable(String, int) -> boolean
            env.RegisterNativeMethod("android.util.Log", "isLoggable", "(Ljava/lang/String;I)Z",
                (jniEnv, thisObj, args) => JniValue.FromBool(true));

            // android/util/Log.println(int, String, String) -> int
            env.RegisterNativeMethod("android.util.Log", "println", "(ILjava/lang/String;Ljava/lang/String;)I",
                (jniEnv, thisObj, args) =>
                {
                    string tag = args.Length > 1 ? args[1].L as string ?? "" : "";
                    string msg = args.Length > 2 ? args[2].L as string ?? "" : "";
                    Debug.WriteLine("[Android.Log] " + tag + ": " + msg);
                    return JniValue.FromInt(0);
                });
        }

        private static void RegisterSystemStubs(JniEnvironment env)
        {
            // System.loadLibrary(String) - track requested native libs
            env.RegisterNativeMethod("java.lang.System", "loadLibrary", "(Ljava/lang/String;)V",
                (jniEnv, thisObj, args) =>
                {
                    string libName = args.Length > 0 ? args[0].L as string ?? "" : "";
                    Debug.WriteLine("[System.loadLibrary] Requested: " + libName);
                    return JniValue.Void();
                });

            // System.nanoTime() -> long
            env.RegisterNativeMethod("java.lang.System", "nanoTime", "()J",
                (jniEnv, thisObj, args) =>
                {
                    long nanos = Stopwatch.GetTimestamp() * (1000000000L / Stopwatch.Frequency);
                    return JniValue.FromLong(nanos);
                });

            // System.currentTimeMillis() -> long
            env.RegisterNativeMethod("java.lang.System", "currentTimeMillis", "()J",
                (jniEnv, thisObj, args) =>
                {
                    long millis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    return JniValue.FromLong(millis);
                });

            // android/os/Build.VERSION.SDK_INT
            env.RegisterNativeMethod("android.os.Build$VERSION", "SDK_INT", "()I",
                (jniEnv, thisObj, args) => JniValue.FromInt(28)); // Android 9 (Pie)

            // android/os/SystemClock.uptimeMillis() -> long
            env.RegisterNativeMethod("android.os.SystemClock", "uptimeMillis", "()J",
                (jniEnv, thisObj, args) =>
                {
                    return JniValue.FromLong(Environment.TickCount);
                });

            // android/os/SystemClock.elapsedRealtime() -> long
            env.RegisterNativeMethod("android.os.SystemClock", "elapsedRealtime", "()J",
                (jniEnv, thisObj, args) =>
                {
                    return JniValue.FromLong(Environment.TickCount);
                });

            // java.lang.Thread.currentThread() -> Thread
            env.RegisterNativeMethod("java.lang.Thread", "currentThread", "()Ljava/lang/Thread;",
                (jniEnv, thisObj, args) => JniValue.FromObject("main-thread"));

            // java.lang.Runtime.getRuntime() -> Runtime
            env.RegisterNativeMethod("java.lang.Runtime", "getRuntime", "()Ljava/lang/Runtime;",
                (jniEnv, thisObj, args) => JniValue.FromObject("runtime"));

            // java.lang.Runtime.maxMemory() -> long
            env.RegisterNativeMethod("java.lang.Runtime", "maxMemory", "()J",
                (jniEnv, thisObj, args) => JniValue.FromLong(256L * 1024 * 1024));

            // java.lang.Runtime.totalMemory() -> long
            env.RegisterNativeMethod("java.lang.Runtime", "totalMemory", "()J",
                (jniEnv, thisObj, args) => JniValue.FromLong(64L * 1024 * 1024));

            // java.lang.Runtime.freeMemory() -> long
            env.RegisterNativeMethod("java.lang.Runtime", "freeMemory", "()J",
                (jniEnv, thisObj, args) => JniValue.FromLong(32L * 1024 * 1024));

            // java.lang.Runtime.availableProcessors() -> int
            env.RegisterNativeMethod("java.lang.Runtime", "availableProcessors", "()I",
                (jniEnv, thisObj, args) => JniValue.FromInt(Environment.ProcessorCount));
        }

        private static void RegisterActivityStubs(JniEnvironment env)
        {
            // android.app.Activity.getWindowManager
            env.RegisterNativeMethod("android.app.Activity", "getWindowManager", "()Landroid/view/WindowManager;",
                (jniEnv, thisObj, args) => JniValue.FromObject("WindowManager"));

            // android.app.Activity.getWindow
            env.RegisterNativeMethod("android.app.Activity", "getWindow", "()Landroid/view/Window;",
                (jniEnv, thisObj, args) => JniValue.FromObject("Window"));

            // android.app.Activity.finish - stub to allow games to signal exit
            env.RegisterNativeMethod("android.app.Activity", "finish", "()V",
                (jniEnv, thisObj, args) =>
                {
                    Debug.WriteLine("[Activity] finish() called");
                    return JniValue.Void();
                });

            // android.app.Activity.runOnUiThread - stub (runs synchronously in our emulator)
            env.RegisterNativeMethod("android.app.Activity", "runOnUiThread", "(Ljava/lang/Runnable;)V",
                (jniEnv, thisObj, args) =>
                {
#if DEBUG
                    Debug.WriteLine("[Activity] runOnUiThread called (stub)");
#endif
                    return JniValue.Void();
                });

            // android.app.Activity.isFinishing
            env.RegisterNativeMethod("android.app.Activity", "isFinishing", "()Z",
                (jniEnv, thisObj, args) => JniValue.FromBool(false));

            // android.app.Activity.getIntent
            env.RegisterNativeMethod("android.app.Activity", "getIntent", "()Landroid/content/Intent;",
                (jniEnv, thisObj, args) => JniValue.FromObject("Intent"));

            // android.content.Context.getSystemService
            env.RegisterNativeMethod("android.content.Context", "getSystemService", "(Ljava/lang/String;)Ljava/lang/Object;",
                (jniEnv, thisObj, args) =>
                {
                    string service = args.Length > 0 ? args[0].L as string ?? "" : "";
#if DEBUG
                    Debug.WriteLine("[Context] getSystemService: " + service);
#endif
                    return JniValue.FromObject(service + "_service");
                });

            // android.content.Context.getPackageName
            env.RegisterNativeMethod("android.content.Context", "getPackageName", "()Ljava/lang/String;",
                (jniEnv, thisObj, args) => JniValue.FromObject("com.emulated.app"));

            // android.content.Context.getApplicationContext
            env.RegisterNativeMethod("android.content.Context", "getApplicationContext", "()Landroid/content/Context;",
                (jniEnv, thisObj, args) => JniValue.FromObject("ApplicationContext"));

            // android.content.Context.getResources
            env.RegisterNativeMethod("android.content.Context", "getResources", "()Landroid/content/res/Resources;",
                (jniEnv, thisObj, args) => JniValue.FromObject("Resources"));

            // android.content.Context.getAssets
            env.RegisterNativeMethod("android.content.Context", "getAssets", "()Landroid/content/res/AssetManager;",
                (jniEnv, thisObj, args) => JniValue.FromObject("AssetManager"));

            // android.content.Context.getFilesDir
            env.RegisterNativeMethod("android.content.Context", "getFilesDir", "()Ljava/io/File;",
                (jniEnv, thisObj, args) => JniValue.FromObject("/data/data/com.emulated.app/files"));

            // android.content.Context.getCacheDir
            env.RegisterNativeMethod("android.content.Context", "getCacheDir", "()Ljava/io/File;",
                (jniEnv, thisObj, args) => JniValue.FromObject("/data/data/com.emulated.app/cache"));

            // android.content.Context.getApplicationInfo
            env.RegisterNativeMethod("android.content.Context", "getApplicationInfo", "()Landroid/content/pm/ApplicationInfo;",
                (jniEnv, thisObj, args) => JniValue.FromObject("ApplicationInfo"));

            // android.content.Context.getPackageManager
            env.RegisterNativeMethod("android.content.Context", "getPackageManager", "()Landroid/content/pm/PackageManager;",
                (jniEnv, thisObj, args) => JniValue.FromObject("PackageManager"));

            // android.content.Context.getSharedPreferences
            env.RegisterNativeMethod("android.content.Context", "getSharedPreferences", "(Ljava/lang/String;I)Landroid/content/SharedPreferences;",
                (jniEnv, thisObj, args) =>
                {
                    string name = args.Length > 0 ? args[0].L as string ?? "prefs" : "prefs";
#if DEBUG
                    Debug.WriteLine("[Context] getSharedPreferences: " + name);
#endif
                    return JniValue.FromObject("SharedPreferences");
                });

            // android.content.Context.getClassLoader
            env.RegisterNativeMethod("android.content.Context", "getClassLoader", "()Ljava/lang/ClassLoader;",
                (jniEnv, thisObj, args) => JniValue.FromObject("ClassLoader"));

            // android.content.Context.getContentResolver
            env.RegisterNativeMethod("android.content.Context", "getContentResolver", "()Landroid/content/ContentResolver;",
                (jniEnv, thisObj, args) => JniValue.FromObject("ContentResolver"));

            // android.content.res.AssetManager stubs
            env.RegisterNativeMethod("android.content.res.AssetManager", "open", "(Ljava/lang/String;)Ljava/io/InputStream;",
                (jniEnv, thisObj, args) =>
                {
                    string assetName = args.Length > 0 ? args[0].L as string ?? "" : "";
#if DEBUG
                    Debug.WriteLine("[AssetManager] open: " + assetName);
#endif
                    return JniValue.FromObject(null);
                });

            // android.content.res.AssetManager.list(String) -> String[]
            env.RegisterNativeMethod("android.content.res.AssetManager", "list", "(Ljava/lang/String;)[Ljava/lang/String;",
                (jniEnv, thisObj, args) => JniValue.FromObject(new string[0]));
        }

        private static void RegisterGLStubs(JniEnvironment env)
        {
            // android.opengl.GLSurfaceView.setRenderer
            env.RegisterNativeMethod("android.opengl.GLSurfaceView", "setRenderer",
                "(Landroid/opengl/GLSurfaceView$Renderer;)V",
                (jniEnv, thisObj, args) =>
                {
                    Debug.WriteLine("[GLSurfaceView] setRenderer called - native renderer attached.");
                    return JniValue.Void();
                });

            // android.opengl.GLSurfaceView.setEGLContextClientVersion
            env.RegisterNativeMethod("android.opengl.GLSurfaceView", "setEGLContextClientVersion", "(I)V",
                (jniEnv, thisObj, args) =>
                {
                    int version = args.Length > 0 ? args[0].I : 0;
#if DEBUG
                    Debug.WriteLine("[GLSurfaceView] setEGLContextClientVersion: " + version);
#endif
                    return JniValue.Void();
                });

            // android.opengl.GLSurfaceView.setPreserveEGLContextOnPause
            env.RegisterNativeMethod("android.opengl.GLSurfaceView", "setPreserveEGLContextOnPause", "(Z)V",
                (jniEnv, thisObj, args) =>
                {
#if DEBUG
                    Debug.WriteLine("[GLSurfaceView] setPreserveEGLContextOnPause: " + (args.Length > 0 ? args[0].Z.ToString() : "?"));
#endif
                    return JniValue.Void();
                });

            // android.opengl.GLSurfaceView.requestRender
            env.RegisterNativeMethod("android.opengl.GLSurfaceView", "requestRender", "()V",
                (jniEnv, thisObj, args) => JniValue.Void());

            // android.opengl.GLSurfaceView.setRenderMode
            env.RegisterNativeMethod("android.opengl.GLSurfaceView", "setRenderMode", "(I)V",
                (jniEnv, thisObj, args) =>
                {
#if DEBUG
                    int mode = args.Length > 0 ? args[0].I : 0;
                    Debug.WriteLine("[GLSurfaceView] setRenderMode: " + mode);
#endif
                    return JniValue.Void();
                });

            // android.opengl.GLSurfaceView.setEGLConfigChooser (various overloads)
            env.RegisterNativeMethod("android.opengl.GLSurfaceView", "setEGLConfigChooser", "(IIIIII)V",
                (jniEnv, thisObj, args) =>
                {
#if DEBUG
                    Debug.WriteLine("[GLSurfaceView] setEGLConfigChooser(r,g,b,a,depth,stencil)");
#endif
                    return JniValue.Void();
                });
            env.RegisterNativeMethod("android.opengl.GLSurfaceView", "setEGLConfigChooser", "(Z)V",
                (jniEnv, thisObj, args) => JniValue.Void());

            // android.opengl.GLSurfaceView.onResume
            env.RegisterNativeMethod("android.opengl.GLSurfaceView", "onResume", "()V",
                (jniEnv, thisObj, args) =>
                {
#if DEBUG
                    Debug.WriteLine("[GLSurfaceView] onResume called");
#endif
                    return JniValue.Void();
                });

            // android.opengl.GLSurfaceView.onPause
            env.RegisterNativeMethod("android.opengl.GLSurfaceView", "onPause", "()V",
                (jniEnv, thisObj, args) =>
                {
#if DEBUG
                    Debug.WriteLine("[GLSurfaceView] onPause called");
#endif
                    return JniValue.Void();
                });

            // android.opengl.GLSurfaceView.queueEvent
            env.RegisterNativeMethod("android.opengl.GLSurfaceView", "queueEvent", "(Ljava/lang/Runnable;)V",
                (jniEnv, thisObj, args) => JniValue.Void());

            // ── GLES20 stubs ─────────────────────────────────────────────
            // Reference: android.opengl.GLES20 from android-7.0.0_r1
            // frameworks/base/opengl/java/android/opengl/GLES20.java
            env.RegisterNativeMethod("android.opengl.GLES20", "glClear", "(I)V",
                (jniEnv, thisObj, args) =>
                {
                    // GL_COLOR_BUFFER_BIT = 0x00004000
                    if (args.Length > 0 && (args[0].I & 0x00004000) != 0)
                    {
                        DalvikUWPCSharp.Reassembly.UI.AndroidRenderSurface.Current?.GLClear();
                    }
                    return JniValue.Void();
                });
            env.RegisterNativeMethod("android.opengl.GLES20", "glClearColor", "(FFFF)V",
                (jniEnv, thisObj, args) =>
                {
                    if (args.Length >= 4)
                    {
                        DalvikUWPCSharp.Reassembly.UI.AndroidRenderSurface.Current?.SetGLClearColor(
                            args[0].F, args[1].F, args[2].F, args[3].F);
                    }
                    return JniValue.Void();
                });
            env.RegisterNativeMethod("android.opengl.GLES20", "glViewport", "(IIII)V",
                (jniEnv, thisObj, args) =>
                {
#if DEBUG
                    if (args.Length >= 4)
                        Debug.WriteLine($"[GLES20] glViewport({args[0].I},{args[1].I},{args[2].I},{args[3].I})");
#endif
                    return JniValue.Void();
                });
            env.RegisterNativeMethod("android.opengl.GLES20", "glEnable", "(I)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.opengl.GLES20", "glDisable", "(I)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.opengl.GLES20", "glBlendFunc", "(II)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.opengl.GLES20", "glDepthFunc", "(I)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.opengl.GLES20", "glDepthMask", "(Z)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.opengl.GLES20", "glColorMask", "(ZZZZ)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.opengl.GLES20", "glScissor", "(IIII)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.opengl.GLES20", "glLineWidth", "(F)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.opengl.GLES20", "glCreateShader", "(I)I",
                (jniEnv, thisObj, args) => JniValue.FromInt(1)); // non-zero handle
            env.RegisterNativeMethod("android.opengl.GLES20", "glCreateProgram", "()I",
                (jniEnv, thisObj, args) => JniValue.FromInt(1));
            env.RegisterNativeMethod("android.opengl.GLES20", "glGenTextures", "(I[II)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.opengl.GLES20", "glBindTexture", "(II)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.opengl.GLES20", "glTexParameteri", "(III)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.opengl.GLES20", "glGetError", "()I",
                (jniEnv, thisObj, args) => JniValue.FromInt(0)); // GL_NO_ERROR
            env.RegisterNativeMethod("android.opengl.GLES20", "glGetIntegerv", "(ILjava/nio/IntBuffer;)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.opengl.GLES20", "glGetString", "(I)Ljava/lang/String;",
                (jniEnv, thisObj, args) =>
                {
                    int name = args.Length > 0 ? args[0].I : 0;
                    switch (name)
                    {
                        case 0x1F00: return JniValue.FromObject("AstoriaUWP"); // GL_VENDOR
                        case 0x1F01: return JniValue.FromObject("AstoriaUWP Renderer"); // GL_RENDERER
                        case 0x1F02: return JniValue.FromObject("OpenGL ES 2.0"); // GL_VERSION
                        case 0x1F03: return JniValue.FromObject(""); // GL_EXTENSIONS
                        default: return JniValue.FromObject("");
                    }
                });
            env.RegisterNativeMethod("android.opengl.GLES20", "glUseProgram", "(I)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.opengl.GLES20", "glDeleteTextures", "(I[II)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.opengl.GLES20", "glActiveTexture", "(I)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.opengl.GLES20", "glDrawArrays", "(III)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.opengl.GLES20", "glDrawElements", "(IIILjava/nio/Buffer;)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.opengl.GLES20", "glFlush", "()V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.opengl.GLES20", "glFinish", "()V",
                (jniEnv, thisObj, args) => JniValue.Void());
        }

        /// <summary>
        /// Stubs for android.content.res.Resources and related resource lookup.
        /// Reference: android-7.0.0_r1 frameworks/base/core/java/android/content/res/Resources.java
        /// </summary>
        private static void RegisterResourceStubs(JniEnvironment env)
        {
            // Resources.getDisplayMetrics
            env.RegisterNativeMethod("android.content.res.Resources", "getDisplayMetrics",
                "()Landroid/util/DisplayMetrics;",
                (jniEnv, thisObj, args) => JniValue.FromObject("DisplayMetrics"));

            // Resources.getString(int) -> String
            env.RegisterNativeMethod("android.content.res.Resources", "getString",
                "(I)Ljava/lang/String;",
                (jniEnv, thisObj, args) =>
                {
                    int resId = args.Length > 0 ? args[0].I : 0;
                    return JniValue.FromObject("string_res_" + resId);
                });

            // Resources.getConfiguration
            env.RegisterNativeMethod("android.content.res.Resources", "getConfiguration",
                "()Landroid/content/res/Configuration;",
                (jniEnv, thisObj, args) => JniValue.FromObject("Configuration"));

            // Resources.getIdentifier
            env.RegisterNativeMethod("android.content.res.Resources", "getIdentifier",
                "(Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;)I",
                (jniEnv, thisObj, args) => JniValue.FromInt(0));

            // WindowManager.getDefaultDisplay -> Display
            env.RegisterNativeMethod("android.view.WindowManager", "getDefaultDisplay",
                "()Landroid/view/Display;",
                (jniEnv, thisObj, args) => JniValue.FromObject("Display"));

            // Display.getMetrics(DisplayMetrics)
            env.RegisterNativeMethod("android.view.Display", "getMetrics",
                "(Landroid/util/DisplayMetrics;)V",
                (jniEnv, thisObj, args) => JniValue.Void());

            // Display.getWidth/getHeight (deprecated but still used)
            env.RegisterNativeMethod("android.view.Display", "getWidth", "()I",
                (jniEnv, thisObj, args) => JniValue.FromInt(1280));
            env.RegisterNativeMethod("android.view.Display", "getHeight", "()I",
                (jniEnv, thisObj, args) => JniValue.FromInt(720));
            env.RegisterNativeMethod("android.view.Display", "getRotation", "()I",
                (jniEnv, thisObj, args) => JniValue.FromInt(0));

            // android.os.Bundle stubs (used by Activity.onCreate(Bundle savedInstanceState))
            env.RegisterNativeMethod("android.os.Bundle", "getString", "(Ljava/lang/String;)Ljava/lang/String;",
                (jniEnv, thisObj, args) => JniValue.FromObject(null));
            env.RegisterNativeMethod("android.os.Bundle", "getInt", "(Ljava/lang/String;I)I",
                (jniEnv, thisObj, args) =>
                {
                    int def = args.Length > 1 ? args[1].I : 0;
                    return JniValue.FromInt(def);
                });
            env.RegisterNativeMethod("android.os.Bundle", "containsKey", "(Ljava/lang/String;)Z",
                (jniEnv, thisObj, args) => JniValue.FromBool(false));

            // android.os.Handler stubs (common in games)
            env.RegisterNativeMethod("android.os.Handler", "post", "(Ljava/lang/Runnable;)Z",
                (jniEnv, thisObj, args) => JniValue.FromBool(true));
            env.RegisterNativeMethod("android.os.Handler", "postDelayed", "(Ljava/lang/Runnable;J)Z",
                (jniEnv, thisObj, args) => JniValue.FromBool(true));
            env.RegisterNativeMethod("android.os.Handler", "sendEmptyMessage", "(I)Z",
                (jniEnv, thisObj, args) => JniValue.FromBool(true));
            env.RegisterNativeMethod("android.os.Handler", "removeCallbacks", "(Ljava/lang/Runnable;)V",
                (jniEnv, thisObj, args) => JniValue.Void());

            // android.os.Looper stubs
            env.RegisterNativeMethod("android.os.Looper", "getMainLooper", "()Landroid/os/Looper;",
                (jniEnv, thisObj, args) => JniValue.FromObject("MainLooper"));
            env.RegisterNativeMethod("android.os.Looper", "myLooper", "()Landroid/os/Looper;",
                (jniEnv, thisObj, args) => JniValue.FromObject("CurrentLooper"));

            // android.os.Process.myPid
            env.RegisterNativeMethod("android.os.Process", "myPid", "()I",
                (jniEnv, thisObj, args) => JniValue.FromInt(1000));
        }

        /// <summary>
        /// Stubs for audio/media APIs commonly used by games.
        /// Reference: android-7.0.0_r1 frameworks/base/media/java/android/media/
        /// </summary>
        private static void RegisterMediaStubs(JniEnvironment env)
        {
            // android.media.AudioTrack.getMinBufferSize
            env.RegisterNativeMethod("android.media.AudioTrack", "getMinBufferSize", "(III)I",
                (jniEnv, thisObj, args) => JniValue.FromInt(4096));

            // android.media.AudioManager.getStreamVolume
            env.RegisterNativeMethod("android.media.AudioManager", "getStreamVolume", "(I)I",
                (jniEnv, thisObj, args) => JniValue.FromInt(7));

            // android.media.AudioManager.getStreamMaxVolume
            env.RegisterNativeMethod("android.media.AudioManager", "getStreamMaxVolume", "(I)I",
                (jniEnv, thisObj, args) => JniValue.FromInt(15));

            // android.media.SoundPool stubs
            env.RegisterNativeMethod("android.media.SoundPool", "load", "(Landroid/content/Context;II)I",
                (jniEnv, thisObj, args) => JniValue.FromInt(1));
            env.RegisterNativeMethod("android.media.SoundPool", "play", "(IFFIIF)I",
                (jniEnv, thisObj, args) => JniValue.FromInt(1));
            env.RegisterNativeMethod("android.media.SoundPool", "release", "()V",
                (jniEnv, thisObj, args) => JniValue.Void());

            // android.media.MediaPlayer stubs
            env.RegisterNativeMethod("android.media.MediaPlayer", "start", "()V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.media.MediaPlayer", "stop", "()V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.media.MediaPlayer", "pause", "()V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.media.MediaPlayer", "release", "()V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.media.MediaPlayer", "setVolume", "(FF)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.media.MediaPlayer", "setLooping", "(Z)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.media.MediaPlayer", "isPlaying", "()Z",
                (jniEnv, thisObj, args) => JniValue.FromBool(false));
        }

        /// <summary>
        /// Miscellaneous stubs for APIs commonly used by games.
        /// </summary>
        private static void RegisterMiscStubs(JniEnvironment env)
        {
            // android.os.Environment stubs
            env.RegisterNativeMethod("android.os.Environment", "getExternalStorageDirectory", "()Ljava/io/File;",
                (jniEnv, thisObj, args) => JniValue.FromObject("/sdcard"));
            env.RegisterNativeMethod("android.os.Environment", "getExternalStorageState", "()Ljava/lang/String;",
                (jniEnv, thisObj, args) => JniValue.FromObject("mounted"));
            env.RegisterNativeMethod("android.os.Environment", "getDataDirectory", "()Ljava/io/File;",
                (jniEnv, thisObj, args) => JniValue.FromObject("/data"));

            // android.net.ConnectivityManager stubs
            env.RegisterNativeMethod("android.net.ConnectivityManager", "getActiveNetworkInfo",
                "()Landroid/net/NetworkInfo;",
                (jniEnv, thisObj, args) => JniValue.FromObject(null));

            // android.provider.Settings.Secure.getString
            env.RegisterNativeMethod("android.provider.Settings$Secure", "getString",
                "(Landroid/content/ContentResolver;Ljava/lang/String;)Ljava/lang/String;",
                (jniEnv, thisObj, args) =>
                {
                    string setting = args.Length > 1 ? args[1].L as string ?? "" : "";
                    if (setting == "android_id")
                        return JniValue.FromObject("astoria_device_id");
                    return JniValue.FromObject(null);
                });

            // android.telephony.TelephonyManager stubs
            env.RegisterNativeMethod("android.telephony.TelephonyManager", "getDeviceId", "()Ljava/lang/String;",
                (jniEnv, thisObj, args) => JniValue.FromObject("000000000000000"));
            env.RegisterNativeMethod("android.telephony.TelephonyManager", "getNetworkCountryIso", "()Ljava/lang/String;",
                (jniEnv, thisObj, args) => JniValue.FromObject("us"));

            // java.util.Locale stubs
            env.RegisterNativeMethod("java.util.Locale", "getDefault", "()Ljava/util/Locale;",
                (jniEnv, thisObj, args) => JniValue.FromObject("en_US"));
            env.RegisterNativeMethod("java.util.Locale", "getLanguage", "()Ljava/lang/String;",
                (jniEnv, thisObj, args) => JniValue.FromObject("en"));
            env.RegisterNativeMethod("java.util.Locale", "getCountry", "()Ljava/lang/String;",
                (jniEnv, thisObj, args) => JniValue.FromObject("US"));

            // android.view.Window stubs
            env.RegisterNativeMethod("android.view.Window", "setFlags", "(II)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.view.Window", "addFlags", "(I)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.view.Window", "clearFlags", "(I)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.view.Window", "requestFeature", "(I)Z",
                (jniEnv, thisObj, args) => JniValue.FromBool(true));
            env.RegisterNativeMethod("android.view.Window", "getDecorView", "()Landroid/view/View;",
                (jniEnv, thisObj, args) => JniValue.FromObject("DecorView"));
            env.RegisterNativeMethod("android.view.Window", "setFormat", "(I)V",
                (jniEnv, thisObj, args) => JniValue.Void());

            // android.view.View stubs (common calls from games)
            env.RegisterNativeMethod("android.view.View", "setVisibility", "(I)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.view.View", "setSystemUiVisibility", "(I)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.view.View", "getSystemUiVisibility", "()I",
                (jniEnv, thisObj, args) => JniValue.FromInt(0));
            env.RegisterNativeMethod("android.view.View", "setOnSystemUiVisibilityChangeListener",
                "(Landroid/view/View$OnSystemUiVisibilityChangeListener;)V",
                (jniEnv, thisObj, args) => JniValue.Void());

            // android.widget.RelativeLayout stubs
            env.RegisterNativeMethod("android.widget.RelativeLayout", "<init>", "(Landroid/content/Context;)V",
                (jniEnv, thisObj, args) =>
                {
#if DEBUG
                    Debug.WriteLine("[Widget] RelativeLayout.<init>(Context)");
#endif
                    return JniValue.Void();
                });
            env.RegisterNativeMethod("android.widget.RelativeLayout", "addView", "(Landroid/view/View;)V",
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.widget.RelativeLayout", "setGravity", "(I)V",
                (jniEnv, thisObj, args) => JniValue.Void());

            // android.widget.FrameLayout stubs
            env.RegisterNativeMethod("android.widget.FrameLayout", "<init>", "(Landroid/content/Context;)V",
                (jniEnv, thisObj, args) => JniValue.Void());

            // android.widget.LinearLayout stubs
            env.RegisterNativeMethod("android.widget.LinearLayout", "<init>", "(Landroid/content/Context;)V",
                (jniEnv, thisObj, args) => JniValue.Void());

            // android.os.Build fields (accessed as static fields, but some games call getters)
            env.RegisterNativeMethod("android.os.Build", "MANUFACTURER", "()Ljava/lang/String;",
                (jniEnv, thisObj, args) => JniValue.FromObject("AstoriaUWP"));
            env.RegisterNativeMethod("android.os.Build", "MODEL", "()Ljava/lang/String;",
                (jniEnv, thisObj, args) => JniValue.FromObject("AOSP Emulator"));
            env.RegisterNativeMethod("android.os.Build", "DEVICE", "()Ljava/lang/String;",
                (jniEnv, thisObj, args) => JniValue.FromObject("generic"));

            // java.lang.Class stubs
            env.RegisterNativeMethod("java.lang.Class", "getName", "()Ljava/lang/String;",
                (jniEnv, thisObj, args) => JniValue.FromObject("java.lang.Object"));
            env.RegisterNativeMethod("java.lang.Class", "getSimpleName", "()Ljava/lang/String;",
                (jniEnv, thisObj, args) => JniValue.FromObject("Object"));

            // java.lang.Object stubs
            env.RegisterNativeMethod("java.lang.Object", "getClass", "()Ljava/lang/Class;",
                (jniEnv, thisObj, args) => JniValue.FromObject("Class"));
            env.RegisterNativeMethod("java.lang.Object", "hashCode", "()I",
                (jniEnv, thisObj, args) => JniValue.FromInt(thisObj?.GetHashCode() ?? 0));
            env.RegisterNativeMethod("java.lang.Object", "toString", "()Ljava/lang/String;",
                (jniEnv, thisObj, args) => JniValue.FromObject(thisObj?.ToString() ?? "null"));

            // java.lang.String stubs
            env.RegisterNativeMethod("java.lang.String", "length", "()I",
                (jniEnv, thisObj, args) =>
                {
                    string s = thisObj as string ?? "";
                    return JniValue.FromInt(s.Length);
                });
            env.RegisterNativeMethod("java.lang.String", "valueOf", "(I)Ljava/lang/String;",
                (jniEnv, thisObj, args) =>
                {
                    int val = args.Length > 0 ? args[0].I : 0;
                    return JniValue.FromObject(val.ToString());
                });

#if DEBUG
            Debug.WriteLine($"[JNI] Registered {env.RegisteredMethodCount} system library stubs.");
#endif
        }
    }
}
