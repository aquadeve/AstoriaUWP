// AndroidProperties.cs
// Managed C# implementation of Android system properties.
// Translated from referenceBridge/BridgeLib/android_init.cpp which sets up
// Android runtime properties, environment variables, and ABI detection.

using DalvikUWPCSharp.Classes;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DalvikUWPCSharp.Classes
{
    /// <summary>
    /// Manages Android system properties (__system_property_get / __system_property_set).
    /// Mirrors the property initialisation performed by referenceBridge android_init.cpp.
    /// </summary>
    public class AndroidProperties
    {
        private readonly Dictionary<string, string> _properties =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public AndroidProperties()
        {
            InitialiseDefaults();
        }

        // ── Public API ────────────────────────────────────────────────────

        /// <summary>Gets a system property value, or <paramref name="defaultValue"/> if not found.</summary>
        public string Get(string name, string defaultValue = "")
        {
            return _properties.TryGetValue(name, out string val) ? val : defaultValue;
        }

        /// <summary>Sets a system property value.</summary>
        public void Set(string name, string value)
        {
            if (!string.IsNullOrEmpty(name) && value != null)
            {
                _properties[name] = value;
                Debug.WriteLine("[AndroidProperties] set " + name + "=" + value);
            }
        }

        /// <summary>Returns true if the property exists.</summary>
        public bool Contains(string name) => _properties.ContainsKey(name);

        /// <summary>Exposes all properties for enumeration.</summary>
        public IReadOnlyDictionary<string, string> All => _properties;

        // ── Default property initialisation (android_init.cpp) ────────────

        private void InitialiseDefaults()
        {
            // ── Build identification (ro.build.*) ─────────────────────────
            Set("ro.build.version.sdk",       "28");   // Android 9 / Pie
            Set("ro.build.version.codename",  "REL");
            Set("ro.build.version.release",   "9");
            Set("ro.build.version.incremental","5124027");
            Set("ro.build.version.security_patch", "2018-08-05");
            Set("ro.build.id",                "PI");
            Set("ro.build.display.id",        "PPR2.180905.006");
            Set("ro.build.type",              "userdebug");
            Set("ro.build.tags",              "release-keys");
            Set("ro.build.flavor",            "generic_x86-userdebug");
            Set("ro.build.date",              "Thu Sep  6 08:42:17 UTC 2018");
            Set("ro.build.date.utc",          "1536223337");
            Set("ro.build.user",              "android-build");
            Set("ro.build.host",              "abfarm-us-east1-c-0092");

            // ── Product identification (ro.product.*) ─────────────────────
            // Values are adjusted based on platform at runtime via XboxPlatform
            string model = GetRuntimeModel();
            string abi   = XboxPlatform.GetAndroidAbiName();

            Set("ro.product.model",    model);
            Set("ro.product.name",     "generic_" + abi);
            Set("ro.product.device",   "generic_" + abi);
            Set("ro.product.board",    "");
            Set("ro.product.brand",    "generic");
            Set("ro.product.manufacturer", "unknown");

            // ── ABI lists (ro.product.cpu.*) ─────────────────────────────
            Set("ro.product.cpu.abi",     GetCpuAbi());
            Set("ro.product.cpu.abi2",    GetCpuAbi2());
            Set("ro.product.cpu.abilist", GetAbiList());
            Set("ro.product.cpu.abilist32", GetAbiList32());
            Set("ro.product.cpu.abilist64", GetAbiList64());

            // ── Dalvik VM tuning ──────────────────────────────────────────
            Set("dalvik.vm.heapsize",               "256m");
            Set("dalvik.vm.heapgrowthlimit",         "96m");
            Set("dalvik.vm.heapminfree",             "2m");
            Set("dalvik.vm.heapmaxfree",             "8m");
            Set("dalvik.vm.heaptargetutilization",   "0.75");
            Set("dalvik.vm.dex2oat-filter",          "quicken");
            Set("dalvik.vm.dex2oat-flags",           "--no-watch-dog");
            Set("dalvik.vm.image-dex2oat-filter",    "speed");

            string isaPropertyName    = GetIsaPropertyName();
            string isaFeaturesName    = GetIsaFeaturesPropertyName();
            Set(isaPropertyName,    GetIsaVariant());
            Set(isaFeaturesName,    "default");

            // ── Android runtime ───────────────────────────────────────────
            Set("ro.dalvik.vm.native.bridge",  "0");
            Set("persist.sys.dalvik.vm.lib.2", "libart.so");

            // ── Hardware/SoC ──────────────────────────────────────────────
            Set("ro.hardware",         "ranchu");
            Set("ro.revision",         "0");
            Set("ro.arch",             GetArchName());

            // ── Logging ───────────────────────────────────────────────────
            Set("log.tag.art",         "WARNING");
            Set("log.tag.EGL_emulation","DEBUG");
            Set("log.tag.OpenGLRenderer","DEBUG");

            // ── OpenGL / graphics ─────────────────────────────────────────
            Set("ro.opengles.version", "131072");   // OpenGL ES 2.0

            // ── Locale & timezone ─────────────────────────────────────────
            Set("persist.sys.language", "en");
            Set("persist.sys.country",  "US");
            Set("persist.sys.timezone", "America/New_York");

            // ── Security ──────────────────────────────────────────────────
            Set("ro.secure",          "1");
            Set("ro.allow.mock.location", "0");
            Set("ro.debuggable",      "1");
            Set("service.adb.root",   "1");

            // ── Android app-process path (android_init.cpp) ───────────────
            Set("ro.zygote",          "zygote32");

            Debug.WriteLine("[AndroidProperties] Initialised " + _properties.Count + " properties.");
        }

        // ── Architecture helpers (mirror android_init.cpp) ────────────────

        private static ProcessorArch Arch => XboxPlatform.GetProcessorArchitecture();
        private static bool IsArm => Arch == ProcessorArch.Arm || Arch == ProcessorArch.Arm64;
        private static bool IsX64 => Arch == ProcessorArch.X64;

        private static string GetRuntimeModel()
        {
            if (IsArm) return "AOSP on ARM Emulator.";
            if (IsX64) return "AOSP on x64 Emulator.";
            return "AOSP on x86 Emulator.";
        }

        private static string GetCpuAbi()
        {
            if (Arch == ProcessorArch.Arm64) return "arm64-v8a";
            if (IsArm) return "armeabi-v7a";
            if (IsX64) return "x86_64";
            return "x86";
        }

        private static string GetCpuAbi2()
        {
            return IsArm ? "armeabi" : "";
        }

        private static string GetAbiList()
        {
            if (Arch == ProcessorArch.Arm64) return "arm64-v8a,armeabi-v7a,armeabi";
            if (IsArm) return "armeabi-v7a,armeabi";
            if (IsX64) return "x86_64";
            return "x86";
        }

        private static string GetAbiList32()
        {
            if (IsArm) return "armeabi-v7a,armeabi";
            if (IsX64) return "";
            return "x86";
        }

        private static string GetAbiList64()
        {
            if (Arch == ProcessorArch.Arm64) return "arm64-v8a";
            if (IsX64) return "x86_64";
            return "";
        }

        private static string GetIsaPropertyName()
        {
            if (IsArm) return "dalvik.vm.isa.arm.variant";
            if (IsX64) return "dalvik.vm.isa.x86_64.variant";
            return "dalvik.vm.isa.x86.variant";
        }

        private static string GetIsaFeaturesPropertyName()
        {
            if (IsArm) return "dalvik.vm.isa.arm.features";
            if (IsX64) return "dalvik.vm.isa.x86_64.features";
            return "dalvik.vm.isa.x86.features";
        }

        private static string GetIsaVariant()
        {
            return IsArm ? "cortex-a7" : "default";
        }

        private static string GetArchName()
        {
            if (IsArm) return "arm";
            if (IsX64) return "x86_64";
            return "x86";
        }
    }
}
