// LinuxProcess - Guest process / thread state container.
// Converted from FLinux src/syscall/process.c + process_info.h.
// Holds file-descriptor table, signal disposition, PID, and related state
// needed by the syscall dispatcher and CPU interpreters.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace DalvikUWPCSharp.FLinux.Core
{
    /// <summary>
    /// Represents a single open file descriptor in the guest process.
    /// Mirrors the `filed` structure from FLinux vfs.c.
    /// </summary>
    public class GuestFileDescriptor
    {
        public int Fd       { get; }
        public Stream Stream { get; set; }
        public string Path  { get; set; }
        public int Flags    { get; set; }   // O_RDONLY / O_WRONLY / O_RDWR etc.
        public bool CloseOnExec { get; set; }
        public long Position { get => Stream?.Position ?? _position; set { if (Stream != null) Stream.Position = value; else _position = value; } }
        private long _position;

        public GuestFileDescriptor(int fd, Stream stream, string path, int flags = 0)
        {
            Fd     = fd;
            Stream = stream;
            Path   = path;
            Flags  = flags;
        }
    }

    /// <summary>
    /// Guest process state: PID, UID, file descriptors, signal handlers.
    /// Corresponds to process_data + process_info in FLinux process.h / process_info.h.
    /// </summary>
    public class LinuxProcess
    {
        // ── Identity ─────────────────────────────────────────────────────────

        /// <summary>Simulated Linux process ID (always looks like 1 for the first guest).</summary>
        public int Pid  { get; private set; } = 1;
        public int Ppid { get; private set; } = 0;
        /// <summary>All Android processes report as root (uid=0) per FLinux convention.</summary>
        public int Uid  { get; private set; } = 0;
        public int Gid  { get; private set; } = 0;
        public int Euid { get; private set; } = 0;
        public int Egid { get; private set; } = 0;

        /// <summary>Current working directory inside the guest filesystem.</summary>
        public string Cwd { get; set; } = "/data/data";

        // ── File descriptor table ─────────────────────────────────────────────
        // Mirrors FLinux vfs.c filed[MAX_FD_COUNT].

        private const int MAX_FD = 1024;
        private readonly GuestFileDescriptor[] _fds = new GuestFileDescriptor[MAX_FD];
        private int _nextFd = 3; // 0=stdin, 1=stdout, 2=stderr pre-opened.

        // ── Signal disposition table ──────────────────────────────────────────
        // Mirrors FLinux sig.c sigaction table: signal → handler address (0 = SIG_DFL).

        private readonly ulong[] _sigHandlers = new ulong[64]; // signals 1–63

        // ── Exit status ───────────────────────────────────────────────────────

        public bool Exited     { get; private set; }
        public int  ExitStatus { get; private set; }

        // ── Constructor ───────────────────────────────────────────────────────

        public LinuxProcess()
        {
            // Pre-open stdin / stdout / stderr as null streams.
            _fds[0] = new GuestFileDescriptor(0, Stream.Null, "/dev/stdin",  0);
            _fds[1] = new GuestFileDescriptor(1, Stream.Null, "/dev/stdout", 1);
            _fds[2] = new GuestFileDescriptor(2, Stream.Null, "/dev/stderr",  1);
            Debug.WriteLine($"[LinuxProcess] Created guest process PID={Pid}");
        }

        // ── File descriptors ─────────────────────────────────────────────────

        /// <summary>Open a new file descriptor. Returns fd number or -1 on failure.</summary>
        public int OpenFd(Stream stream, string path, int flags = 0)
        {
            int fd = AllocateFd();
            if (fd < 0) return -24; // EMFILE
            _fds[fd] = new GuestFileDescriptor(fd, stream, path, flags);
            Debug.WriteLine($"[LinuxProcess] open fd={fd} path={path}");
            return fd;
        }

        /// <summary>Close an open file descriptor.</summary>
        public int CloseFd(int fd)
        {
            if (fd < 0 || fd >= MAX_FD || _fds[fd] == null) return -9; // EBADF
            try { _fds[fd].Stream?.Dispose(); } catch { }
            _fds[fd] = null;
            return 0;
        }

        /// <summary>Retrieve a file descriptor entry (null if not open).</summary>
        public GuestFileDescriptor GetFd(int fd)
        {
            if (fd < 0 || fd >= MAX_FD) return null;
            return _fds[fd];
        }

        /// <summary>Duplicate fd. Returns new fd or negative errno.</summary>
        public int DupFd(int oldFd, int newFd = -1)
        {
            var src = GetFd(oldFd);
            if (src == null) return -9; // EBADF

            int targetFd = (newFd >= 0) ? newFd : AllocateFd();
            if (targetFd < 0) return -24; // EMFILE
            if (targetFd >= MAX_FD) return -9;

            if (_fds[targetFd] != null && targetFd != oldFd)
                CloseFd(targetFd);

            _fds[targetFd] = new GuestFileDescriptor(targetFd, src.Stream, src.Path, src.Flags);
            return targetFd;
        }

        private int AllocateFd()
        {
            for (int i = _nextFd; i < MAX_FD; i++)
            {
                if (_fds[i] == null) { _nextFd = i + 1; return i; }
            }
            return -1;
        }

        // ── Signal handling ───────────────────────────────────────────────────

        /// <summary>Set the handler address for a signal. 0 = SIG_DFL, 1 = SIG_IGN.</summary>
        public void SetSigHandler(int sig, ulong handlerAddr)
        {
            if (sig >= 1 && sig < _sigHandlers.Length)
                _sigHandlers[sig] = handlerAddr;
        }

        public ulong GetSigHandler(int sig)
        {
            if (sig >= 1 && sig < _sigHandlers.Length)
                return _sigHandlers[sig];
            return 0;
        }

        // ── Exit ──────────────────────────────────────────────────────────────

        public void Exit(int status)
        {
            Exited     = true;
            ExitStatus = status;
            Debug.WriteLine($"[LinuxProcess] exit status={status}");
        }

        // ── Resource limits ───────────────────────────────────────────────────
        // Stub: mirrors FLinux getrlimit / setrlimit.

        public void GetRlimit(int resource, out ulong softLimit, out ulong hardLimit)
        {
            softLimit = 0xFFFFFFFFUL;
            hardLimit = 0xFFFFFFFFUL;
        }
    }
}
