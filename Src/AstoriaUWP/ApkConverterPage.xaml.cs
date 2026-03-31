// ApkConverterPage - Analyses an APK file for compatibility with AstoriaUWP's
// Android emulation layer and converts it into the installed format used by the
// emulator.  The page is accessible from the APK Converter button in the
// MainPage CommandBar.

using dex.net;
using DalvikUWPCSharp.Applet;
using DalvikUWPCSharp.Disassembly.APKReader;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace DalvikUWPCSharp
{
    /// <summary>
    /// Tool page that analyses an APK file for compatibility with the AstoriaUWP
    /// Android emulation layer and converts it into the format expected by the
    /// emulator (extracted + binary-XML-decoded app folder).
    /// </summary>
    public sealed partial class ApkConverterPage : Page
    {
        // -----------------------------------------------------------------------
        // Known native-method signatures that JniBridge registers at startup.
        // Format: "className.methodName" (signature intentionally omitted so that
        // method name look-ups are signature-agnostic).
        // -----------------------------------------------------------------------
        private static readonly HashSet<string> SupportedNativeMethods = new HashSet<string>
        {
            "android.util.Log.d",
            "android.util.Log.i",
            "android.util.Log.e",
            "android.util.Log.w",
            "android.util.Log.v",
            "android.util.Log.isLoggable",
            "android.util.Log.println",
            "java.lang.System.loadLibrary",
            "java.lang.System.nanoTime",
            "java.lang.System.currentTimeMillis",
            "android.os.Build$VERSION.SDK_INT",
            "android.os.SystemClock.uptimeMillis",
            "android.os.SystemClock.elapsedRealtime",
            "java.lang.Thread.currentThread",
            "java.lang.Runtime.getRuntime",
            "java.lang.Runtime.maxMemory",
            "java.lang.Runtime.totalMemory",
            "java.lang.Runtime.freeMemory",
            "java.lang.Runtime.availableProcessors",
            "android.app.Activity.getWindowManager",
            "android.app.Activity.getWindow",
            "android.app.Activity.finish",
            "android.app.Activity.runOnUiThread",
            "android.app.Activity.isFinishing",
            "android.app.Activity.getIntent",
            "android.content.Context.getSystemService",
            "android.content.Context.getPackageName",
            "android.content.Context.getApplicationContext",
            "android.content.Context.getResources",
            "android.content.Context.getAssets",
            "android.content.Context.getFilesDir",
            "android.content.Context.getCacheDir",
            "android.content.Context.getApplicationInfo",
            "android.content.Context.getPackageManager",
            "android.content.Context.getSharedPreferences",
            "android.content.Context.getClassLoader",
            "android.content.Context.getContentResolver",
            "android.content.res.AssetManager.open",
            "android.content.res.AssetManager.list",
            "android.opengl.GLSurfaceView.setRenderer",
            "android.opengl.GLSurfaceView.setEGLContextClientVersion",
            "android.opengl.GLSurfaceView.setPreserveEGLContextOnPause",
            "android.opengl.GLSurfaceView.requestRender",
            "android.opengl.GLSurfaceView.setRenderMode",
            "android.opengl.GLSurfaceView.setEGLConfigChooser",
            "android.opengl.GLSurfaceView.onResume",
            "android.opengl.GLSurfaceView.onPause",
            "android.opengl.GLSurfaceView.queueEvent",
            "android.opengl.GLES20.glClear",
            "android.opengl.GLES20.glClearColor",
            "android.opengl.GLES20.glViewport",
            "android.opengl.GLES20.glEnable",
            "android.opengl.GLES20.glDisable",
            "android.opengl.GLES20.glBlendFunc",
            "android.opengl.GLES20.glDepthFunc",
            "android.opengl.GLES20.glDepthMask",
            "android.opengl.GLES20.glColorMask",
            "android.opengl.GLES20.glScissor",
            "android.opengl.GLES20.glLineWidth",
            "android.opengl.GLES20.glCreateShader",
            "android.opengl.GLES20.glCreateProgram",
            "android.opengl.GLES20.glGenTextures",
            "android.opengl.GLES20.glBindTexture",
            "android.opengl.GLES20.glTexParameteri",
            "android.opengl.GLES20.glGetError",
            "android.opengl.GLES20.glGetIntegerv",
            "android.opengl.GLES20.glGetString",
            "android.opengl.GLES20.glUseProgram",
            "android.opengl.GLES20.glDeleteTextures",
            "android.opengl.GLES20.glActiveTexture",
            "android.opengl.GLES20.glDrawArrays",
            "android.opengl.GLES20.glDrawElements",
            "android.opengl.GLES20.glFlush",
            "android.opengl.GLES20.glFinish",
            "android.content.res.Resources.getDisplayMetrics",
            "android.content.res.Resources.getString",
            "android.content.res.Resources.getConfiguration",
            "android.content.res.Resources.getIdentifier",
            "android.view.WindowManager.getDefaultDisplay",
            "android.view.Display.getMetrics",
            "android.view.Display.getWidth",
            "android.view.Display.getHeight",
            "android.view.Display.getRotation",
            "android.os.Bundle.getString",
            "android.os.Bundle.getInt",
            "android.os.Bundle.containsKey",
            "android.os.Handler.post",
            "android.os.Handler.postDelayed",
            "android.os.Handler.sendEmptyMessage",
            "android.os.Handler.removeCallbacks",
            "android.os.Looper.getMainLooper",
            "android.os.Looper.myLooper",
            "android.os.Process.myPid",
            "android.media.AudioTrack.getMinBufferSize",
            "android.media.AudioManager.getStreamVolume",
            "android.media.AudioManager.getStreamMaxVolume",
            "android.media.SoundPool.load",
            "android.media.SoundPool.play",
            "android.media.SoundPool.release",
            "android.media.MediaPlayer.start",
            "android.media.MediaPlayer.stop",
            "android.media.MediaPlayer.pause",
            "android.media.MediaPlayer.release",
            "android.media.MediaPlayer.setVolume",
            "android.media.MediaPlayer.setLooping",
            "android.media.MediaPlayer.isPlaying",
            "android.os.Environment.getExternalStorageDirectory",
            "android.os.Environment.getExternalStorageState",
            "android.os.Environment.getDataDirectory",
            "android.net.ConnectivityManager.getActiveNetworkInfo",
            "android.provider.Settings$Secure.getString",
            "android.telephony.TelephonyManager.getDeviceId",
            "android.telephony.TelephonyManager.getNetworkCountryIso",
            "java.util.Locale.getDefault",
            "java.util.Locale.getLanguage",
            "java.util.Locale.getCountry",
            "android.view.Window.setFlags",
            "android.view.Window.addFlags",
            "android.view.Window.clearFlags",
            "android.view.Window.requestFeature",
            "android.view.Window.getDecorView",
            "android.view.Window.setFormat",
            "android.view.View.setVisibility",
            "android.view.View.setSystemUiVisibility",
            "android.view.View.getSystemUiVisibility",
            "android.view.View.setOnSystemUiVisibilityChangeListener",
            "android.widget.RelativeLayout.<init>",
            "android.widget.RelativeLayout.addView",
            "android.widget.RelativeLayout.setGravity",
            "android.widget.FrameLayout.<init>",
            "android.widget.LinearLayout.<init>",
            "android.os.Build.MANUFACTURER",
            "android.os.Build.MODEL",
            "android.os.Build.DEVICE",
            "java.lang.Class.getName",
            "java.lang.Class.getSimpleName",
            "java.lang.Object.getClass",
            "java.lang.Object.hashCode",
            "java.lang.Object.toString",
            "java.lang.String.length",
            "java.lang.String.valueOf",
        };

        private StorageFile _selectedApkFile = null;
        private readonly StringBuilder _log = new StringBuilder();

        public ApkConverterPage()
        {
            this.InitializeComponent();
        }

        // -------------------------------------------------------------------
        // Logging helpers
        // -------------------------------------------------------------------

        private void Log(string line)
        {
            _log.AppendLine(line);
            logBox.Text = _log.ToString();
            Debug.WriteLine("[ApkConverter] " + line);
        }

        private void SetStatus(string text)
        {
            statusText.Text = text;
        }

        private void SetBusy(bool busy)
        {
            progressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            browseButton.IsEnabled = !busy;
            analyzeButton.IsEnabled = !busy && _selectedApkFile != null;
            convertButton.IsEnabled = !busy && _selectedApkFile != null;
            clearButton.IsEnabled = !busy;
        }

        // -------------------------------------------------------------------
        // Browse button
        // -------------------------------------------------------------------

        private async void browseButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.Desktop,
                CommitButtonText = "Select APK"
            };
            picker.FileTypeFilter.Add(".apk");

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            _selectedApkFile = file;
            apkPathBox.Text = file.Path;
            analyzeButton.IsEnabled = true;
            convertButton.IsEnabled = true;

            _log.Clear();
            Log("APK selected: " + file.Name);
            SetStatus("APK selected. Press Analyze or Convert & Install.");
        }

        // -------------------------------------------------------------------
        // Analyze button
        // -------------------------------------------------------------------

        private async void analyzeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedApkFile == null) return;

            SetBusy(true);
            SetStatus("Analyzing APK...");
            _log.Clear();

            try
            {
                await AnalyzeApkAsync(_selectedApkFile);
            }
            catch (Exception ex)
            {
                Log("[ERROR] Analysis failed: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
                SetStatus("Analysis complete.");
            }
        }

        // -------------------------------------------------------------------
        // Convert & Install button
        // -------------------------------------------------------------------

        private async void convertButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedApkFile == null) return;

            SetBusy(true);
            SetStatus("Converting APK...");
            _log.Clear();

            try
            {
                // First run analysis so the user sees the compatibility report.
                await AnalyzeApkAsync(_selectedApkFile);

                Log("");
                Log("=== Starting conversion ===");

                // Use the existing DroidApp pipeline to copy, extract, and
                // decode the APK into the local Apps folder.
                var app = await DroidApp.CreateAsync(_selectedApkFile);

                Log("Extracting and decoding APK contents...");
                await app.Install();

                Log("");
                Log("=== Conversion complete ===");
                Log("The APK has been converted and installed.");
                Log("You can now launch it from the Apps list on the home screen.");

                SetStatus("Conversion successful.");
                convertButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                Log("[ERROR] Conversion failed: " + ex.Message);
                SetStatus("Conversion failed.");
            }
            finally
            {
                SetBusy(false);
            }
        }

        // -------------------------------------------------------------------
        // Clear button
        // -------------------------------------------------------------------

        private void clearButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedApkFile = null;
            apkPathBox.Text = string.Empty;
            _log.Clear();
            logBox.Text = "APK Converter ready. Select an APK file to begin.";
            analyzeButton.IsEnabled = false;
            convertButton.IsEnabled = false;
            SetStatus("Ready");
        }

        // -------------------------------------------------------------------
        // Back button
        // -------------------------------------------------------------------

        private void backButton_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(MainPage));
        }

        // -------------------------------------------------------------------
        // Helper: convert a DEX type descriptor to a dotted Java class name.
        // e.g. "Landroid/content/Context;" -> "android.content.Context"
        //      "[Ljava/lang/String;"        -> "java.lang.String"
        // -------------------------------------------------------------------
        private static string ConvertDexTypeNameToJavaClassName(string dexTypeName)
        {
            string name = dexTypeName.TrimStart('[').TrimStart('L').TrimEnd(';');
            return name.Replace('/', '.');
        }

        // -------------------------------------------------------------------
        // Core analysis logic – async Task so UI updates (Log calls) are safe
        // without any dispatcher marshalling.  Blocking I/O is offloaded via
        // Task.Run to keep the UI responsive.
        // -------------------------------------------------------------------

        private async Task AnalyzeApkAsync(StorageFile apkFile)
        {
            Log("=== APK Compatibility Analysis ===");
            Log("File : " + apkFile.Name);
            Log("Path : " + apkFile.Path);
            Log("");

            // ------------------------------------------------------------------
            // Read the APK into memory once using the UWP-safe async stream API
            // so we can open it as a ZipArchive multiple times without re-reading
            // the file from disk.
            // ------------------------------------------------------------------
            byte[] apkBytes;
            try
            {
                apkBytes = await Task.Run(async () =>
                {
                    using (var fileStream = await apkFile.OpenStreamForReadAsync())
                    using (var ms = new MemoryStream())
                    {
                        await fileStream.CopyToAsync(ms);
                        return ms.ToArray();
                    }
                });
            }
            catch (Exception ex)
            {
                Log("[ERROR] Could not read APK file: " + ex.Message);
                return;
            }

            // ------------------------------------------------------------------
            // Single-pass ZIP scan – collect all needed data in one traversal.
            // ------------------------------------------------------------------
            bool hasDex       = false;
            bool hasManifest  = false;
            bool hasResources = false;
            int  xmlFileCount = 0;
            int  totalEntries = 0;
            var  nativeLibs   = new List<string>();
            byte[] manifestBytes = null;
            byte[] resBytes      = null;
            byte[] dexBytes      = null;

            try
            {
                await Task.Run(() =>
                {
                    using (var zip = new ZipArchive(new MemoryStream(apkBytes), ZipArchiveMode.Read))
                    {
                        totalEntries = zip.Entries.Count;
                        foreach (ZipArchiveEntry entry in zip.Entries)
                        {
                            string name = entry.FullName;

                            if (name == "classes.dex")
                            {
                                hasDex = true;
                                using (var ms = new MemoryStream())
                                { entry.Open().CopyTo(ms); dexBytes = ms.ToArray(); }
                            }
                            else if (name == "AndroidManifest.xml")
                            {
                                hasManifest = true;
                                using (var ms = new MemoryStream())
                                { entry.Open().CopyTo(ms); manifestBytes = ms.ToArray(); }
                            }
                            else if (name == "resources.arsc")
                            {
                                hasResources = true;
                                using (var ms = new MemoryStream())
                                { entry.Open().CopyTo(ms); resBytes = ms.ToArray(); }
                            }

                            if (name.EndsWith(".xml")) xmlFileCount++;

                            // Detect native libraries in any lib/ sub-directory
                            // (e.g. lib/armeabi-v7a/libgame.so or lib/arm64-v8a/lib.so)
                            if (name.StartsWith("lib/") && name.EndsWith(".so"))
                                nativeLibs.Add(name);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Log("[ERROR] Could not open APK as ZIP: " + ex.Message);
                return;
            }

            Log("APK contents (" + totalEntries + " entries):");
            Log("  classes.dex       : " + (hasDex      ? "YES" : "NO - APK may not be supported"));
            Log("  AndroidManifest   : " + (hasManifest ? "YES" : "NO"));
            Log("  resources.arsc    : " + (hasResources ? "YES" : "NO"));
            Log("  Binary XML files  : " + xmlFileCount);
            Log("  Native .so libs   : " + nativeLibs.Count);

            if (nativeLibs.Count > 0)
            {
                Log("");
                Log("Native libraries found:");
                foreach (string lib in nativeLibs)
                    Log("  " + lib);
            }

            if (!hasDex)
            {
                Log("");
                Log("[WARN] No classes.dex found. This APK may rely entirely on native");
                Log("       code and cannot be run by the Dalvik emulator directly.");
                return;
            }

            // ------------------------------------------------------------------
            // Parse manifest for package metadata
            // ------------------------------------------------------------------
            Log("");
            Log("=== Manifest Info ===");

            if (manifestBytes != null && resBytes != null)
            {
                try
                {
                    ApkInfo appInfo = await Task.Run(() =>
                    {
                        var reader = new ApkReader();
                        return reader.extractInfo(manifestBytes, resBytes);
                    });

                    Log("  Package     : " + appInfo.packageName);
                    Log("  Label       : " + appInfo.label);
                    Log("  Version     : " + appInfo.versionName + " (code " + appInfo.versionCode + ")");
                    Log("  Min SDK     : " + appInfo.minSdkVersion);
                    Log("  Target SDK  : " + appInfo.targetSdkVersion);
                    Log("  Activity    : " + appInfo.mainActivity);

                    if (appInfo.Permissions != null && appInfo.Permissions.Count > 0)
                        Log("  Permissions : " + string.Join(", ", appInfo.Permissions));
                }
                catch (Exception ex)
                {
                    Log("  [WARN] Manifest parsing failed: " + ex.Message);
                }
            }
            else
            {
                Log("  [WARN] Could not locate manifest or resources inside APK.");
            }

            // ------------------------------------------------------------------
            // Scan DEX bytecode for Android framework API calls
            // ------------------------------------------------------------------
            Log("");
            Log("=== DEX API Scan ===");

            var usedFrameworkMethods = new HashSet<string>();
            int classCount = 0;

            if (dexBytes != null)
            {
                try
                {
                    var scanResult = await Task.Run(() =>
                    {
                        var found = new HashSet<string>();
                        int classes = 0;
                        using (var dexStream = new MemoryStream(dexBytes))
                        {
                            var dex = new Dex(dexStream);
                            foreach (Class cls in dex.GetClasses())
                            {
                                classes++;
                                foreach (Method method in cls.GetMethods())
                                {
                                    foreach (OpCode opcode in method.GetInstructions())
                                    {
                                        if (opcode is InvokeOpCode invoke)
                                        {
                                            var invokedMethod = dex.GetMethod(invoke.MethodIndex);
                                            string className = ConvertDexTypeNameToJavaClassName(
                                                dex.GetTypeName(invokedMethod.ClassIndex));
                                            string methodName = invokedMethod.Name;

                                            // Only track Android framework / java.* calls
                                            if (className.StartsWith("android.") ||
                                                className.StartsWith("java.")     ||
                                                className.StartsWith("javax."))
                                            {
                                                found.Add(className + "." + methodName);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        return (found, classes);
                    });

                    usedFrameworkMethods = scanResult.found;
                    classCount = scanResult.classes;
                }
                catch (Exception ex)
                {
                    Log("  [WARN] DEX scan failed: " + ex.Message);
                }
            }

            Log("  DEX classes scanned : " + classCount);
            Log("  Framework API calls : " + usedFrameworkMethods.Count + " unique methods");

            // ------------------------------------------------------------------
            // Compatibility report
            // ------------------------------------------------------------------
            Log("");
            Log("=== JNI Compatibility Report ===");

            var supported   = usedFrameworkMethods.Where(m => SupportedNativeMethods.Contains(m)).OrderBy(m => m).ToList();
            var unsupported = usedFrameworkMethods.Where(m => !SupportedNativeMethods.Contains(m)).OrderBy(m => m).ToList();

            Log("Supported methods called by this APK (" + supported.Count + "):");
            foreach (string m in supported)
                Log("  [OK]  " + m);

            Log("");
            Log("Unsupported / stub methods called by this APK (" + unsupported.Count + "):");
            foreach (string m in unsupported)
                Log("  [--]  " + m);

            Log("");

            int total = supported.Count + unsupported.Count;
            double pct = total > 0 ? (supported.Count * 100.0 / total) : 100.0;
            Log(string.Format("Compatibility: {0}/{1} framework methods supported ({2:0}%)",
                supported.Count, total, pct));

            if (pct >= 80)
                Log("Assessment: This APK is LIKELY compatible with AstoriaUWP.");
            else if (pct >= 50)
                Log("Assessment: This APK has PARTIAL compatibility. Some features may not work.");
            else
                Log("Assessment: This APK may have LIMITED compatibility. Many APIs are unsupported.");

            Log("");
            Log("Press 'Convert & Install' to extract and prepare the APK for emulation.");
        }
    }
}
