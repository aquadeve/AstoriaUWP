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

        // ── Symbol hooks from ExAndroidNativeEmu (symbol_hooks.py) ──────────────

        /// <summary>
        /// __system_property_get – reads an Android system property by name.
        /// Equivalent to ExAndroidNativeEmu SymbolHooks.system_property_get.
        /// </summary>
        public int SystemPropertyGet(string name, out string value)
        {
            if (_properties != null)
            {
                string v = _properties.Get(name);
                if (!string.IsNullOrEmpty(v))
                {
                    Debug.WriteLine($"[DynamicLinker] __system_property_get({name}) = {v}");
                    value = v;
                    return v.Length;
                }
            }
            Debug.WriteLine($"[DynamicLinker] __system_property_get({name}) – not found");
            value = string.Empty;
            return 0;
        }

        /// <summary>
        /// dladdr – look up the module that owns a given address.
        /// Equivalent to ExAndroidNativeEmu SymbolHooks.dladdr.
        /// Note: ElfLoader does not track runtime load address in this implementation,
        /// so this returns the best available match by library name.
        /// </summary>
        public bool DlAddr(ulong addr, out string moduleName, out ulong moduleBase)
        {
            // Without runtime load addresses we can only return the first registered module.
            // A full implementation would track base+size per loaded library.
            foreach (var kv in _loadedLibraries)
            {
                moduleName = kv.Key;
                moduleBase = 0; // load address not tracked in ElfLoader
                Debug.WriteLine($"[DynamicLinker] dladdr 0x{addr:X} → first module {moduleName}");
                return true;
            }
            moduleName = string.Empty;
            moduleBase = 0;
            return false;
        }

        /// <summary>dl_unwind_find_exidx – ARM exception index stub; always returns null.</summary>
        public ulong DlUnwindFindExidx(ulong pc)
        {
            Debug.WriteLine($"[DynamicLinker] dl_unwind_find_exidx pc=0x{pc:X} – stub 0");
            return 0;
        }

        // ── pthread stubs (ExAndroidNativeEmu SymbolHooks pthread_*) ─────────────

        private static int _nextPthreadId = 32145;

        /// <summary>
        /// pthread_create stub – returns a fake thread ID; no real thread is spawned.
        /// Equivalent to ExAndroidNativeEmu SymbolHooks.pthread_create.
        /// </summary>
        public int PthreadCreate(out int threadId, ulong startRoutine, ulong arg)
        {
            threadId = _nextPthreadId++;
            Debug.WriteLine($"[DynamicLinker] pthread_create start_routine=0x{startRoutine:X} – stub tid={threadId}");
            return 0; // success
        }

        /// <summary>pthread_join stub – always succeeds immediately.</summary>
        public int PthreadJoin(int threadId)
        {
            Debug.WriteLine($"[DynamicLinker] pthread_join tid={threadId} – stub");
            return 0;
        }

        /// <summary>pthread_detach stub.</summary>
        public int PthreadDetach(int threadId)
        {
            Debug.WriteLine($"[DynamicLinker] pthread_detach tid={threadId} – stub");
            return 0;
        }

        /// <summary>pthread_attr_init stub.</summary>
        public int PthreadAttrInit() => 0;

        /// <summary>pthread_attr_destroy stub.</summary>
        public int PthreadAttrDestroy() => 0;

        /// <summary>pthread_attr_setdetachstate stub.</summary>
        public int PthreadAttrSetDetachState(int state) => 0;

        /// <summary>pthread_attr_setstacksize stub.</summary>
        public int PthreadAttrSetStackSize(ulong size) => 0;

        // ── C runtime helpers ─────────────────────────────────────────────────

        private static readonly Random _rng = new Random();

        /// <summary>
        /// rand / random – returns a pseudo-random integer in [0, RAND_MAX].
        /// Equivalent to ExAndroidNativeEmu SymbolHooks.rand.
        /// </summary>
        public int Rand() => _rng.Next(0, int.MaxValue);

        /// <summary>
        /// srand / srandom – seed the RNG; stub (our RNG is already seeded by .NET).
        /// </summary>
        public void SRand(uint seed) { /* no-op */ }

        /// <summary>
        /// newlocale – returns 0 (null locale); old libc may not have this symbol.
        /// Equivalent to ExAndroidNativeEmu SymbolHooks.newlocale.
        /// </summary>
        public ulong NewLocale(int mask, string locale, ulong baseLocale)
        {
            Debug.WriteLine($"[DynamicLinker] newlocale – stub 0");
            return 0;
        }

        /// <summary>uselocale stub.</summary>
        public ulong UseLocale(ulong locale) => 0;

        /// <summary>freelocale stub.</summary>
        public void FreeLocale(ulong locale) { }

        /// <summary>
        /// abort – signal that the guest called abort().
        /// Equivalent to ExAndroidNativeEmu SymbolHooks.abort.
        /// </summary>
        public void Abort()
        {
            throw new Exception("[DynamicLinker] abort() called by guest code");
        }

        /// <summary>__cxa_thread_atexit_impl stub – thread-local destructor registration.</summary>
        public int CxaThreadAtExitImpl() => 0;

        /// <summary>__cxa_atexit stub.</summary>
        public int CxaAtExit() => 0;

        /// <summary>pthread_once stub – always treats the once_control as already executed.</summary>
        public int PthreadOnce() => 0;

        /// <summary>pthread_key_create stub.</summary>
        public int PthreadKeyCreate() => 0;

        /// <summary>pthread_key_delete stub.</summary>
        public int PthreadKeyDelete() => 0;

        /// <summary>pthread_setspecific stub.</summary>
        public int PthreadSetSpecific() => 0;

        /// <summary>pthread_getspecific stub – returns 0.</summary>
        public ulong PthreadGetSpecific() => 0;

        /// <summary>pthread_mutex_init stub.</summary>
        public int PthreadMutexInit() => 0;

        /// <summary>pthread_mutex_destroy stub.</summary>
        public int PthreadMutexDestroy() => 0;

        /// <summary>pthread_mutex_lock stub.</summary>
        public int PthreadMutexLock() => 0;

        /// <summary>pthread_mutex_trylock stub.</summary>
        public int PthreadMutexTryLock() => 0;

        /// <summary>pthread_mutex_unlock stub.</summary>
        public int PthreadMutexUnlock() => 0;

        /// <summary>pthread_cond_init stub.</summary>
        public int PthreadCondInit() => 0;

        /// <summary>pthread_cond_signal stub.</summary>
        public int PthreadCondSignal() => 0;

        /// <summary>pthread_cond_broadcast stub.</summary>
        public int PthreadCondBroadcast() => 0;

        /// <summary>pthread_cond_wait stub.</summary>
        public int PthreadCondWait() => 0;

        /// <summary>pthread_cond_destroy stub.</summary>
        public int PthreadCondDestroy() => 0;

        /// <summary>pthread_rwlock_init stub.</summary>
        public int PthreadRwlockInit() => 0;

        /// <summary>pthread_rwlock_rdlock stub.</summary>
        public int PthreadRwlockRdlock() => 0;

        /// <summary>pthread_rwlock_wrlock stub.</summary>
        public int PthreadRwlockWrlock() => 0;

        /// <summary>pthread_rwlock_unlock stub.</summary>
        public int PthreadRwlockUnlock() => 0;

        /// <summary>pthread_rwlock_destroy stub.</summary>
        public int PthreadRwlockDestroy() => 0;
    }
}
