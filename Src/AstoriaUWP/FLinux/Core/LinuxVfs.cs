// LinuxVfs - Virtual filesystem for the FLinux C# port.
// Converted from FLinux src/syscall/vfs.c + src/fs/ layer.
// Maps Android-style Linux paths to real StorageFolder / Stream objects,
// and provides open / read / write / stat / close syscall implementations
// that the SyscallDispatcher can call.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace DalvikUWPCSharp.FLinux.Core
{
    /// <summary>
    /// Linux open() flags (O_* constants from fcntl.h).
    /// Mirrors FLinux src/common/fcntl.h.
    /// </summary>
    public static class OFlags
    {
        public const int RDONLY   = 0;
        public const int WRONLY   = 1;
        public const int RDWR     = 2;
        public const int CREAT    = 0x0040;
        public const int EXCL     = 0x0080;
        public const int NOCTTY   = 0x0100;
        public const int TRUNC    = 0x0200;
        public const int APPEND   = 0x0400;
        public const int NONBLOCK = 0x0800;
        public const int SYNC     = 0x1000;
        public const int NOFOLLOW = 0x20000;
        public const int DIRECTORY= 0x10000;
        public const int CLOEXEC  = 0x80000;
    }

    /// <summary>
    /// Linux stat structure (simplified).
    /// Mirrors struct stat from FLinux src/common/stat.h.
    /// </summary>
    public class LinuxStat
    {
        public ulong Dev;
        public ulong Ino;
        public uint  Mode;
        public uint  Nlink;
        public uint  Uid;
        public uint  Gid;
        public ulong RDev;
        public long  Size;
        public long  BlkSize;
        public long  Blocks;
        public long  AtimeSec;
        public long  MtimeSec;
        public long  CtimeSec;
    }

    /// <summary>
    /// Virtual filesystem layer.
    /// Combines FLinux winfs.c, devfs.c, procfs.c, and the Android binder/ashmem
    /// device stubs into a single managed class.
    /// Maps guest paths like /data/data/com.example, /proc/self, /dev/null, etc.
    /// to real Windows Storage objects or in-memory streams.
    /// </summary>
    public class LinuxVfs
    {
        // Host folder that represents /data/data (the APK install root).
        private StorageFolder _dataRoot;

        // Mount table: guest prefix → real host folder path.
        private readonly Dictionary<string, StorageFolder> _mounts
            = new Dictionary<string, StorageFolder>(StringComparer.Ordinal);

        // In-memory /proc and /sys virtual files (path → content).
        private readonly Dictionary<string, string> _procFiles
            = new Dictionary<string, string>(StringComparer.Ordinal);

        public LinuxVfs()
        {
            // Populate /proc virtual files (mirrors FLinux procfs.c).
            _procFiles["/proc/version"]    = "Linux version 3.18.0-astoria (astoria@uwp) (gcc version 4.9) #1 SMP\n";
            _procFiles["/proc/cpuinfo"]    = BuildCpuInfo();
            _procFiles["/proc/meminfo"]    = "MemTotal: 2097152 kB\nMemFree: 1048576 kB\n";
            _procFiles["/proc/self/maps"]  = ""; // populated at runtime
            _procFiles["/proc/self/stat"]  = "1 (app_process) S 0 1 1 0 -1 4210944 0 0 0 0 0 0 0 0 20 0 1 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0\n";
            _procFiles["/proc/self/status"]= "Name: app_process\nPid: 1\nUid: 0 0 0 0\nGid: 0 0 0 0\n";

            Debug.WriteLine("[LinuxVfs] Virtual filesystem initialised.");
        }

        /// <summary>Set the host folder used as the root of /data/data.</summary>
        public void SetDataRoot(StorageFolder folder)
        {
            _dataRoot = folder;
            Debug.WriteLine($"[LinuxVfs] /data/data mapped to {folder?.Path}");
        }

        /// <summary>Mount a real host folder at a guest path prefix.</summary>
        public void Mount(string guestPrefix, StorageFolder hostFolder)
        {
            _mounts[guestPrefix] = hostFolder;
            Debug.WriteLine($"[LinuxVfs] mount {guestPrefix} -> {hostFolder?.Path}");
        }

        // ── open ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Open a guest path.  Returns a Stream or null on failure.
        /// Mirrors FLinux sys_open() / sys_openat().
        /// </summary>
        public async Task<Stream> Open(string path, int flags)
        {
            // /dev/null
            if (path == "/dev/null" || path == "/dev/zero")
                return Stream.Null;

            // /dev/random, /dev/urandom – random bytes.
            if (path == "/dev/random" || path == "/dev/urandom")
                return new RandomDevStream();

            // /dev/ashmem – Android shared memory (stub: returns MemoryStream).
            if (path == "/dev/ashmem")
                return new MemoryStream();

            // /proc/* – virtual file.
            if (_procFiles.TryGetValue(path, out string procContent))
                return new MemoryStream(Encoding.UTF8.GetBytes(procContent));

            // Try real StorageFolder mounts.
            var stream = await TryOpenFromMounts(path, flags);
            if (stream != null) return stream;

            // /data/data/…  mapped to _dataRoot.
            if (_dataRoot != null && path.StartsWith("/data/"))
            {
                string rel = path.Substring("/data/".Length);
                return await TryOpenRelative(_dataRoot, rel, flags);
            }

            Debug.WriteLine($"[LinuxVfs] open: not found: {path}");
            return null;
        }

        // ── read / write ─────────────────────────────────────────────────────

        /// <summary>
        /// Read up to <paramref name="count"/> bytes from <paramref name="stream"/>
        /// into <paramref name="buf"/> at <paramref name="offset"/>.
        /// Returns bytes read or negative errno.
        /// Mirrors FLinux sys_read().
        /// </summary>
        public int Read(Stream stream, byte[] buf, int offset, int count)
        {
            if (stream == null || !stream.CanRead) return -9; // EBADF
            try
            {
                return stream.Read(buf, offset, count);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LinuxVfs] read error: {ex.Message}");
                return -5; // EIO
            }
        }

        /// <summary>
        /// Write <paramref name="count"/> bytes to <paramref name="stream"/>.
        /// Returns bytes written or negative errno.
        /// Mirrors FLinux sys_write().
        /// </summary>
        public int Write(Stream stream, byte[] buf, int offset, int count)
        {
            if (stream == null || !stream.CanWrite) return -9; // EBADF
            try
            {
                stream.Write(buf, offset, count);
                return count;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LinuxVfs] write error: {ex.Message}");
                return -5; // EIO
            }
        }

        // ── stat ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Stat a guest path.
        /// Returns null on ENOENT / EACCES.
        /// Mirrors FLinux sys_newstat().
        /// </summary>
        public async Task<LinuxStat> Stat(string path)
        {
            if (_procFiles.ContainsKey(path))
            {
                return new LinuxStat
                {
                    Mode    = 0x8180, // S_IFREG | 0600
                    Size    = _procFiles[path].Length,
                    Nlink   = 1,
                    MtimeSec= DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
            }

            // Try StorageFolder.
            StorageFile file = await TryGetStorageFile(path);
            if (file != null)
            {
                var props = await file.GetBasicPropertiesAsync();
                return new LinuxStat
                {
                    Mode    = 0x8180,
                    Size    = (long)props.Size,
                    Nlink   = 1,
                    MtimeSec= props.DateModified.ToUnixTimeSeconds()
                };
            }

            return null; // ENOENT
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private async Task<Stream> TryOpenFromMounts(string path, int flags)
        {
            foreach (var kv in _mounts)
            {
                if (!path.StartsWith(kv.Key)) continue;
                string rel = path.Substring(kv.Key.Length).TrimStart('/');
                var stream = await TryOpenRelative(kv.Value, rel, flags);
                if (stream != null) return stream;
            }
            return null;
        }

        private async Task<Stream> TryOpenRelative(StorageFolder root, string relativePath, int flags)
        {
            try
            {
                bool writing = (flags & (OFlags.WRONLY | OFlags.RDWR)) != 0;
                if (writing)
                {
                    var file = await root.CreateFileAsync(relativePath,
                        (flags & OFlags.CREAT) != 0
                            ? CreationCollisionOption.OpenIfExists
                            : CreationCollisionOption.OpenIfExists);
                    return await file.OpenStreamForWriteAsync();
                }
                else
                {
                    // Navigate subfolders for relative paths like "lib/armeabi/libfoo.so".
                    var parts = relativePath.Split(new[] {'/', '\\'}, StringSplitOptions.RemoveEmptyEntries);
                    StorageFolder folder = root;
                    for (int i = 0; i < parts.Length - 1; i++)
                        folder = await folder.GetFolderAsync(parts[i]);

                    var file = await folder.GetFileAsync(parts[parts.Length - 1]);
                    return await file.OpenStreamForReadAsync();
                }
            }
            catch
            {
                return null;
            }
        }

        private async Task<StorageFile> TryGetStorageFile(string path)
        {
            foreach (var kv in _mounts)
            {
                if (!path.StartsWith(kv.Key)) continue;
                string rel = path.Substring(kv.Key.Length).TrimStart('/');
                try
                {
                    return await kv.Value.GetFileAsync(rel);
                }
                catch { }
            }
            return null;
        }

        private static string BuildCpuInfo()
        {
            // Expose a minimal ARM Cortex-A53 cpuinfo for bionic compat.
            return "processor\t: 0\n"
                 + "model name\t: ARMv8 Processor rev 3 (v8l)\n"
                 + "BogoMIPS\t: 38.40\n"
                 + "Features\t: half thumb fastmult vfp edsp neon vfpv3 tls vfpv4 idiva idivt vfpd32 lpae evtstrm\n"
                 + "CPU implementer\t: 0x41\n"
                 + "CPU architecture: 8\n"
                 + "CPU variant\t: 0x0\n"
                 + "CPU part\t: 0xd03\n"
                 + "CPU revision\t: 3\n";
        }
    }

    // ── /dev/random emulation ────────────────────────────────────────────────

    /// <summary>
    /// Infinite stream of pseudo-random bytes – emulates /dev/urandom.
    /// Mirrors FLinux src/fs/random.c.
    /// </summary>
    internal class RandomDevStream : Stream
    {
        private static readonly Random _rng = new Random();

        public override bool CanRead  => true;
        public override bool CanSeek  => false;
        public override bool CanWrite => false;
        public override long Length   => long.MaxValue;
        public override long Position { get => 0; set { } }

        public override int Read(byte[] buffer, int offset, int count)
        {
            _rng.NextBytes(buffer);
            return count;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => 0;
        public override void SetLength(long value) { }
        public override void Write(byte[] buffer, int offset, int count) { }
    }
}
