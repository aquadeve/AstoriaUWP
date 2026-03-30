// ElfBinary - Extended ELF loader that maps program segments into LinuxMemory.
// Wraps the existing ElfLoader parser and adds the segment-loading logic
// derived from FLinux src/syscall/exec.c: load_elf() and run().

using System;
using System.Diagnostics;
using System.IO;

namespace DalvikUWPCSharp.FLinux.Core
{
    using DalvikUWPCSharp.Classes; // ElfLoader, ElfSegment, ElfSegmentType

    /// <summary>
    /// ELF binary type (e_type field).
    /// Mirrors ET_EXEC / ET_DYN from FLinux binfmt/elf.h.
    /// </summary>
    public enum ElfType : ushort
    {
        None = 0,
        Rel  = 1,
        Exec = 2,
        Dyn  = 3,
        Core = 4
    }

    /// <summary>
    /// Auxiliary-vector entry written onto the initial guest stack.
    /// Mirrors the AT_* constants from FLinux src/common/auxvec.h.
    /// </summary>
    public static class AuxVec
    {
        public const ulong AT_NULL     =  0;
        public const ulong AT_PHDR     =  3;
        public const ulong AT_PHENT    =  4;
        public const ulong AT_PHNUM    =  5;
        public const ulong AT_PAGESZ   =  6;
        public const ulong AT_BASE     =  7;
        public const ulong AT_FLAGS    =  8;
        public const ulong AT_ENTRY    =  9;
        public const ulong AT_SECURE   = 23;
        public const ulong AT_RANDOM   = 25;
        public const ulong AT_HWCAP    = 16;
        public const ulong AT_CLKTCK   = 17;
        public const ulong AT_PLATFORM = 15;
    }

    /// <summary>
    /// Loaded ELF image: header metadata + virtual load addresses.
    /// Populated by <see cref="ElfBinary.Load"/>.
    /// </summary>
    public class LoadedElf
    {
        /// <summary>Architecture machine code (e_machine).</summary>
        public ushort Machine      { get; set; }
        /// <summary>ELF type: ET_EXEC (2) or ET_DYN (3).</summary>
        public ElfType Type        { get; set; }
        /// <summary>Virtual address of the ELF entry point (before load_base).</summary>
        public ulong EntryPoint    { get; set; }
        /// <summary>Load base – offset applied to all virtual addresses for ET_DYN.</summary>
        public ulong LoadBase      { get; set; }
        /// <summary>Lowest PT_LOAD virtual address (before relocation).</summary>
        public ulong LoadLow       { get; set; }
        /// <summary>Highest PT_LOAD virtual address end (before relocation).</summary>
        public ulong LoadHigh      { get; set; }
        /// <summary>Virtual address of the program header table in guest memory.</summary>
        public ulong PhdrAddress   { get; set; }
        /// <summary>Number of program header entries.</summary>
        public ushort PhdrNum      { get; set; }
        /// <summary>Size of one program header entry.</summary>
        public ushort PhdrEntSize  { get; set; }
        /// <summary>Offset of the interpreter path (.interp segment), or null.</summary>
        public string InterpreterPath { get; set; }
        /// <summary>Whether an interpreter (dynamic linker) is needed.</summary>
        public bool HasInterpreter { get; set; }
        /// <summary>Loaded interpreter image, or null.</summary>
        public LoadedElf Interpreter { get; set; }
    }

    /// <summary>
    /// Loads an ELF binary into a <see cref="LinuxMemory"/> virtual address space.
    /// Mirrors FLinux exec.c:load_elf() plus the run() stack-setup helper.
    /// Supports ET_EXEC (fixed-address) and ET_DYN (position-independent) binaries.
    /// </summary>
    public class ElfBinary
    {
        private readonly LinuxMemory _memory;
        private readonly ElfLoader   _parser;

        // Parsed raw data for segment loading.
        private byte[] _rawData;

