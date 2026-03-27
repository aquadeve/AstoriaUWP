// DynamicLinker.cs
// Managed C# stub implementation of the Android dynamic linker.
// Translated from referenceBridge/BridgeLib/linker.cpp (dlopen, dlsym, dlclose stubs).

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DalvikUWPCSharp.Classes
{
    /// <summary>
    /// Provides managed stubs for Android dynamic linker functions (dlopen/dlsym/dlclose).
    /// Based on referenceBridge/BridgeLib/linker.cpp.
    /// </summary>
    public class DynamicLinker
    {
        private readonly AndroidProperties _properties;
        private readonly Dictionary<string, ElfLoader> _loadedLibraries =
            new Dictionary<string, ElfLoader>(StringComparer.OrdinalIgnoreCase);

        private uint _targetSdkVersion;

        public DynamicLinker(AndroidProperties properties)
        {
            _properties = properties;
        }

        /// <summary>Called by DalvikCPU after each .so is parsed to register it with the linker.</summary>
        public void RegisterLoadedLibrary(string name, ElfLoader loader)
        {
            _loadedLibraries[name] = loader;
            Debug.WriteLine("[DynamicLinker] Registered: " + name);
        }

        // ── linker.cpp equivalents ────────────────────────────────────────

        /// <summary>dlopen stub – returns a handle token for the requested library name.</summary>
        public string DlOpen(string filename, int flags)
        {
            if (string.IsNullOrEmpty(filename))
            {
                Debug.WriteLine("[DynamicLinker] dlopen: null filename");
                return null;
            }

            string libName = System.IO.Path.GetFileName(filename);
            Debug.WriteLine("[DynamicLinker] dlopen: " + libName + " flags=" + flags);

            if (_loadedLibraries.ContainsKey(libName))
                return libName;

            // Return the name as an opaque handle so callers can dlsym against it
            return libName;
        }

        /// <summary>dlsym stub – looks up a symbol name in a loaded ELF library.</summary>
        public string DlSym(string handle, string symbol)
        {
            Debug.WriteLine("[DynamicLinker] dlsym: handle=" + handle + " symbol=" + symbol);

            if (handle != null && _loadedLibraries.TryGetValue(handle, out ElfLoader loader))
            {
                if (loader.ExportedFunctions.Contains(symbol))
                {
                    Debug.WriteLine("[DynamicLinker] dlsym found: " + symbol);
                    return symbol;
                }
            }

            Debug.WriteLine("[DynamicLinker] dlsym not found: " + symbol);
            return null;
        }

        /// <summary>dlclose stub – unregisters a library handle.</summary>
        public void DlClose(string handle)
        {
            Debug.WriteLine("[DynamicLinker] dlclose: " + handle);
            // In a full implementation this would decrement refcount and unload
        }

        /// <summary>dlerror stub.</summary>
        public string DlError()
        {
            return null;
        }

        /// <summary>android_set_application_target_sdk_version stub.</summary>
        public void SetTargetSdkVersion(uint version)
        {
            _targetSdkVersion = version;
            _properties.Set("ro.build.version.sdk", version.ToString());
            Debug.WriteLine("[DynamicLinker] Target SDK version: " + version);
        }

        /// <summary>android_get_application_target_sdk_version stub.</summary>
        public uint GetTargetSdkVersion() => _targetSdkVersion;

        /// <summary>android_get_LD_LIBRARY_PATH stub.</summary>
        public string GetLdLibraryPath()
        {
            return "/system/lib:/vendor/lib:/data/app-lib";
        }

        /// <summary>android_create_namespace stub.</summary>
        public object CreateNamespace(string name, string ldLibraryPath, string defaultLibraryPath)
        {
            Debug.WriteLine("[DynamicLinker] android_create_namespace: " + name);
            return null;
        }

        /// <summary>android_init_namespaces stub.</summary>
        public bool InitNamespaces(string publicNsSonames, string anonNsLibraryPath)
        {
            Debug.WriteLine("[DynamicLinker] android_init_namespaces");
            return true;
        }
    }
}
