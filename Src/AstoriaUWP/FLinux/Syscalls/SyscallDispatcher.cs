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

                // ── Futex ──────────────────────────────────────────────────────
                case "futex":
                    // Stub: FUTEX_WAIT returns 0 immediately (no actual blocking),
                    // FUTEX_WAKE always wakes waiters.
                    return 0;

                // ── Unknown ────────────────────────────────────────────────────
                default:
                    Debug.WriteLine($"[Syscall] UNIMPLEMENTED: {name} (a0=0x{a0:X})");
                    return -38; // ENOSYS
            }
        }

        // ── Individual syscall implementations ────────────────────────────────

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
            _memory.WriteUInt32((uint)addr, stat.Mode);       addr += 4;
            _memory.WriteUInt32((uint)addr, stat.Nlink);      addr += 4;
            _memory.WriteUInt32((uint)addr, (uint)stat.Uid);  addr += 4;
            _memory.WriteUInt32((uint)addr, (uint)stat.Gid);  addr += 4;
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
            // struct sysinfo (simplified 64-bit version).
            ulong addr = buf;
            _memory.WriteUInt64(addr, 0);                  addr += 8; // uptime
            _memory.WriteUInt64(addr, 0x40000000UL);       addr += 8; // loads[0]
            _memory.WriteUInt64(addr, 0x40000000UL);       addr += 8; // loads[1]
            _memory.WriteUInt64(addr, 0x40000000UL);       addr += 8; // loads[2]
            _memory.WriteUInt64(addr, 2UL * 1024 * 1024 * 1024); addr += 8; // totalram 2 GB
            _memory.WriteUInt64(addr, 1UL * 1024 * 1024 * 1024); addr += 8; // freeram
            _memory.WriteUInt64(addr, 0);                  addr += 8; // sharedram
            _memory.WriteUInt64(addr, 0);                  addr += 8; // bufferram
            _memory.WriteUInt64(addr, 4UL * 1024 * 1024 * 1024); addr += 8; // totalswap
            _memory.WriteUInt64(addr, 4UL * 1024 * 1024 * 1024); addr += 8; // freeswap
            _memory.WriteUInt16(addr, 1);                  addr += 2; // procs
            _memory.WriteUInt32(addr, 4096);               // mem_unit = 1 byte? No: PAGE_SIZE
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