        public ElfBinary(LinuxMemory memory)
        {
            _memory = memory;
            _parser = new ElfLoader();
        }

        /// <summary>The underlying ELF parser (provides architecture / export info).</summary>
        public ElfLoader Parser => _parser;

        // ── Load ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Parse <paramref name="data"/> and map all PT_LOAD segments into guest memory.
        /// Returns a <see cref="LoadedElf"/> describing the result, or null on failure.
        /// Mirrors FLinux exec.c:load_elf().
        /// </summary>
        public LoadedElf Load(byte[] data, ulong preferredBase = 0)
        {
            _rawData = data;
            if (!_parser.Load(data))
            {
                Debug.WriteLine("[ElfBinary] ElfLoader failed to parse binary.");
                return null;
            }

            // Re-parse ELF header fields that ElfLoader doesn't expose.
            var infoRaw = ParseRawHeader(data);
            if (infoRaw == null)
            {
                Debug.WriteLine("[ElfBinary] Could not parse ELF header.");
                return null;
            }
            var info = infoRaw.Value;

            // Determine virtual address range of PT_LOAD segments.
            ulong low  = ulong.MaxValue;
            ulong high = 0;
            foreach (var seg in _parser.Segments)
            {
                if (seg.Type == ElfSegmentType.Load)
                {
                    if (seg.VirtualAddress < low)  low  = seg.VirtualAddress;
                    ulong end = seg.VirtualAddress + seg.MemorySize;
                    if (end > high) high = end;
                }
            }

            if (low == ulong.MaxValue) { low = 0; high = 0; }

            // For ET_DYN (PIE), find a free region and compute load_base.
            ulong loadBase = 0;
            if (info.Type == ElfType.Dyn)
            {
                ulong size = LinuxMemory.AlignUp(high - low, LinuxMemory.PAGE_SIZE);
                loadBase = _memory.Mmap(preferredBase, size,
                    MemProt.Read | MemProt.Write | MemProt.Exec,
                    MapFlags.Private | MapFlags.Anonymous,
                    name: "libguest.so");
                if (loadBase == unchecked((ulong)-12L))
                {
                    Debug.WriteLine("[ElfBinary] Out of guest virtual memory.");
                    return null;
                }
                // munmap placeholder – LoadSegments will re-map each segment individually.
                _memory.Munmap(loadBase, LinuxMemory.AlignUp(high - low, LinuxMemory.PAGE_SIZE));
                loadBase -= low; // offset to apply to all vaddrs
            }

            // Load each PT_LOAD segment.
            ulong phdrAddr = 0;
            foreach (var seg in _parser.Segments)
            {
                if (seg.Type == ElfSegmentType.Load)
                {
                    LoadSegment(seg, loadBase, data, info.Type);
                }
                else if (seg.Type == ElfSegmentType.Phdr)
                {
                    phdrAddr = loadBase + seg.VirtualAddress;
                }
            }

            // Interpreter path from PT_INTERP.
            string interpPath = null;
            foreach (var seg in _parser.Segments)
            {
                if (seg.Type == ElfSegmentType.Interp && seg.FileSize > 0)
                {
                    interpPath = System.Text.Encoding.ASCII.GetString(
                        data, (int)seg.Offset, (int)seg.FileSize - 1 /* skip null */);
                    break;
                }
            }

            // Update brk base to just above the highest loaded address.
            _memory.SetBrkBase(loadBase + high);

            var loaded = new LoadedElf
            {
                Machine          = _parser.Machine,
                Type             = info.Type,
                EntryPoint       = _parser.EntryPoint,
                LoadBase         = loadBase,
                LoadLow          = low,
                LoadHigh         = high,
                PhdrAddress      = phdrAddr,
                PhdrNum          = info.PhdrNum,
                PhdrEntSize      = info.PhdrEntSize,
                InterpreterPath  = interpPath,
                HasInterpreter   = interpPath != null
            };

            Debug.WriteLine($"[ElfBinary] Loaded ELF: arch={_parser.GetArchitectureName()} " +
                            $"type={info.Type} base=0x{loadBase:X} entry=0x{_parser.EntryPoint:X} " +
                            $"range=[0x{low + loadBase:X},0x{high + loadBase:X})");
            return loaded;
        }

