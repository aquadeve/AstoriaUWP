// SyscallNumbers - Linux syscall number tables for ARM32, ARM64, and x86-64.
// Derived from FLinux src/linux/arm/unistd.h (ARM32),
// the AArch64 Linux uapi unistd.h (ARM64),
// and FLinux src/syscall/syscall_table_x64.h (x86-64).
// These constants are used by SyscallDispatcher to route syscall numbers
// to their managed C# handler implementations.

namespace DalvikUWPCSharp.FLinux.Syscalls
{
    /// <summary>
    /// ARM32 Linux syscall numbers (EABI __NR_SYSCALL_BASE = 0).
    /// Source: FLinux src/linux/arm/unistd.h.
    /// </summary>
    public static class Arm32Syscalls
    {
        public const int restart_syscall =   0;
        public const int exit            =   1;
        public const int fork            =   2;
        public const int read            =   3;
        public const int write           =   4;
        public const int open            =   5;
        public const int close           =   6;
        public const int creat           =   8;
        public const int link            =   9;
        public const int unlink          =  10;
        public const int execve          =  11;
        public const int chdir           =  12;
        public const int lseek           =  19;
        public const int getpid          =  20;
        public const int getuid          =  24;
        public const int geteuid         =  49;
        public const int getgid          =  47;
        public const int getegid         =  50;
        public const int brk             =  45;
        public const int ioctl           =  54;
        public const int fcntl           =  55;
        public const int dup             =  41;
        public const int dup2            =  63;
        public const int getppid         =  64;
        public const int setsid          =  66;
        public const int getdents        =  89;
        public const int mmap            =  90;
        public const int munmap          =  91;
        public const int truncate        =  92;
        public const int ftruncate       =  93;
        public const int mprotect        = 125;
        public const int sigprocmask     = 126;
        public const int getdents64      = 217;
        public const int gettid          = 224;
        public const int clock_gettime   = 263;
        public const int clock_getres    = 264;
        public const int exit_group      = 248;
        public const int set_tid_address = 256;
        public const int futex           = 240;
        public const int nanosleep       = 162;
        public const int sched_yield     = 158;
        public const int getpriority     =  96;
        public const int setpriority     =  97;
        public const int prctl           = 172;
        public const int uname           = 122;
        public const int stat64          = 195;
        public const int fstat64         = 197;
        public const int lstat64         = 196;
        public const int mmap2           = 192;
        public const int mremap          = 163;
        public const int madvise         = 220;
        public const int openat          = 322;
        public const int faccessat       = 334;
        public const int pipe            =  42;
        public const int pipe2           = 359;
        public const int socketcall      = 102;
        public const int gettimeofday    =  78;
        public const int rt_sigaction    = 174;
        public const int rt_sigprocmask  = 175;
        public const int getrlimit       =  76;
        public const int setrlimit       =  75;
        public const int prlimit64       = 369;
        public const int getcwd          = 183;
        public const int mkdir           =  39;
        public const int rmdir           =  40;
        public const int access          =  33;
        public const int rename          =  38;
        public const int chmod           =  15;
        public const int fchmod          =  94;
        public const int kill            =  37;
        public const int tgkill          = 268;
        public const int tkill           = 238;
        public const int sysinfo         = 116;
        public const int times           =  43;
        public const int socket          = 281;
        public const int bind            = 282;
        public const int connect         = 283;
        public const int listen          = 284;
        public const int accept          = 285;
        public const int setsockopt      = 294;
        public const int getsockopt      = 295;
        public const int sendto          = 290;
        public const int recvfrom        = 291;
        public const int sendmsg         = 296;
        public const int recvmsg         = 297;
        public const int shutdown        = 293;

