// LinuxMemory - Virtual memory manager for the FLinux C# port.
// Converted from FLinux src/syscall/mm.c (Foreign Linux by Xiangyan Sun).
// Manages a 4 kB page-granular virtual address space backed by byte arrays,
// implementing mmap / munmap / mprotect / brk semantics in managed code.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DalvikUWPCSharp.FLinux.Core
{
    /// <summary>Linux mmap() protection flags (PROT_*).</summary>
    [Flags]
    public enum MemProt
    {
        None  = 0,
        Read  = 1,
        Write = 2,
        Exec  = 4,
        All   = Read | Write | Exec
    }

    /// <summary>Linux mmap() flags (MAP_*).</summary>
    [Flags]
    public enum MapFlags
    {
        Shared    = 0x01,
        Private   = 0x02,
        Fixed     = 0x10,
        Anonymous = 0x20,
        GrowsDown = 0x0100
    }

    /// <summary>
    /// Describes a single virtual-memory mapping region.
    /// Corresponds to a Linux VMA (vm_area_struct) entry in /proc/[pid]/maps.
    /// </summary>
    public class VmaRegion
    {
        public ulong Start   { get; set; }
        public ulong End     { get; set; }   // exclusive
        public MemProt Prot  { get; set; }
        public MapFlags Flags { get; set; }
        public string Name   { get; set; }   // e.g. "[stack]", "[heap]", "libfoo.so"

        public ulong Length => End - Start;
    }

    /// <summary>
    /// Software virtual memory manager – no real OS mapping, all in managed byte arrays.
    /// Each 4 kB page lives in a Dictionary keyed by page number.
    /// Mirrors FLinux mm.c behaviour: tracks VMAs, enforces prot, supports brk.
    /// </summary>
    public class LinuxMemory
    {
        public const ulong PAGE_SIZE  = 4096;
        public const ulong PAGE_MASK  = PAGE_SIZE - 1;

        // Lowest usable guest virtual address (above the null-guard page).
        private const ulong GUEST_BASE = 0x0000_1000UL;
        // Highest usable guest virtual address (4 GB for 32-bit, 128 TB for 64-bit).
        private const ulong GUEST_TOP_32 = 0x8000_0000UL;
        private const ulong GUEST_TOP_64 = 0x0001_0000_0000_0000UL;

        // Current allocation cursor (mmap hint).
        private ulong _allocCursor = 0x4000_0000UL;

        // Backing store: pageNumber → 4 kB byte array.
        private readonly Dictionary<ulong, byte[]> _pages = new Dictionary<ulong, byte[]>();

        // VMA list (ordered by start address).
        private readonly List<VmaRegion> _vmas = new List<VmaRegion>();

        // Program break (top of heap).
        private ulong _brk;
        private ulong _brkBase;

        // ── Lifecycle ────────────────────────────────────────────────────────

        public LinuxMemory()
        {
            Debug.WriteLine("[LinuxMemory] Virtual memory manager initialised.");
        }

        /// <summary>Set the initial program-break base (called after ELF load).</summary>
        public void SetBrkBase(ulong addr)
        {
            _brkBase = AlignUp(addr, PAGE_SIZE);
            _brk     = _brkBase;
        }

        // ── Low-level page access ────────────────────────────────────────────

        private ulong PageNumber(ulong addr) => addr >> 12;

        private byte[] GetOrCreatePage(ulong pageNo)
        {
            if (!_pages.TryGetValue(pageNo, out var page))
            {
                page = new byte[PAGE_SIZE];
                _pages[pageNo] = page;
            }
            return page;
        }

        // ── Public byte-level read / write ───────────────────────────────────

        public byte ReadByte(ulong addr)
        {
            ulong pageNo  = PageNumber(addr);
            ulong pageOff = addr & PAGE_MASK;
            if (!_pages.TryGetValue(pageNo, out var page))
                return 0;
            return page[(int)pageOff];
        }

        public void WriteByte(ulong addr, byte value)
        {
            ulong pageNo  = PageNumber(addr);
            ulong pageOff = addr & PAGE_MASK;
            GetOrCreatePage(pageNo)[(int)pageOff] = value;
        }

        public ushort ReadUInt16(ulong addr)
        {
            return (ushort)(ReadByte(addr) | (ReadByte(addr + 1) << 8));
        }

        public uint ReadUInt32(ulong addr)
        {
            return (uint)(ReadByte(addr)
                        | (ReadByte(addr + 1) << 8)
                        | (ReadByte(addr + 2) << 16)
                        | (ReadByte(addr + 3) << 24));
        }

        public ulong ReadUInt64(ulong addr)
        {
            return (ulong)ReadUInt32(addr) | ((ulong)ReadUInt32(addr + 4) << 32);
        }

        public void WriteUInt16(ulong addr, ushort value)
        {
            WriteByte(addr,     (byte)(value));
            WriteByte(addr + 1, (byte)(value >> 8));
        }

        public void WriteUInt32(ulong addr, uint value)
        {
            WriteByte(addr,     (byte)(value));
            WriteByte(addr + 1, (byte)(value >> 8));
            WriteByte(addr + 2, (byte)(value >> 16));
            WriteByte(addr + 3, (byte)(value >> 24));
        }

        public void WriteUInt64(ulong addr, ulong value)
        {
            WriteUInt32(addr,     (uint)(value));
            WriteUInt32(addr + 4, (uint)(value >> 32));
        }

        /// <summary>Copy a byte span into guest memory.</summary>
        public void WriteBytes(ulong addr, byte[] data, int offset, int count)
        {
            for (int i = 0; i < count; i++)
                WriteByte(addr + (ulong)i, data[offset + i]);
        }

        /// <summary>Read <paramref name="count"/> bytes from guest memory.</summary>
        public byte[] ReadBytes(ulong addr, int count)
        {
            var buf = new byte[count];
            for (int i = 0; i < count; i++)
                buf[i] = ReadByte(addr + (ulong)i);
            return buf;
        }

        /// <summary>
        /// Read a null-terminated UTF-8 string from guest memory
        /// (equivalent to strcpy from a guest pointer).
        /// </summary>
        public string ReadString(ulong addr)
        {
            var bytes = new System.Collections.Generic.List<byte>();
            while (true)
            {
                byte b = ReadByte(addr++);
                if (b == 0) break;
                bytes.Add(b);
            }
            return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
        }

        /// <summary>Write a null-terminated UTF-8 string into guest memory.</summary>
        public void WriteString(ulong addr, string value)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
            WriteBytes(addr, bytes, 0, bytes.Length);
            WriteByte(addr + (ulong)bytes.Length, 0);
        }

        // ── mmap / munmap ────────────────────────────────────────────────────

        /// <summary>
        /// Allocate <paramref name="length"/> bytes of anonymous guest memory.
        /// Returns the guest virtual address, or 0 on failure (Linux: -ENOMEM).
        /// Mirrors FLinux mm_mmap() / mmap_anonymous() logic.
        /// </summary>
        public ulong Mmap(ulong addr, ulong length, MemProt prot, MapFlags flags,
                          int fd = -1, ulong offset = 0, string name = null)
        {
            if (length == 0) return unchecked((ulong)-1L); // EINVAL

            ulong alignedLen = AlignUp(length, PAGE_SIZE);

            if ((flags & MapFlags.Fixed) != 0)
            {
                // Fixed mapping: caller dictates the address.
                ulong alignedAddr = AlignDown(addr, PAGE_SIZE);
                AllocatePages(alignedAddr, alignedLen, prot, flags, name);
                return alignedAddr;
            }

            // Find a free region after the cursor.
            ulong candidate = AlignUp(_allocCursor, PAGE_SIZE);
            candidate = FindFreeRegion(candidate, alignedLen);
            if (candidate == 0)
            {
                Debug.WriteLine("[LinuxMemory] mmap: out of virtual address space");
                return unchecked((ulong)-12L); // ENOMEM
            }

            AllocatePages(candidate, alignedLen, prot, flags, name);
            _allocCursor = candidate + alignedLen;

            Debug.WriteLine($"[LinuxMemory] mmap  addr=0x{candidate:X8} len=0x{alignedLen:X} prot={prot} flags={flags} name={name}");
            return candidate;
        }

        /// <summary>
        /// Unmap a guest memory range.
        /// Mirrors FLinux mm_munmap() – frees pages and removes VMA entries.
        /// </summary>
        public int Munmap(ulong addr, ulong length)
        {
            if ((addr & PAGE_MASK) != 0) return -22; // EINVAL
            ulong alignedLen = AlignUp(length, PAGE_SIZE);
            ulong pages = alignedLen / PAGE_SIZE;

            for (ulong i = 0; i < pages; i++)
                _pages.Remove(PageNumber(addr + i * PAGE_SIZE));

            // Remove / trim overlapping VMAs.
            RemoveVmaRange(addr, addr + alignedLen);
            Debug.WriteLine($"[LinuxMemory] munmap addr=0x{addr:X8} len=0x{alignedLen:X}");
            return 0;
        }

        /// <summary>
        /// Change protection of a guest memory range.
        /// Mirrors FLinux mm_mprotect() – updates VMA prot field.
        /// </summary>
        public int Mprotect(ulong addr, ulong length, MemProt prot)
        {
            if ((addr & PAGE_MASK) != 0) return -22; // EINVAL
            ulong end = AlignUp(addr + length, PAGE_SIZE);

            foreach (var vma in _vmas)
            {
                if (vma.End <= addr || vma.Start >= end) continue;
                vma.Prot = prot;
            }
            return 0;
        }

        // ── brk ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Adjust the program break.
        /// Passing 0 returns the current brk; a higher value extends the heap.
        /// Mirrors FLinux sys_brk().
        /// </summary>
        public ulong Brk(ulong newBrk)
        {
            if (newBrk == 0)
                return _brk;

            if (newBrk < _brkBase)
                return _brk; // Cannot shrink below base.

            ulong oldBrk = _brk;
            ulong alignedNew = AlignUp(newBrk, PAGE_SIZE);

            if (alignedNew > AlignUp(oldBrk, PAGE_SIZE))
            {
                // Extend: allocate new pages.
                ulong start = AlignUp(oldBrk, PAGE_SIZE);
                ulong pages = (alignedNew - start) / PAGE_SIZE;
                for (ulong i = 0; i < pages; i++)
                    GetOrCreatePage(PageNumber(start + i * PAGE_SIZE));
            }
            else if (alignedNew < AlignUp(oldBrk, PAGE_SIZE))
            {
                // Shrink: free pages.
                ulong end   = AlignUp(oldBrk,    PAGE_SIZE);
                ulong start = alignedNew;
                ulong pages = (end - start) / PAGE_SIZE;
                for (ulong i = 0; i < pages; i++)
                    _pages.Remove(PageNumber(start + i * PAGE_SIZE));
            }

            _brk = newBrk;
            return _brk;
        }

        // ── VMA helpers ──────────────────────────────────────────────────────

        private void AllocatePages(ulong addr, ulong length, MemProt prot, MapFlags flags, string name)
        {
            ulong pages = length / PAGE_SIZE;
            for (ulong i = 0; i < pages; i++)
                GetOrCreatePage(PageNumber(addr + i * PAGE_SIZE));

            // Register VMA.
            _vmas.RemoveAll(v => v.End > addr && v.Start < addr + length);
            _vmas.Add(new VmaRegion
            {
                Start  = addr,
                End    = addr + length,
                Prot   = prot,
                Flags  = flags,
                Name   = name ?? ""
            });
            _vmas.Sort((a, b) => a.Start.CompareTo(b.Start));
        }

        private void RemoveVmaRange(ulong start, ulong end)
        {
            _vmas.RemoveAll(v => v.Start >= start && v.End <= end);
            // Trim partially-overlapping VMAs.
            foreach (var vma in _vmas)
            {
                if (vma.Start < start && vma.End > start) vma.End = start;
                if (vma.Start < end   && vma.End > end  ) vma.Start = end;
            }
        }

        private ulong FindFreeRegion(ulong hint, ulong size)
        {
            ulong candidate = hint;
            foreach (var vma in _vmas)
            {
                if (vma.Start >= candidate + size) break;
                if (vma.End > candidate)
                    candidate = AlignUp(vma.End, PAGE_SIZE);
            }
            return (candidate + size < GUEST_TOP_64) ? candidate : 0;
        }

        // ── Stack helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Allocate a contiguous guest stack of <paramref name="stackSize"/> bytes.
        /// Returns the stack top (highest address; stack grows downward).
        /// Mirrors FLinux process_get_stack_base() + the stack setup in run().
        /// </summary>
        public ulong AllocateStack(ulong stackSize = 1024 * 1024)
        {
            ulong stackAddr = Mmap(0, stackSize, MemProt.Read | MemProt.Write,
                                   MapFlags.Private | MapFlags.Anonymous, name: "[stack]");
            return stackAddr + stackSize; // top of stack (grows downward)
        }

        // ── Utility ──────────────────────────────────────────────────────────

        public static ulong AlignUp(ulong value, ulong align)
            => (value + align - 1) & ~(align - 1);

        public static ulong AlignDown(ulong value, ulong align)
            => value & ~(align - 1);

        /// <summary>Return a human-readable summary of all current VMAs.</summary>
        public string DumpMaps()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var vma in _vmas)
                sb.AppendLine($"0x{vma.Start:X16}-0x{vma.End:X16} {vma.Prot} {vma.Name}");
            return sb.ToString();
        }
    }
}