        // ── Initial stack setup ───────────────────────────────────────────────

        /// <summary>
        /// Build the initial guest stack with argc/argv/envp and the ELF auxiliary vector.
        /// Returns the new (lower) stack pointer.
        /// Mirrors FLinux exec.c:run().
        /// </summary>
        public ulong SetupStack(ulong stackTop, bool is64Bit,
                                LoadedElf exe, LoadedElf interp,
                                string[] argv, string[] envp)
        {
            ulong sp = stackTop;

            // Write strings into the "string area" at the bottom of the stack.
            var argPtrs = new ulong[argv.Length];
            var envPtrs = new ulong[envp.Length];

            // Write env strings.
            for (int i = envp.Length - 1; i >= 0; i--)
            {
                sp = WriteStackString(sp, envp[i], is64Bit);
                envPtrs[i] = sp;
            }
            // Write arg strings.
            for (int i = argv.Length - 1; i >= 0; i--)
            {
                sp = WriteStackString(sp, argv[i], is64Bit);
                argPtrs[i] = sp;
            }

            // 16-random bytes for AT_RANDOM.
            sp -= 16;
            ulong randomAddr = sp;
            var rnd = new Random();
            for (int i = 0; i < 16; i++)
                _memory.WriteByte(sp + (ulong)i, (byte)rnd.Next(256));

            // Align sp to pointer size.
            ulong psize = is64Bit ? 8UL : 4UL;
            sp = LinuxMemory.AlignDown(sp, psize * 2);

            // ── Aux vector (pushed in reverse order) ────────────────────────
            PushAux(ref sp, is64Bit, AuxVec.AT_NULL,   0);
            PushAux(ref sp, is64Bit, AuxVec.AT_FLAGS,  0);
            PushAux(ref sp, is64Bit, AuxVec.AT_SECURE, 0);
            PushAux(ref sp, is64Bit, AuxVec.AT_RANDOM, randomAddr);
            PushAux(ref sp, is64Bit, AuxVec.AT_PAGESZ, LinuxMemory.PAGE_SIZE);
            PushAux(ref sp, is64Bit, AuxVec.AT_PHDR,
                    exe.PhdrAddress != 0 ? exe.PhdrAddress : exe.LoadBase + exe.EntryPoint);
            PushAux(ref sp, is64Bit, AuxVec.AT_PHENT, exe.PhdrEntSize);
            PushAux(ref sp, is64Bit, AuxVec.AT_PHNUM, exe.PhdrNum);
            ulong entry = exe.Type == ElfType.Dyn
                ? exe.LoadBase + exe.EntryPoint
                : exe.EntryPoint;
            PushAux(ref sp, is64Bit, AuxVec.AT_ENTRY, entry);
            ulong interpBase = (interp != null) ? interp.LoadBase : 0;
            PushAux(ref sp, is64Bit, AuxVec.AT_BASE,  interpBase);

            // ── envp[] ──────────────────────────────────────────────────────
            PushPtr(ref sp, is64Bit, 0); // null terminator
            for (int i = envp.Length - 1; i >= 0; i--)
                PushPtr(ref sp, is64Bit, envPtrs[i]);

            // ── argv[] ──────────────────────────────────────────────────────
            PushPtr(ref sp, is64Bit, 0); // null terminator
            for (int i = argv.Length - 1; i >= 0; i--)
                PushPtr(ref sp, is64Bit, argPtrs[i]);

            // ── argc ────────────────────────────────────────────────────────
            if (is64Bit)
            {
                sp -= 8;
                _memory.WriteUInt64(sp, (ulong)argv.Length);
            }
            else
            {
                sp -= 4;
                _memory.WriteUInt32(sp, (uint)argv.Length);
            }

            Debug.WriteLine($"[ElfBinary] Stack set up: sp=0x{sp:X} argc={argv.Length} envc={envp.Length}");
            return sp;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private void LoadSegment(ElfSegment seg, ulong loadBase, byte[] data, ElfType type)
        {
            ulong vaddr = (type == ElfType.Dyn) ? (loadBase + seg.VirtualAddress) : seg.VirtualAddress;
            ulong memsz = seg.MemorySize;
            ulong filesz = seg.FileSize;

            // Map memory for the segment.
            MemProt prot = MemProt.Read;
            if ((seg.Flags & 2) != 0) prot |= MemProt.Write;
            if ((seg.Flags & 1) != 0) prot |= MemProt.Exec;

            ulong alignedAddr = LinuxMemory.AlignDown(vaddr, LinuxMemory.PAGE_SIZE);
            ulong alignedEnd  = LinuxMemory.AlignUp(vaddr + memsz, LinuxMemory.PAGE_SIZE);
            _memory.Mmap(alignedAddr, alignedEnd - alignedAddr,
                         prot, MapFlags.Private | MapFlags.Anonymous | MapFlags.Fixed,
                         name: ".so:PT_LOAD");

            // Copy file content.
            if (filesz > 0 && seg.Offset < (ulong)data.Length)
            {
                ulong copyLen = Math.Min(filesz, (ulong)data.Length - seg.Offset);
                _memory.WriteBytes(vaddr, data, (int)seg.Offset, (int)copyLen);
            }
            // BSS: already zeroed by GetOrCreatePage().
        }

        private ulong WriteStackString(ulong sp, string s, bool is64Bit)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(s);
            sp -= (ulong)(bytes.Length + 1);
            _memory.WriteBytes(sp, bytes, 0, bytes.Length);
            _memory.WriteByte(sp + (ulong)bytes.Length, 0);
            return sp;
        }