        // Additional syscalls from ExAndroidNativeEmu (ARM32 EABI numbers)
        public const int ptrace          =  26;
        public const int wait4           = 114;
        public const int sigaction       =  67;
        public const int sigaltstack     = 186;
        public const int vfork           = 190;
        public const int getuid32        = 199;
        public const int clone           = 120;
        public const int getcpu          = 345;
        public const int dup3_arm32      = 358;
        public const int process_vm_readv = 376;
        public const int getrandom       = 384;
        // VFS syscalls from ExAndroidNativeEmu vfs/file_system.py
        // Note: unlink(10), ioctl(54), getdents64(217) already defined above.
        public const int writev          = 146;
        public const int poll            = 168;
        public const int llseek          = 140;
        public const int fcntl64         = 221;
        public const int statfs64        = 266;
        public const int mkdirat         = 323;
        public const int fstatat64       = 327;
        public const int unlinkat        = 328;
        public const int readlinkat      = 332;
        public const int ppoll           = 336;
        // ARM-private syscalls (encoded as SWI 0xF0000 + N on ARM)
        public const int ARM_cacheflush  = 0xF0002;
        public const int ARM_set_tls     = 0xF0005;
    }

    /// <summary>
    /// AArch64 (ARM64) Linux syscall numbers.
    /// Source: Linux kernel arch/arm64/include/uapi/asm/unistd.h.
    /// </summary>
    public static class Arm64Syscalls
    {
        public const int io_setup         =   0;
        public const int exit             =  93;
        public const int exit_group       =  94;
        public const int read             =  63;
        public const int write            =  64;
        public const int openat           =  56;
        public const int close            =  57;
        public const int lseek            =  62;
        public const int mmap             = 222;
        public const int mprotect         = 226;
        public const int munmap           = 215;
        public const int mremap           = 216;
        public const int brk              = 214;
        public const int ioctl            =  29;
        public const int fcntl            =  25;
        public const int dup              =  23;
        public const int dup3             =  24;
        public const int getdents64       =  61;
        public const int getpid           = 172;
        public const int getppid          = 173;
        public const int getuid           = 174;
        public const int getgid           = 176;
        public const int geteuid          = 175;
        public const int getegid          = 177;
        public const int gettid           = 178;
        public const int futex            =  98;
        public const int nanosleep        = 101;
        public const int clock_gettime    = 113;
        public const int clock_getres     = 114;
        public const int set_tid_address  =  96;
        public const int sched_yield      = 124;
        public const int prctl            = 167;
        public const int uname            = 160;
        public const int fstat            =  80;
        public const int newfstatat       = 262;
        public const int pipe2            =  59;
        public const int socket           = 198;
        public const int bind             = 200;
        public const int connect          = 203;
        public const int listen           = 201;
        public const int accept           = 202;
        public const int setsockopt       = 208;
        public const int getsockopt       = 209;
        public const int sendto           = 206;
        public const int recvfrom         = 207;
        public const int sendmsg          = 211;
        public const int recvmsg          = 212;
        public const int shutdown         = 210;
        public const int madvise          = 233;
        public const int getrlimit        = 163;
        public const int prlimit64        = 261;
        public const int getcwd           =  17;
        public const int mkdir_unavailable = unchecked((int)0xFFFFFFFF); // not a native ARM64 syscall; use mkdirat instead
        public const int mkdirat          =  34;
        public const int unlinkat         =  35;
        public const int renameat         =  38;
        public const int faccessat        =  48;
        public const int kill             = 129;
        public const int tgkill           = 131;
        public const int tkill            = 130;
        public const int rt_sigaction     = 134;
        public const int rt_sigprocmask   = 135;
        public const int sysinfo          = 179;
        public const int gettimeofday     = 169;

        // Additional syscalls from ExAndroidNativeEmu (ARM64 numbers)
        public const int ptrace           = 117;
        public const int execve           = 221;
        public const int clone            = 220;
        public const int wait4            = 260;
        public const int sigaltstack      = 132;
        public const int process_vm_readv = 270;
        public const int getrandom        = 278;
        public const int getcpu           = 168;
        // VFS syscalls from ExAndroidNativeEmu vfs/file_system.py (ARM64)
        // Note: ioctl(29), getdents64(61), mkdirat(34), unlinkat(35), faccessat(48) already defined above.
        public const int writev           =  66;
        public const int statfs           =  43;
        public const int readlinkat       =  78;
        public const int ppoll            =  73;
    }

