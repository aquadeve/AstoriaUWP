// SyscallDispatcher - Linux syscall routing and managed handler implementations.
// Converts FLinux src/syscall/ layer (mm.c, vfs.c, process.c, sig.c, futex.c)
// into a managed C# class that the CPU interpreters can call directly.
//
// When a CPU interpreter encounters a SVC / SYSCALL instruction it calls
// SyscallDispatcher.Dispatch(sysno, cpu, process).  The dispatcher maps the
// syscall number (already normalised to x64 numbering space, or looked up in
// per-ABI tables) to one of the private handler methods below.
//
// Handlers return the Linux errno convention:
//   >=  0  success (may be a value like a fd, byte count, etc.)
//    < -1  negative errno value (e.g. -2 = ENOENT, -9 = EBADF)

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DalvikUWPCSharp.FLinux.Syscalls
{
    using DalvikUWPCSharp.FLinux.Core;
    using DalvikUWPCSharp.FLinux.Cpu;

    /// <summary>
    /// Routes Linux syscall numbers to their managed C# implementations.
    /// Mirrors FLinux syscall_dispatch.c + individual syscall/mm.c, vfs.c, etc.
    /// </summary>
    public class SyscallDispatcher
    {
        private readonly LinuxMemory  _memory;
        private readonly LinuxVfs     _vfs;
        private readonly ExecutionMode _abi;  // determines which numbering table to use

        // Simulated PID counter.
        private static int _nextPid = 2;

        public SyscallDispatcher(LinuxMemory memory, LinuxVfs vfs, ExecutionMode abi)
        {
            _memory = memory;
            _vfs    = vfs;
            _abi    = abi;
        }

        // ── Public entry point ────────────────────────────────────────────────

        /// <summary>
        /// Dispatch a syscall.  <paramref name="sysno"/> is the raw number as
        /// placed in the appropriate register by the guest binary (R7 for ARM32,
        /// X8 for ARM64, RAX for x64).
        /// Returns the Linux syscall return value.
        /// </summary>
        public long Dispatch(int sysno, ICpuInterpreter cpu, LinuxProcess proc)
        {
            // Normalise syscall number to a canonical name so we can share handlers.
            string name = ResolveName(sysno);
            Debug.WriteLine($"[Syscall] {_abi} #{sysno} ({name}) PC=0x{cpu.PC:X}");

            // Retrieve argument registers according to ABI.
            ulong a0, a1, a2, a3, a4, a5;
            GetArgs(cpu, out a0, out a1, out a2, out a3, out a4, out a5);

            long result = DispatchByName(name, a0, a1, a2, a3, a4, a5, cpu, proc);
            Debug.WriteLine($"[Syscall] {name} → {result}");
            return result;
        }

        // ── Argument extraction per ABI ───────────────────────────────────────

        private void GetArgs(ICpuInterpreter cpu, out ulong a0, out ulong a1,
                             out ulong a2, out ulong a3, out ulong a4, out ulong a5)
        {
            switch (_abi)
            {
                case ExecutionMode.Arm32:
                    // R0–R5 are argument registers.
                    a0 = cpu.GetRegister(0); a1 = cpu.GetRegister(1);
                    a2 = cpu.GetRegister(2); a3 = cpu.GetRegister(3);
                    a4 = cpu.GetRegister(4); a5 = cpu.GetRegister(5);
                    break;
                case ExecutionMode.Arm64:
                    // X0–X5 are argument registers.
                    a0 = cpu.GetRegister(0); a1 = cpu.GetRegister(1);
                    a2 = cpu.GetRegister(2); a3 = cpu.GetRegister(3);
                    a4 = cpu.GetRegister(4); a5 = cpu.GetRegister(5);
                    break;
                default: // x64: RDI, RSI, RDX, R10, R8, R9
                    a0 = cpu.GetRegister(7);  // RDI
                    a1 = cpu.GetRegister(6);  // RSI
                    a2 = cpu.GetRegister(2);  // RDX
                    a3 = cpu.GetRegister(10); // R10
                    a4 = cpu.GetRegister(8);  // R8
                    a5 = cpu.GetRegister(9);  // R9
                    break;
            }
        }

        // ── Syscall number → canonical name ─────────────────────────────────

        private string ResolveName(int sysno)
        {
            switch (_abi)
            {
                case ExecutionMode.Arm32:  return ResolveArm32(sysno);
                case ExecutionMode.Arm64:  return ResolveArm64(sysno);
                default:                   return ResolveX64(sysno);
            }
        }

        private static string ResolveArm32(int n)
        {
            switch (n)
            {
                case Arm32Syscalls.exit:          return "exit";
                case Arm32Syscalls.exit_group:    return "exit_group";
                case Arm32Syscalls.read:          return "read";
                case Arm32Syscalls.write:         return "write";
                case Arm32Syscalls.open:          return "open";
                case Arm32Syscalls.close:         return "close";
                case Arm32Syscalls.lseek:         return "lseek";
                case Arm32Syscalls.brk:           return "brk";
                case Arm32Syscalls.mmap:          return "mmap";
                case Arm32Syscalls.mmap2:         return "mmap2";
                case Arm32Syscalls.mprotect:      return "mprotect";
                case Arm32Syscalls.munmap:        return "munmap";
                case Arm32Syscalls.getpid:        return "getpid";
                case Arm32Syscalls.getuid:        return "getuid";
                case Arm32Syscalls.geteuid:       return "geteuid";
                case Arm32Syscalls.getgid:        return "getgid";
                case Arm32Syscalls.getegid:       return "getegid";
                case Arm32Syscalls.gettid:        return "gettid";
                case Arm32Syscalls.getppid:       return "getppid";
                case Arm32Syscalls.prctl:         return "prctl";
                case Arm32Syscalls.uname:         return "uname";
                case Arm32Syscalls.nanosleep:     return "nanosleep";
                case Arm32Syscalls.sched_yield:   return "sched_yield";
                case Arm32Syscalls.clock_gettime: return "clock_gettime";
                case Arm32Syscalls.gettimeofday:  return "gettimeofday";
                case Arm32Syscalls.futex:         return "futex";
                case Arm32Syscalls.set_tid_address:return "set_tid_address";
                case Arm32Syscalls.rt_sigaction:  return "rt_sigaction";
                case Arm32Syscalls.rt_sigprocmask:return "rt_sigprocmask";
                case Arm32Syscalls.getcwd:        return "getcwd";
                case Arm32Syscalls.openat:        return "openat";
                case Arm32Syscalls.fcntl:         return "fcntl";
                case Arm32Syscalls.dup:           return "dup";
                case Arm32Syscalls.dup2:          return "dup2";
                case Arm32Syscalls.pipe:          return "pipe";
                case Arm32Syscalls.stat64:        return "stat";
                case Arm32Syscalls.fstat64:       return "fstat";
                case Arm32Syscalls.lstat64:       return "lstat";
                case Arm32Syscalls.getrlimit:     return "getrlimit";
                case Arm32Syscalls.madvise:       return "madvise";
                case Arm32Syscalls.sysinfo:       return "sysinfo";
                // Additional from ExAndroidNativeEmu
                case Arm32Syscalls.fork:          return "fork";
                case Arm32Syscalls.vfork:         return "vfork";
                case Arm32Syscalls.execve:        return "execve";
                case Arm32Syscalls.ptrace:        return "ptrace";
                case Arm32Syscalls.kill:          return "kill";
                case Arm32Syscalls.sigaction:     return "sigaction";
                case Arm32Syscalls.sigaltstack:   return "sigaltstack";
                case Arm32Syscalls.wait4:         return "wait4";
                case Arm32Syscalls.clone:         return "clone";
                case Arm32Syscalls.getuid32:      return "getuid";  // alias
                case Arm32Syscalls.tgkill:        return "tgkill";
                case Arm32Syscalls.tkill:         return "tkill";
                case Arm32Syscalls.socket:        return "socket";
                case Arm32Syscalls.bind:          return "bind";
                case Arm32Syscalls.connect:       return "connect";
                case Arm32Syscalls.setsockopt:    return "setsockopt";
                case Arm32Syscalls.getcpu:        return "getcpu";
                case Arm32Syscalls.dup3_arm32:    return "dup3";
                case Arm32Syscalls.process_vm_readv: return "process_vm_readv";
                case Arm32Syscalls.getrandom:     return "getrandom";
                case Arm32Syscalls.ARM_cacheflush: return "ARM_cacheflush";
                case Arm32Syscalls.ARM_set_tls:   return "ARM_set_tls";
                // VFS syscalls from ExAndroidNativeEmu
                case Arm32Syscalls.access:        return "access";
                case Arm32Syscalls.unlink:        return "unlink";
                case Arm32Syscalls.ioctl:         return "ioctl";
                case Arm32Syscalls.writev:        return "writev";
                case Arm32Syscalls.poll:          return "poll";
                case Arm32Syscalls.getdents64:    return "getdents64";
                case Arm32Syscalls.llseek:        return "llseek";
                case Arm32Syscalls.fcntl64:       return "fcntl64";
                case Arm32Syscalls.statfs64:      return "statfs64";
                case Arm32Syscalls.mkdirat:       return "mkdirat";
                case Arm32Syscalls.fstatat64:     return "fstatat";
                case Arm32Syscalls.unlinkat:      return "unlinkat";
                case Arm32Syscalls.readlinkat:    return "readlinkat";
                case Arm32Syscalls.ppoll:         return "ppoll";
                case Arm32Syscalls.faccessat:     return "faccessat";
                default: return $"unknown_{n}";
            }
        }

        private static string ResolveArm64(int n)
        {
            switch (n)
            {
                case Arm64Syscalls.exit:          return "exit";
                case Arm64Syscalls.exit_group:    return "exit_group";
                case Arm64Syscalls.read:          return "read";
                case Arm64Syscalls.write:         return "write";
                case Arm64Syscalls.openat:        return "openat";
                case Arm64Syscalls.close:         return "close";
                case Arm64Syscalls.lseek:         return "lseek";
                case Arm64Syscalls.brk:           return "brk";
                case Arm64Syscalls.mmap:          return "mmap";
                case Arm64Syscalls.mprotect:      return "mprotect";
                case Arm64Syscalls.munmap:        return "munmap";
                case Arm64Syscalls.getpid:        return "getpid";
                case Arm64Syscalls.getuid:        return "getuid";
                case Arm64Syscalls.geteuid:       return "geteuid";
                case Arm64Syscalls.getgid:        return "getgid";
                case Arm64Syscalls.getegid:       return "getegid";
                case Arm64Syscalls.gettid:        return "gettid";
                case Arm64Syscalls.getppid:       return "getppid";
                case Arm64Syscalls.prctl:         return "prctl";
                case Arm64Syscalls.uname:         return "uname";
                case Arm64Syscalls.nanosleep:     return "nanosleep";
                case Arm64Syscalls.sched_yield:   return "sched_yield";
                case Arm64Syscalls.clock_gettime: return "clock_gettime";
                case Arm64Syscalls.gettimeofday:  return "gettimeofday";
                case Arm64Syscalls.futex:         return "futex";
                case Arm64Syscalls.set_tid_address:return "set_tid_address";
                case Arm64Syscalls.rt_sigaction:  return "rt_sigaction";
                case Arm64Syscalls.rt_sigprocmask:return "rt_sigprocmask";
                case Arm64Syscalls.getcwd:        return "getcwd";
                case Arm64Syscalls.fcntl:         return "fcntl";
                case Arm64Syscalls.dup:           return "dup";
                case Arm64Syscalls.fstat:         return "fstat";
                case Arm64Syscalls.newfstatat:    return "stat";
                case Arm64Syscalls.getrlimit:     return "getrlimit";
                case Arm64Syscalls.madvise:       return "madvise";
                case Arm64Syscalls.sysinfo:       return "sysinfo";
                // Additional from ExAndroidNativeEmu
                case Arm64Syscalls.execve:        return "execve";
                case Arm64Syscalls.ptrace:        return "ptrace";
                case Arm64Syscalls.kill:          return "kill";
                case Arm64Syscalls.sigaltstack:   return "sigaltstack";
                case Arm64Syscalls.wait4:         return "wait4";
                case Arm64Syscalls.clone:         return "clone";
                case Arm64Syscalls.tgkill:        return "tgkill";
                case Arm64Syscalls.tkill:         return "tkill";
                case Arm64Syscalls.socket:        return "socket";
                case Arm64Syscalls.bind:          return "bind";
                case Arm64Syscalls.connect:       return "connect";
                case Arm64Syscalls.setsockopt:    return "setsockopt";
                case Arm64Syscalls.getcpu:        return "getcpu";
                case Arm64Syscalls.process_vm_readv: return "process_vm_readv";
                case Arm64Syscalls.getrandom:     return "getrandom";
                // VFS syscalls from ExAndroidNativeEmu (ARM64)
                case Arm64Syscalls.ioctl:         return "ioctl";
                case Arm64Syscalls.writev:        return "writev";
                case Arm64Syscalls.getdents64:    return "getdents64";
                case Arm64Syscalls.statfs:        return "statfs";
                case Arm64Syscalls.mkdirat:       return "mkdirat";
                case Arm64Syscalls.unlinkat:      return "unlinkat";
                case Arm64Syscalls.readlinkat:    return "readlinkat";
                case Arm64Syscalls.faccessat:     return "faccessat";
                case Arm64Syscalls.ppoll:         return "ppoll";
                default: return $"unknown_{n}";
            }
        }

        private static string ResolveX64(int n)
        {
            switch (n)
            {
                case X64Syscalls.read:            return "read";
                case X64Syscalls.write:           return "write";
                case X64Syscalls.open:            return "open";
                case X64Syscalls.close:           return "close";
                case X64Syscalls.fstat:           return "fstat";
                case X64Syscalls.stat:            return "stat";
                case X64Syscalls.lstat:           return "lstat";
                case X64Syscalls.lseek:           return "lseek";
                case X64Syscalls.mmap:            return "mmap";
                case X64Syscalls.mprotect:        return "mprotect";
                case X64Syscalls.munmap:          return "munmap";
                case X64Syscalls.brk:             return "brk";
                case X64Syscalls.exit_group:      return "exit_group";
                case X64Syscalls.exit_:           return "exit";
                case X64Syscalls.getpid:          return "getpid";
                case X64Syscalls.getuid:          return "getuid";
                case X64Syscalls.geteuid:         return "geteuid";
                case X64Syscalls.getgid:          return "getgid";
                case X64Syscalls.getegid:         return "getegid";
                case X64Syscalls.gettid:          return "gettid";
                case X64Syscalls.getppid:         return "getppid";
                case X64Syscalls.prctl:           return "prctl";
                case X64Syscalls.uname:           return "uname";
                case X64Syscalls.nanosleep:       return "nanosleep";
                case X64Syscalls.sched_yield:     return "sched_yield";
                case X64Syscalls.clock_gettime:   return "clock_gettime";
                case X64Syscalls.gettimeofday:    return "gettimeofday";
                case X64Syscalls.futex:           return "futex";
                case X64Syscalls.set_tid_address: return "set_tid_address";
                case X64Syscalls.rt_sigaction:    return "rt_sigaction";
                case X64Syscalls.rt_sigprocmask:  return "rt_sigprocmask";
                case X64Syscalls.getcwd:          return "getcwd";
                case X64Syscalls.openat:          return "openat";
                case X64Syscalls.fcntl:           return "fcntl";
                case X64Syscalls.dup:             return "dup";
                case X64Syscalls.dup2:            return "dup2";
                case X64Syscalls.pipe:            return "pipe";
                case X64Syscalls.getrlimit:       return "getrlimit";
                case X64Syscalls.madvise:         return "madvise";
                case X64Syscalls.sysinfo:         return "sysinfo";
                case X64Syscalls.arch_prctl:      return "arch_prctl";
                // Additional from ExAndroidNativeEmu
                case X64Syscalls.ptrace:          return "ptrace";
                case X64Syscalls.kill:            return "kill";
                case X64Syscalls.tgkill:          return "tgkill";
                case X64Syscalls.tkill:           return "tkill";
                case X64Syscalls.sigaltstack:     return "sigaltstack";
                case X64Syscalls.wait4:           return "wait4";
                case X64Syscalls.clone:           return "clone";
                case X64Syscalls.execve:          return "execve";
                case X64Syscalls.socket:          return "socket";
                case X64Syscalls.bind:            return "bind";
                case X64Syscalls.connect:         return "connect";
                case X64Syscalls.setsockopt:      return "setsockopt";
                case X64Syscalls.getcpu:          return "getcpu";
                case X64Syscalls.process_vm_readv: return "process_vm_readv";
                case X64Syscalls.getrandom:       return "getrandom";
                // VFS syscalls
                case X64Syscalls.access:          return "access";
                case X64Syscalls.ioctl:           return "ioctl";
                case X64Syscalls.writev:          return "writev";
                case X64Syscalls.poll:            return "poll";
                case X64Syscalls.getdents:        return "getdents64";
                case X64Syscalls.getdents64:      return "getdents64";
                case X64Syscalls.mkdirat:         return "mkdirat";
                case X64Syscalls.unlinkat:        return "unlinkat";
                case X64Syscalls.faccessat:       return "faccessat";
                case X64Syscalls.pipe2:           return "pipe2";
                default: return $"unknown_{n}";
            }
        }

        // ── Handler dispatch by canonical name ────────────────────────────────

        private long DispatchByName(string name,
                                    ulong a0, ulong a1, ulong a2, ulong a3, ulong a4, ulong a5,
                                    ICpuInterpreter cpu, LinuxProcess proc)
        {
            switch (name)
            {
                // ── Process / thread ──────────────────────────────────────────
                case "exit":
                case "exit_group":
                    proc.Exit((int)a0);
                    return 0;

                case "getpid":    return proc.Pid;
                case "getppid":   return proc.Ppid;
                case "getuid":    return proc.Uid;
                case "geteuid":   return proc.Euid;
                case "getgid":    return proc.Gid;
                case "getegid":   return proc.Egid;
                case "gettid":    return proc.Pid; // single-threaded: tid = pid

                case "set_tid_address":
                    // Store clear_child_tid pointer (ignore for now).
                    return proc.Pid;

                case "sched_yield":
                    return 0; // success, yield is a no-op in SW interpreter

                case "nanosleep":
                    // timespec at a0; ignore actual sleep in SW mode.
                    return 0;

                case "prctl":
                    return SysPrctl(a0, a1, a2, a3, a4, cpu);

                case "arch_prctl":
                    return SysArchPrctl(a0, a1, cpu);

                case "uname":
                    return SysUname(a0);

                case "sysinfo":
                    return SysSysinfo(a0);

                case "gettimeofday":
                    return SysGettimeofday(a0, a1);

                case "clock_gettime":
                    return SysClockGettime(a0, a1);

                // ── Signal handling ────────────────────────────────────────────
                case "rt_sigaction":
                    if (a1 != 0) proc.SetSigHandler((int)a0, a1);
                    return 0;

                case "rt_sigprocmask":
                    return 0; // stub: pretend to succeed

                case "kill":
                case "tkill":
                case "tgkill":
                    // For our single-process model, signals to ourselves are a no-op.
                    return 0;

                // ── Memory management ─────────────────────────────────────────
                case "brk":
                    return (long)_memory.Brk(a0);

                case "mmap":
                case "mmap2":
                {
                    ulong len   = a1;
                    var   prot  = (MemProt)(int)a2;
                    var   flags = (MapFlags)(int)a3;
                    int   fd    = (int)a4;
                    ulong off   = a5;
                    // mmap2: offset is in 4096-byte pages, not bytes.
                    if (name == "mmap2") off <<= 12;
                    // If fd >= 0 and MAP_ANONYMOUS is not set: read-file-into-memory stub.
                    return (long)_memory.Mmap(a0, len, prot, flags | MapFlags.Anonymous);
                }

                case "munmap":
                    return _memory.Munmap(a0, a1);

                case "mprotect":
                    return _memory.Mprotect(a0, a1, (MemProt)(int)a2);

                case "madvise":
                    return 0; // stub: madvise is advisory, safe to ignore

                case "mremap":
                {
                    // Simple mremap: allocate new region and copy.
                    ulong oldAddr = a0, oldLen = a1, newLen = a2;
                    ulong newAddr = _memory.Mmap(0, newLen, MemProt.Read | MemProt.Write,
                                                  MapFlags.Private | MapFlags.Anonymous);
                    byte[] data = _memory.ReadBytes(oldAddr, (int)Math.Min(oldLen, newLen));
                    _memory.WriteBytes(newAddr, data, 0, data.Length);
                    _memory.Munmap(oldAddr, oldLen);
                    return (long)newAddr;
                }

                case "getrlimit":
                {
                    proc.GetRlimit((int)a0, out ulong soft, out ulong hard);
                    _memory.WriteUInt64(a1, soft);
                    _memory.WriteUInt64(a1 + 8, hard);
                    return 0;
                }

                case "prlimit64":
                    proc.GetRlimit((int)a1, out ulong s2, out ulong h2);
                    if (a3 != 0) { _memory.WriteUInt64(a3, s2); _memory.WriteUInt64(a3 + 8, h2); }
                    return 0;

                // ── File I/O ───────────────────────────────────────────────────
                case "open":
                case "openat":
                    return SysOpenAsync(name == "openat" ? _memory.ReadString(a1) : _memory.ReadString(a0),
                                        (int)(name == "openat" ? a2 : a1), proc).GetAwaiter().GetResult();

                case "close":
                    return proc.CloseFd((int)a0);

                case "read":
                {
                    var gfd = proc.GetFd((int)a0);
                    if (gfd == null) return -9;
                    byte[] buf = new byte[(int)a2];
                    int n = _vfs.Read(gfd.Stream, buf, 0, buf.Length);
                    if (n > 0) _memory.WriteBytes(a1, buf, 0, n);
                    return n;
                }

                case "write":
                {
                    var gfd = proc.GetFd((int)a0);
                    if (gfd == null) return -9;
                    byte[] buf = _memory.ReadBytes(a1, (int)a2);
                    // For stdout/stderr route to Debug output.
                    if ((int)a0 == 1 || (int)a0 == 2)
                    {
                        Debug.WriteLine("[guest] " + Encoding.UTF8.GetString(buf));
                        return buf.Length;
                    }
                    return _vfs.Write(gfd.Stream, buf, 0, buf.Length);
                }

                case "lseek":
                {
                    var gfd = proc.GetFd((int)a0);
                    if (gfd?.Stream == null) return -9;
                    SeekOrigin origin = (SeekOrigin)(int)a2;
                    try { gfd.Stream.Seek((long)a1, origin); return gfd.Stream.Position; }
                    catch { return -22; }
                }

                case "stat":
                case "lstat":
                case "fstat":
                case "newfstatat":
                    return SysStatAsync(name, a0, a1, a2, a3, proc).GetAwaiter().GetResult();

                case "getcwd":
                {
                    string cwd = proc.Cwd;
                    _memory.WriteString(a0, cwd);
                    return cwd.Length + 1;
                }

                case "dup":
                    return proc.DupFd((int)a0);
                case "dup2":
                case "dup3":
                    return proc.DupFd((int)a0, (int)a1);

                case "fcntl":
                    return SysFcntl((int)a0, (int)a1, (int)a2, proc);

                case "pipe":
                case "pipe2":
                {
                    // Return stub pipe fds backed by a MemoryStream.
                    var pipeStream = new MemoryStream();
                    int rfd = proc.OpenFd(pipeStream, "[pipe:read]",  OFlags.RDONLY);
                    int wfd = proc.OpenFd(pipeStream, "[pipe:write]", OFlags.WRONLY);
                    _memory.WriteUInt32(a0,     (uint)rfd);
                    _memory.WriteUInt32(a0 + 4, (uint)wfd);
                    return 0;
                }

                // ── Additional VFS syscalls from ExAndroidNativeEmu ────────────
                case "unlink":
                case "unlinkat":
                {
                    string path = name == "unlinkat" ? _memory.ReadString(a1) : _memory.ReadString(a0);
                    Debug.WriteLine($"[Syscall] {name} path={path} – stub 0");
                    return 0; // pretend success; VFS doesn't track deletes in SW mode
                }

                case "access":
                case "faccessat":
                {
                    string path = name == "faccessat" ? _memory.ReadString(a1) : _memory.ReadString(a0);
                    Debug.WriteLine($"[Syscall] {name} path={path}");
                    // Check if file exists in VFS.
                    var stat = _vfs.Stat(path).GetAwaiter().GetResult();
                    return stat != null ? 0 : -2; // 0=success, ENOENT
                }

                case "ioctl":
                    Debug.WriteLine($"[Syscall] ioctl fd={(int)a0} cmd=0x{a1:X} – stub 0");
                    return 0;

                case "writev":
                {
                    // Gather-write: write multiple buffers to a single fd.
                    var gfd = proc.GetFd((int)a0);
                    if (gfd == null) return -9; // EBADF
                    int ptrSz   = _abi == ExecutionMode.Arm64 ? 8 : 4;
                    int iovcnt  = (int)a2;
                    ulong iovBase = a1;
                    long total  = 0;
                    for (int i = 0; i < iovcnt; i++)
                    {
                        ulong base_ = ptrSz == 8 ? _memory.ReadUInt64(iovBase) : _memory.ReadUInt32(iovBase);
                        ulong len_  = ptrSz == 8 ? _memory.ReadUInt64(iovBase + 8) : _memory.ReadUInt32(iovBase + 4);
                        iovBase += (ulong)(ptrSz * 2);
                        if (len_ == 0) continue;
                        byte[] chunk = _memory.ReadBytes(base_, (int)len_);
                        if ((int)a0 <= 2)
                            Debug.WriteLine("[guest] " + Encoding.UTF8.GetString(chunk));
                        else
                            _vfs.Write(gfd.Stream, chunk, 0, chunk.Length);
                        total += (long)len_;
                    }
                    return total;
                }

                case "poll":
                case "ppoll":
                    // Polling is a no-op in SW mode: report all fds as readable.
                    return (int)a1; // nfds

                case "getdents64":
                    // Return 0 = no entries; full directory listing not implemented.
                    return 0;

                case "llseek":
                {
                    // ARM32 _llseek: fd=a0, offset_high=a1, offset_low=a2, result_ptr=a3, whence=a4
                    var gfd = proc.GetFd((int)a0);
                    if (gfd?.Stream == null) return -9;
                    long off = ((long)(uint)a1 << 32) | (uint)a2;
                    try
                    {
                        long pos = gfd.Stream.Seek(off, (SeekOrigin)(int)a4);
                        _memory.WriteUInt64(a3, (ulong)pos);
                        return 0;
                    }
                    catch { return -22; }
                }

                case "fcntl64":
                    return SysFcntl((int)a0, (int)a1, (int)a2, proc);

                case "statfs":
                case "statfs64":
                {
                    // Fill a simplified statfs struct: report a generous ext4-like filesystem.
                    ulong addr = a1;
                    _memory.WriteUInt64(addr, 0xEF53);   addr += 8; // f_type (EXT4_SUPER_MAGIC)
                    _memory.WriteUInt64(addr, 4096);      addr += 8; // f_bsize
                    _memory.WriteUInt64(addr, 1000000);   addr += 8; // f_blocks
                    _memory.WriteUInt64(addr, 500000);    addr += 8; // f_bfree
                    _memory.WriteUInt64(addr, 500000);    addr += 8; // f_bavail
                    _memory.WriteUInt64(addr, 100000);    addr += 8; // f_files
                    _memory.WriteUInt64(addr, 100000);    addr += 8; // f_ffree
                    return 0;
                }

                case "mkdirat":
                {
                    string dirPath = _memory.ReadString(a1);
                    Debug.WriteLine($"[Syscall] mkdirat dirfd={(int)a0} path={dirPath} – stub 0");
                    return 0;
                }

                case "fstatat":
                    return SysStatAsync("fstat", a0, a2, a3, 0, proc).GetAwaiter().GetResult();

                case "readlinkat":
                {
                    string path = _memory.ReadString(a1);
                    Debug.WriteLine($"[Syscall] readlinkat path={path} – stub EINVAL");
                    return -22; // EINVAL: not a symlink
                }

                // ── Futex ──────────────────────────────────────────────────────
                case "futex":
                    return SysFutex(a0, (int)a1, (int)a2, a3, a4);

                // ── Process / fork / clone ─────────────────────────────────────
                // In single-process SW emulation mode we stub these out.
                case "fork":
                case "vfork":
                    // Return 0: we are always the child (no real fork).
                    Debug.WriteLine($"[Syscall] {name} – stub returning 0 (child path)");
                    return 0;

                case "execve":
                {
                    string exe = _memory.ReadString(a0);
                    Debug.WriteLine($"[Syscall] execve {exe} – stub ENOSYS");
                    return -38; // ENOSYS
                }

                case "clone":
                    // Stub: return 0 = child; store TLS if CLONE_SETTLS requested.
                    return SysClone(a0, a1, a2, a3, a4, proc);

                case "wait4":
                    // No real children in SW mode; return 0 immediately.
                    return 0;

                // ── Signal helpers ─────────────────────────────────────────────
                case "sigaction":
                    // Old-style sigaction: same semantics as rt_sigaction.
                    if (a1 != 0) proc.SetSigHandler((int)a0, a1);
                    return 0;

                case "sigaltstack":
                    // Stub: alternate signal stack configuration; safe to ignore.
                    return 0;

                // ── Misc process ───────────────────────────────────────────────
                case "ptrace":
                    // Anti-debug stub: always pretend we are not being traced.
                    Debug.WriteLine($"[Syscall] ptrace request={(int)a0} – stub 0");
                    return 0;

                case "tgkill":
                case "tkill":
                {
                    int sig = (int)a2;
                    // SIGABRT (6): treat as process abort.
                    if (sig == 6)
                        throw new Exception($"[Syscall] {name}: SIGABRT sent – guest aborted");
                    return 0;
                }

                // ── Sockets (stub – no real networking in SW mode) ─────────────
                case "socket":
                    Debug.WriteLine($"[Syscall] socket family={(int)a0} type={(int)a1} – stub -ENOSYS");
                    return -38; // ENOSYS

                case "bind":
                case "connect":
                    return -111; // ECONNREFUSED

                case "setsockopt":
                    return 0; // success stub

                // ── CPU / system helpers ───────────────────────────────────────
                case "getcpu":
                    // Write cpu=1 to first pointer if non-null.
                    if (a0 != 0) _memory.WriteUInt32(a0, 1);
                    if (a1 != 0) _memory.WriteUInt32(a1, 0); // node=0
                    return 0;

                case "getrandom":
                    return SysGetrandom(a0, (int)a1, (int)a2);

                case "process_vm_readv":
                    return SysProcessVmReadv(a0, a1, (int)a2, a3, (int)a4, proc);

                // ── ARM-specific ──────────────────────────────────────────────
                case "ARM_cacheflush":
                    // Cache flush is a no-op in SW interpretation.
                    return 0;

                case "ARM_set_tls":
                    // Store TLS base; in SW mode we just log it.
                    Debug.WriteLine($"[Syscall] ARM_set_tls 0x{a0:X}");
                    proc.TlsBase = a0;
                    return 0;

                // ── Unknown ────────────────────────────────────────────────────
                default:
                    Debug.WriteLine($"[Syscall] UNIMPLEMENTED: {name} (a0=0x{a0:X})");
                    return -38; // ENOSYS
            }
        }

        // ── Individual syscall implementations ────────────────────────────────

        private long SysFutex(ulong uaddr, int op, int val, ulong timeoutAddr, ulong uaddr2)
        {
            const int FUTEX_WAIT      = 0;
            const int FUTEX_WAKE      = 1;
            const int FUTEX_PRIVATE   = 128;
            const int FUTEX_WAIT_BITSET = 9;
            const int FUTEX_WAKE_BITSET = 10;
            const int CMD_MASK        = ~(FUTEX_PRIVATE | 256); // strip FUTEX_PRIVATE_FLAG | FUTEX_CLOCK_REALTIME

            int cmd = op & CMD_MASK;

            if (cmd == FUTEX_WAIT || cmd == FUTEX_WAIT_BITSET)
            {
                // Read current value; if it differs from val return EAGAIN.
                uint current = _memory.ReadUInt32(uaddr);
                if (current != (uint)val)
                    return -11; // EAGAIN
                // In single-threaded SW mode we can't actually block; return 0 immediately.
                return 0;
            }
            if (cmd == FUTEX_WAKE || cmd == FUTEX_WAKE_BITSET)
                return 0; // no waiters to wake in single-thread mode

            return 0; // all other futex ops: stub success
        }

        private long SysClone(ulong flags, ulong childStack, ulong parentTid, ulong newTls,
                               ulong childTid, LinuxProcess proc)
        {
            const ulong CLONE_THREAD    = 0x00010000;
            const ulong CLONE_SETTLS    = 0x00080000;

            Debug.WriteLine($"[Syscall] clone flags=0x{flags:X} childStack=0x{childStack:X}");

            // If CLONE_THREAD is not set it's a fork; stub returns child=0 (child path).
            if ((flags & CLONE_THREAD) == 0)
                return 0;

            // Thread clone: store TLS base if CLONE_SETTLS requested.
            if ((flags & CLONE_SETTLS) != 0 && newTls != 0)
            {
                Debug.WriteLine($"[Syscall] clone CLONE_SETTLS tls=0x{newTls:X}");
                proc.TlsBase = newTls;
            }

            // In single-threaded SW mode: return fake child tid.
            return 1; // child thread tid stub
        }

        private long SysGetrandom(ulong buf, int count, int flags)
        {
            if (buf == 0 || count <= 0) return -22; // EINVAL
            var rng = new Random();
            var bytes = new byte[count];
            rng.NextBytes(bytes);
            _memory.WriteBytes(buf, bytes, 0, bytes.Length);
            return count;
        }

        private long SysProcessVmReadv(ulong pid, ulong localIov, int liovcnt,
                                        ulong remoteIov, int riovcnt, LinuxProcess proc)
        {
            // Only allow reading from ourselves.
            if ((long)pid != proc.Pid)
            {
                Debug.WriteLine($"[Syscall] process_vm_readv: cross-process not supported");
                return -1; // EPERM
            }

            long totalRead = 0;
            int ptrSz = _abi == ExecutionMode.Arm64 ? 8 : 4;
            ulong remoteOff = remoteIov;
            // Collect remote data.
            var collected = new System.Collections.Generic.List<byte[]>();
            for (int i = 0; i < riovcnt; i++)
            {
                ulong rbase = ptrSz == 8 ? _memory.ReadUInt64(remoteOff) : _memory.ReadUInt32(remoteOff);
                ulong rlen  = ptrSz == 8 ? _memory.ReadUInt64(remoteOff + 8) : _memory.ReadUInt32(remoteOff + 4);
                collected.Add(_memory.ReadBytes(rbase, (int)rlen));
                remoteOff += (ulong)(ptrSz * 2);
            }
            // Flatten.
            var flat = new System.Collections.Generic.List<byte>();
            foreach (var c in collected) flat.AddRange(c);
            byte[] src = flat.ToArray();

            // Write into local iovecs.
            int srcOff = 0;
            ulong localOff = localIov;
            for (int i = 0; i < liovcnt && srcOff < src.Length; i++)
            {
                ulong lbase = ptrSz == 8 ? _memory.ReadUInt64(localOff) : _memory.ReadUInt32(localOff);
                ulong llen  = ptrSz == 8 ? _memory.ReadUInt64(localOff + 8) : _memory.ReadUInt32(localOff + 4);
                int toCopy  = (int)Math.Min(llen, (ulong)(src.Length - srcOff));
                _memory.WriteBytes(lbase, src, srcOff, toCopy);
                srcOff    += toCopy;
                totalRead += toCopy;
                localOff  += (ulong)(ptrSz * 2);
            }
            return totalRead;
        }

        private async Task<long> SysOpenAsync(string path, int flags, LinuxProcess proc)
        {
            var stream = await _vfs.Open(path, flags);
            if (stream == null) return -2; // ENOENT
            return proc.OpenFd(stream, path, flags);
        }

        private async Task<long> SysStatAsync(string sysname, ulong a0, ulong a1, ulong a2, ulong a3,
                                              LinuxProcess proc)
        {
            string path;
            if (sysname == "fstat")
            {
                var gfd = proc.GetFd((int)a0);
                path = gfd?.Path ?? "";
            }
            else path = _memory.ReadString(a0);

            var stat = await _vfs.Stat(path);
            if (stat == null) return -2; // ENOENT

            // Write a simplified stat64 struct at a1 (64-bit fields).
            ulong addr = a1;
            _memory.WriteUInt64(addr,       stat.Dev);        addr += 8;
            _memory.WriteUInt64(addr,       stat.Ino);        addr += 8;
            _memory.WriteUInt32(addr, stat.Mode);       addr += 4;
            _memory.WriteUInt32(addr, stat.Nlink);      addr += 4;
            _memory.WriteUInt32(addr, (uint)stat.Uid);  addr += 4;
            _memory.WriteUInt32(addr, (uint)stat.Gid);  addr += 4;
            _memory.WriteUInt64(addr,       stat.RDev);       addr += 8;
            _memory.WriteUInt64(addr,       (ulong)stat.Size);addr += 8;
            _memory.WriteUInt64(addr,       4096);             addr += 8; // blksize
            _memory.WriteUInt64(addr,       (ulong)stat.Blocks); addr += 8;
            _memory.WriteUInt64(addr,       (ulong)stat.AtimeSec); addr += 8;
            _memory.WriteUInt64(addr,       0);                addr += 8; // atimensec
            _memory.WriteUInt64(addr,       (ulong)stat.MtimeSec); addr += 8;
            _memory.WriteUInt64(addr,       0);                addr += 8; // mtimensec
            _memory.WriteUInt64(addr,       (ulong)stat.CtimeSec); addr += 8;
            return 0;
        }

        private long SysPrctl(ulong option, ulong a1, ulong a2, ulong a3, ulong a4,
                               ICpuInterpreter cpu)
        {
            // PR_SET_NAME = 15
            if (option == 15)
            {
                string name = _memory.ReadString(a1);
                Debug.WriteLine($"[Syscall] prctl PR_SET_NAME \"{name}\"");
                return 0;
            }
            // PR_GET_NAME = 16
            if (option == 16)
            {
                _memory.WriteString(a1, "app_process");
                return 0;
            }
            return 0;
        }

        private long SysArchPrctl(ulong code, ulong addr, ICpuInterpreter cpu)
        {
            // ARCH_SET_FS = 0x1002
            if (code == 0x1002)
            {
                // Store FS base in a cpu-local way (simplified: just log).
                Debug.WriteLine($"[Syscall] arch_prctl ARCH_SET_FS 0x{addr:X}");
                return 0;
            }
            // ARCH_GET_FS = 0x1003
            if (code == 0x1003)
            {
                _memory.WriteUInt64(addr, 0);
                return 0;
            }
            return -22; // EINVAL
        }

        private long SysUname(ulong buf)
        {
            if (buf == 0) return -14; // EFAULT
            // struct utsname: 6 × 65-byte fields.
            WriteUtsField(buf + 0,   "Linux");
            WriteUtsField(buf + 65,  "localhost");
            WriteUtsField(buf + 130, "3.18.0-astoria");
            WriteUtsField(buf + 195, "#1 SMP");
            WriteUtsField(buf + 260, _abi == ExecutionMode.Arm32 ? "armv7l" :
                                     _abi == ExecutionMode.Arm64 ? "aarch64" : "x86_64");
            WriteUtsField(buf + 325, "(none)");
            return 0;
        }

        private void WriteUtsField(ulong addr, string val)
        {
            byte[] b = Encoding.ASCII.GetBytes(val);
            _memory.WriteBytes(addr, b, 0, Math.Min(b.Length, 64));
            _memory.WriteByte(addr + (ulong)Math.Min(b.Length, 64), 0);
        }

        private long SysSysinfo(ulong buf)
        {
            if (buf == 0) return -14;
            ulong addr = buf;
            // Emit struct sysinfo matching ARM32 (4-byte fields) or ARM64/x64 (8-byte fields).
            // See ExAndroidNativeEmu syscall_hooks.py __sysinfo for reference layout.
            if (_abi == ExecutionMode.Arm32)
            {
                // ARM32: all kernel_long_t fields are 4 bytes.
                _memory.WriteUInt32(addr, 0);               addr += 4; // uptime
                _memory.WriteUInt32(addr, 503328);          addr += 4; // loads[0]
                _memory.WriteUInt32(addr, 504576);          addr += 4; // loads[1]
                _memory.WriteUInt32(addr, 537280);          addr += 4; // loads[2]
                _memory.WriteUInt32(addr, 1945137152);      addr += 4; // totalram
                _memory.WriteUInt32(addr, 47845376);        addr += 4; // freeram
                _memory.WriteUInt32(addr, 0);               addr += 4; // sharedram
                _memory.WriteUInt32(addr, 169373696);       addr += 4; // bufferram
                _memory.WriteUInt32(addr, 0);               addr += 4; // totalswap
                _memory.WriteUInt32(addr, 0);               addr += 4; // freeswap
                _memory.WriteUInt16(addr, 1);               addr += 2; // procs
                _memory.WriteUInt16(addr, 0);               addr += 2; // pad
                _memory.WriteUInt32(addr, 1185939456);      addr += 4; // totalhigh
                _memory.WriteUInt32(addr, 1863680);         addr += 4; // freehigh
                _memory.WriteUInt32(addr, 1);                          // mem_unit
            }
            else
            {
                // ARM64 / x64: all kernel_long_t fields are 8 bytes.
                _memory.WriteUInt64(addr, 0);                     addr += 8; // uptime
                _memory.WriteUInt64(addr, 503328);                addr += 8; // loads[0]
                _memory.WriteUInt64(addr, 504576);                addr += 8; // loads[1]
                _memory.WriteUInt64(addr, 537280);                addr += 8; // loads[2]
                _memory.WriteUInt64(addr, 2UL * 1024 * 1024 * 1024); addr += 8; // totalram
                _memory.WriteUInt64(addr, 1UL * 1024 * 1024 * 1024); addr += 8; // freeram
                _memory.WriteUInt64(addr, 0);                     addr += 8; // sharedram
                _memory.WriteUInt64(addr, 169373696);             addr += 8; // bufferram
                _memory.WriteUInt64(addr, 0);                     addr += 8; // totalswap
                _memory.WriteUInt64(addr, 0);                     addr += 8; // freeswap
                _memory.WriteUInt16(addr, 1);                     addr += 2; // procs
                _memory.WriteUInt16(addr, 0);                     addr += 2; // pad
                _memory.WriteUInt32(addr, 0);                     addr += 4; // (alignment pad)
                _memory.WriteUInt64(addr, 0);                     addr += 8; // totalhigh
                _memory.WriteUInt64(addr, 0);                     addr += 8; // freehigh
                _memory.WriteUInt32(addr, 1);                              // mem_unit
            }
            return 0;
        }

        private long SysGettimeofday(ulong tvAddr, ulong tzAddr)
        {
            if (tvAddr != 0)
            {
                long unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                // Microseconds within the current second (ticks → microseconds).
                long usec = (DateTimeOffset.UtcNow.Ticks % TimeSpan.TicksPerSecond)
                            / (TimeSpan.TicksPerMillisecond / 1000);
                _memory.WriteUInt64(tvAddr,     (ulong)unixTime);
                _memory.WriteUInt64(tvAddr + 8, (ulong)usec);
            }
            return 0;
        }

        private long SysClockGettime(ulong clockId, ulong tsAddr)
        {
            if (tsAddr == 0) return -14;
            long totalMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _memory.WriteUInt64(tsAddr,     (ulong)(totalMs / 1000));
            _memory.WriteUInt64(tsAddr + 8, (ulong)(totalMs % 1000 * 1_000_000));
            return 0;
        }

        private long SysFcntl(int fd, int cmd, int arg, LinuxProcess proc)
        {
            var gfd = proc.GetFd(fd);
            if (gfd == null) return -9; // EBADF
            switch (cmd)
            {
                case 1:  return gfd.Flags;              // F_GETFD
                case 2:  gfd.Flags = arg; return 0;    // F_SETFD
                case 3:  return gfd.Flags;              // F_GETFL
                case 4:  gfd.Flags = arg; return 0;    // F_SETFL
                default: return 0;
            }
        }
    }
}