        private void PushAux(ref ulong sp, bool is64Bit, ulong id, ulong val)
        {
            if (is64Bit)
            {
                sp -= 8; _memory.WriteUInt64(sp, val);
                sp -= 8; _memory.WriteUInt64(sp, id);
            }
            else
            {
                sp -= 4; _memory.WriteUInt32(sp, (uint)val);
                sp -= 4; _memory.WriteUInt32(sp, (uint)id);
            }
        }

        private void PushPtr(ref ulong sp, bool is64Bit, ulong ptr)
        {
            if (is64Bit) { sp -= 8; _memory.WriteUInt64(sp, ptr); }
            else         { sp -= 4; _memory.WriteUInt32(sp, (uint)ptr); }
        }

        /// <summary>
        /// Read just the e_type, e_phentsize, e_phnum fields from raw ELF data.
        /// ElfLoader doesn't expose these publicly, so we re-read them here.
        /// </summary>
        private static (ElfType Type, ushort PhdrNum, ushort PhdrEntSize)? ParseRawHeader(byte[] data)
        {
            if (data == null || data.Length < 52) return null;
            bool is64 = data[4] == 2;
            try
            {
                using (var ms = new MemoryStream(data))
                using (var r  = new BinaryReader(ms))
                {
                    ms.Seek(16, SeekOrigin.Begin); // skip e_ident
                    var type    = (ElfType)r.ReadUInt16();
                    r.ReadUInt16(); // e_machine
                    r.ReadUInt32(); // e_version
                    if (is64) { r.ReadUInt64(); r.ReadUInt64(); r.ReadUInt64(); }
                    else       { r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); }
                    r.ReadUInt32(); // e_flags
                    r.ReadUInt16(); // e_ehsize
                    ushort phEntSize = r.ReadUInt16();
                    ushort phNum     = r.ReadUInt16();
                    return (type, phNum, phEntSize);
                }
            }
            catch { return null; }
        }
    }
}
