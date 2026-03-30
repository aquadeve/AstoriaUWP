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
            return NewLocalRef(dotName);
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
                (jniEnv, thisObj, args) => JniValue.Void());
            env.RegisterNativeMethod("android.opengl.GLES20", "glClearColor", "(FFFF)V",
                (jniEnv, thisObj, args) => JniValue.Void());
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
