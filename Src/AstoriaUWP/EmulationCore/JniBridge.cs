// JniBridge - Provides a managed JNI-like interface for Android native method calls.
// Inspired by apkenv's JNI implementation that bridges native Android libraries.

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

            Debug.WriteLine("[JNI] Native method not found: " + key);
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

            Debug.WriteLine("[JNI] FindClass failed: " + jniClassName);
            return 0;
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
    }

    /// <summary>
    /// Provides stub implementations of common Android system libraries.
    /// Maps Android libc/liblog/libandroid calls to UWP equivalents.
    /// Inspired by apkenv's approach of hooking system library functions.
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
        }

        private static void RegisterSystemStubs(JniEnvironment env)
        {
            // System.loadLibrary(String) - track requested native libs
            env.RegisterNativeMethod("java.lang.System", "loadLibrary", "(Ljava/lang/String;)V",
                (jniEnv, thisObj, args) =>
                {
                    string libName = args.Length > 0 ? args[0].L as string ?? "" : "";
                    Debug.WriteLine("[System.loadLibrary] Requested: " + libName);
                    return JniValue.FromInt(0);
                });

            // android/os/Build.VERSION.SDK_INT
            env.RegisterNativeMethod("android.os.Build$VERSION", "SDK_INT", "()I",
                (jniEnv, thisObj, args) =>
                {
                    return JniValue.FromInt(28); // Android 9 (Pie)
                });
        }

        private static void RegisterActivityStubs(JniEnvironment env)
        {
            // android.app.Activity.getWindowManager
            env.RegisterNativeMethod("android.app.Activity", "getWindowManager", "()Landroid/view/WindowManager;",
                (jniEnv, thisObj, args) => JniValue.FromObject(null));

            // android.app.Activity.getWindow
            env.RegisterNativeMethod("android.app.Activity", "getWindow", "()Landroid/view/Window;",
                (jniEnv, thisObj, args) => JniValue.FromObject(null));

            // android.app.Activity.finish - stub to allow games to signal exit
            env.RegisterNativeMethod("android.app.Activity", "finish", "()V",
                (jniEnv, thisObj, args) =>
                {
                    Debug.WriteLine("[Activity] finish() called");
                    return JniValue.FromInt(0);
                });

            // android.app.Activity.runOnUiThread - stub (runs synchronously in our emulator)
            env.RegisterNativeMethod("android.app.Activity", "runOnUiThread", "(Ljava/lang/Runnable;)V",
                (jniEnv, thisObj, args) =>
                {
                    Debug.WriteLine("[Activity] runOnUiThread called (stub)");
                    return JniValue.FromInt(0);
                });

            // android.content.Context.getSystemService
            env.RegisterNativeMethod("android.content.Context", "getSystemService", "(Ljava/lang/String;)Ljava/lang/Object;",
                (jniEnv, thisObj, args) =>
                {
                    string service = args.Length > 0 ? args[0].L as string ?? "" : "";
                    Debug.WriteLine("[Context] getSystemService: " + service);
                    return JniValue.FromObject(null);
                });

            // android.content.Context.getPackageName
            env.RegisterNativeMethod("android.content.Context", "getPackageName", "()Ljava/lang/String;",
                (jniEnv, thisObj, args) => JniValue.FromObject("com.emulated.app"));

            // android.content.res.AssetManager stubs
            env.RegisterNativeMethod("android.content.res.AssetManager", "open", "(Ljava/lang/String;)Ljava/io/InputStream;",
                (jniEnv, thisObj, args) =>
                {
                    string assetName = args.Length > 0 ? args[0].L as string ?? "" : "";
                    Debug.WriteLine("[AssetManager] open: " + assetName);
                    return JniValue.FromObject(null);
                });
        }

        private static void RegisterGLStubs(JniEnvironment env)
        {
            // android.opengl.GLSurfaceView.setRenderer
            env.RegisterNativeMethod("android.opengl.GLSurfaceView", "setRenderer",
                "(Landroid/opengl/GLSurfaceView$Renderer;)V",
                (jniEnv, thisObj, args) =>
                {
                    Debug.WriteLine("[GLSurfaceView] setRenderer called - native renderer attached.");
                    return JniValue.FromInt(0);
                });

            // android.opengl.GLSurfaceView.setEGLContextClientVersion
            env.RegisterNativeMethod("android.opengl.GLSurfaceView", "setEGLContextClientVersion", "(I)V",
                (jniEnv, thisObj, args) =>
                {
                    int version = args.Length > 0 ? args[0].I : 0;
                    Debug.WriteLine("[GLSurfaceView] setEGLContextClientVersion: " + version);
                    return JniValue.FromInt(0);
                });

            // android.opengl.GLSurfaceView.setPreserveEGLContextOnPause
            env.RegisterNativeMethod("android.opengl.GLSurfaceView", "setPreserveEGLContextOnPause", "(Z)V",
                (jniEnv, thisObj, args) =>
                {
                    Debug.WriteLine("[GLSurfaceView] setPreserveEGLContextOnPause: " + (args.Length > 0 ? args[0].Z.ToString() : "?"));
                    return JniValue.FromInt(0);
                });

            // android.opengl.GLSurfaceView.requestRender
            env.RegisterNativeMethod("android.opengl.GLSurfaceView", "requestRender", "()V",
                (jniEnv, thisObj, args) =>
                {
                    Debug.WriteLine("[GLSurfaceView] requestRender called");
                    return JniValue.FromInt(0);
                });

            // android.opengl.GLSurfaceView.onResume
            env.RegisterNativeMethod("android.opengl.GLSurfaceView", "onResume", "()V",
                (jniEnv, thisObj, args) =>
                {
                    Debug.WriteLine("[GLSurfaceView] onResume called");
                    return JniValue.FromInt(0);
                });

            // android.opengl.GLSurfaceView.onPause
            env.RegisterNativeMethod("android.opengl.GLSurfaceView", "onPause", "()V",
                (jniEnv, thisObj, args) =>
                {
                    Debug.WriteLine("[GLSurfaceView] onPause called");
                    return JniValue.FromInt(0);
                });

            // android.opengl.GLES20 stubs for basic GL calls
            env.RegisterNativeMethod("android.opengl.GLES20", "glClear", "(I)V",
                (jniEnv, thisObj, args) => JniValue.FromInt(0));
            env.RegisterNativeMethod("android.opengl.GLES20", "glClearColor", "(FFFF)V",
                (jniEnv, thisObj, args) => JniValue.FromInt(0));
            env.RegisterNativeMethod("android.opengl.GLES20", "glViewport", "(IIII)V",
                (jniEnv, thisObj, args) =>
                {
                    if (args.Length >= 4)
                        Debug.WriteLine("[GLES20] glViewport(" + args[0].I + "," + args[1].I + "," + args[2].I + "," + args[3].I + ")");
                    return JniValue.FromInt(0);
                });
        }
    }
}