    /// <summary>
    /// x86-64 Linux syscall numbers.
    /// Source: FLinux src/syscall/syscall_table_x64.h (indices match position in table).
    /// </summary>
    public static class X64Syscalls
    {
        public const int read            =   0;
        public const int write           =   1;
        public const int open            =   2;
        public const int close           =   3;
        public const int stat            =   4;
        public const int fstat           =   5;
        public const int lstat           =   6;
        public const int poll            =   7;
        public const int lseek           =   8;
        public const int mmap            =   9;
        public const int mprotect        =  10;
        public const int munmap          =  11;
        public const int brk             =  12;
        public const int rt_sigaction    =  13;
        public const int rt_sigprocmask  =  14;
        public const int rt_sigreturn    =  15;
        public const int ioctl           =  16;
        public const int pread64         =  17;
        public const int pwrite64        =  18;
        public const int readv           =  19;
        public const int writev          =  20;
        public const int access          =  21;
        public const int pipe            =  22;
        public const int select          =  23;
        public const int sched_yield     =  24;
        public const int mremap          =  25;
        public const int msync           =  26;
        public const int madvise         =  28;
        public const int dup             =  32;
        public const int dup2            =  33;
        public const int nanosleep       =  35;
        public const int getpid          =  39;
        public const int socket          =  41;
        public const int connect         =  42;
        public const int accept          =  43;
        public const int sendto          =  44;
        public const int recvfrom        =  45;
        public const int sendmsg         =  46;
        public const int recvmsg         =  47;
        public const int shutdown        =  48;
        public const int bind            =  49;
        public const int listen          =  50;
        public const int getsockname     =  51;
        public const int getpeername     =  52;
        public const int setsockopt      =  54;
        public const int getsockopt      =  55;
        public const int clone           =  56;
        public const int execve          =  59;
        public const int wait4           =  61;
        public const int kill            =  62;
        public const int uname           =  63;
        public const int fcntl           =  72;
        public const int flock           =  73;
        public const int fsync           =  74;
        public const int fdatasync       =  75;
        public const int truncate        =  76;
        public const int ftruncate       =  77;
        public const int getdents        =  78;
        public const int getcwd          =  79;
        public const int chdir           =  80;
        public const int fchdir          =  81;
        public const int rename          =  82;
        public const int mkdir           =  83;
        public const int rmdir           =  84;
        public const int creat           =  85;
        public const int link            =  86;
        public const int unlink          =  87;
        public const int symlink         =  88;
        public const int readlink        =  89;
        public const int chmod           =  90;
        public const int fchmod          =  91;
        public const int chown           =  92;
        public const int fchown          =  93;
        public const int lchown          =  94;
        public const int umask           =  95;
        public const int gettimeofday    =  96;
        public const int getrlimit       =  97;
        public const int getrusage       =  98;
        public const int sysinfo         =  99;
        public const int getuid          = 102;
        public const int getgid          = 104;
        public const int geteuid         = 107;
        public const int getegid         = 108;
        public const int getppid         = 110;
        public const int setpgid         = 109;
        public const int getpgrp         = 111;
        public const int setsid          = 112;
        public const int setuid          = 105;
        public const int setgid          = 106;
        public const int prctl           = 157;
        public const int arch_prctl      = 158;
        public const int gettid          = 186;
        public const int futex           = 202;
        public const int set_tid_address = 218;
        public const int clock_gettime   = 228;
        public const int clock_getres    = 229;
        public const int exit_group      = 231;
        public const int openat          = 257;
        public const int mkdirat         = 258;
        public const int newfstatat      = 262;
        public const int unlinkat        = 263;
        public const int renameat        = 264;
        public const int faccessat       = 269;
        public const int pipe2           = 293;
        public const int prlimit64       = 302;
        public const int getdents64      = 217;
        public const int exit_           =  60;

        // Additional syscalls from ExAndroidNativeEmu (x64 numbers)
        public const int ptrace          = 101;
        public const int getrandom       = 318;
        public const int process_vm_readv = 310;
        public const int getcpu          = 309;
        public const int sigaltstack     = 131;
        public const int tgkill          = 234;
        public const int tkill           = 200;
    }
}
