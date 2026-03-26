// ElfLoader - Parses ELF binary headers from Android .so native libraries.
// Inspired by apkenv's ELF loading approach for running Android native code.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace DalvikUWPCSharp.Classes
{
    /// <summary>
    /// Represents the type of an ELF binary segment.
    /// </summary>
    public enum ElfSegmentType : uint
    {
        Null = 0,
        Load = 1,
        Dynamic = 2,
        Interp = 3,
        Note = 4,
        Shlib = 5,
        Phdr = 6,
        Tls = 7
    }

    /// <summary>
    /// Represents a parsed ELF section header.
    /// </summary>
    public class ElfSection
    {
        public string Name { get; set; }
        public uint Type { get; set; }
        public ulong Flags { get; set; }
        public ulong Address { get; set; }
        public ulong Offset { get; set; }
        public ulong Size { get; set; }
    }

    /// <summary>
    /// Represents a parsed ELF program segment.
    /// </summary>
    public class ElfSegment
    {
        public ElfSegmentType Type { get; set; }
        public ulong Offset { get; set; }
        public ulong VirtualAddress { get; set; }
        public ulong PhysicalAddress { get; set; }
        public ulong FileSize { get; set; }
        public ulong MemorySize { get; set; }
        public uint Flags { get; set; }
        public ulong Alignment { get; set; }
    }

    /// <summary>
    /// Represents a symbol from the ELF dynamic symbol table.
    /// </summary>
    public class ElfSymbol
    {
        public string Name { get; set; }
        public ulong Value { get; set; }
        public ulong Size { get; set; }
        public byte Info { get; set; }
        public ushort SectionIndex { get; set; }
    }

    /// <summary>
    /// Parses Android ELF (.so) binaries and extracts metadata needed for JNI bridging.
    /// Based on the approach used by apkenv for loading Android native libraries.
    /// Supports both 32-bit (ARM) and 64-bit (x64/ARM64) ELF formats.
    /// </summary>
    public class ElfLoader
    {
        public bool Is64Bit { get; private set; }
        public bool IsLittleEndian { get; private set; }
        public ushort Machine { get; private set; }
        public ulong EntryPoint { get; private set; }

        public List<ElfSection> Sections { get; private set; } = new List<ElfSection>();
        public List<ElfSegment> Segments { get; private set; } = new List<ElfSegment>();
        public List<ElfSymbol> Symbols { get; private set; } = new List<ElfSymbol>();
        public List<string> NeededLibraries { get; private set; } = new List<string>();
        public List<string> ExportedFunctions { get; private set; } = new List<string>();

        private byte[] rawData;

        /// <summary>
        /// Loads and parses an ELF binary from a byte array.
        /// </summary>
        public bool Load(byte[] data)
        {
            if (data == null || data.Length < 16)
                return false;

            rawData = data;

            // Verify ELF magic: 0x7F 'E' 'L' 'F'
            if (data[0] != 0x7F || data[1] != 0x45 || data[2] != 0x4C || data[3] != 0x46)
            {
                Debug.WriteLine("[ElfLoader] Invalid ELF magic header");
                return false;
            }

            Is64Bit = data[4] == 2; // 1 = 32-bit, 2 = 64-bit
            IsLittleEndian = data[5] == 1;

            if (!IsLittleEndian)
            {
                Debug.WriteLine("[ElfLoader] Big-endian ELF not supported (Android uses little-endian)");
                return false;
            }

            try
            {
                using (var ms = new MemoryStream(data))
                using (var reader = new BinaryReader(ms))
                {
                    if (Is64Bit)
                        ParseElfHeader64(reader);
                    else
                        ParseElfHeader32(reader);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[ElfLoader] Parse error: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Loads an ELF binary from a file stream asynchronously.
        /// </summary>
        public async Task<bool> LoadAsync(Stream stream)
        {
            using (var ms = new MemoryStream())
            {
                await stream.CopyToAsync(ms);
                return Load(ms.ToArray());
            }
        }

        /// <summary>
        /// Finds an exported JNI function by name (e.g., "Java_com_example_MyClass_nativeMethod").
        /// </summary>
        public ElfSymbol FindJniFunction(string jniFunctionName)
        {
            foreach (var sym in Symbols)
            {
                if (sym.Name == jniFunctionName)
                    return sym;
            }
            return null;
        }

        /// <summary>
        /// Lists all JNI-style exported functions (names starting with "Java_").
        /// </summary>
        public List<string> GetJniExports()
        {
            var result = new List<string>();
            foreach (var name in ExportedFunctions)
            {
                if (name.StartsWith("Java_"))
                    result.Add(name);
            }
            return result;
        }

        /// <summary>
        /// Checks if this ELF is compatible with the current runtime architecture.
        /// </summary>
        public bool IsCompatibleArchitecture()
        {
            // EM_ARM = 40, EM_AARCH64 = 183, EM_386 = 3, EM_X86_64 = 62
            switch (Machine)
            {
                case 40:   // ARM 32-bit
                case 183:  // ARM64 / AArch64
                case 3:    // x86
                case 62:   // x86-64
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Gets a human-readable description of the target architecture.
        /// </summary>
        public string GetArchitectureName()
        {
            switch (Machine)
            {
                case 40: return "ARM";
                case 183: return "ARM64";
                case 3: return "x86";
                case 62: return "x86-64";
                default: return "Unknown (" + Machine + ")";
            }
        }

        private void ParseElfHeader32(BinaryReader reader)
        {
            reader.BaseStream.Seek(16, SeekOrigin.Begin);

            ushort type = reader.ReadUInt16();
            Machine = reader.ReadUInt16();
            uint version = reader.ReadUInt32();
            EntryPoint = reader.ReadUInt32();
            uint phOffset = reader.ReadUInt32();
            uint shOffset = reader.ReadUInt32();
            uint flags = reader.ReadUInt32();
            ushort ehSize = reader.ReadUInt16();
            ushort phEntSize = reader.ReadUInt16();
            ushort phNum = reader.ReadUInt16();
            ushort shEntSize = reader.ReadUInt16();
            ushort shNum = reader.ReadUInt16();
            ushort shStrNdx = reader.ReadUInt16();

            // Parse program headers (segments)
            for (int i = 0; i < phNum; i++)
            {
                reader.BaseStream.Seek(phOffset + (i * phEntSize), SeekOrigin.Begin);
                var seg = new ElfSegment
                {
                    Type = (ElfSegmentType)reader.ReadUInt32(),
                    Offset = reader.ReadUInt32(),
                    VirtualAddress = reader.ReadUInt32(),
                    PhysicalAddress = reader.ReadUInt32(),
                    FileSize = reader.ReadUInt32(),
                    MemorySize = reader.ReadUInt32(),
                    Flags = reader.ReadUInt32(),
                    Alignment = reader.ReadUInt32()
                };
                Segments.Add(seg);
            }

            // Parse section headers
            ParseSectionHeaders32(reader, shOffset, shEntSize, shNum, shStrNdx);

            // Extract dynamic info (needed libraries, exports)
            ExtractDynamicInfo32(reader);
        }

        private void ParseElfHeader64(BinaryReader reader)
        {
            reader.BaseStream.Seek(16, SeekOrigin.Begin);

            ushort type = reader.ReadUInt16();
            Machine = reader.ReadUInt16();
            uint version = reader.ReadUInt32();
            EntryPoint = reader.ReadUInt64();
            ulong phOffset = reader.ReadUInt64();
            ulong shOffset = reader.ReadUInt64();
            uint flags = reader.ReadUInt32();
            ushort ehSize = reader.ReadUInt16();
            ushort phEntSize = reader.ReadUInt16();
            ushort phNum = reader.ReadUInt16();
            ushort shEntSize = reader.ReadUInt16();
            ushort shNum = reader.ReadUInt16();
            ushort shStrNdx = reader.ReadUInt16();

            // Parse program headers (segments)
            for (int i = 0; i < phNum; i++)
            {
                reader.BaseStream.Seek((long)(phOffset + (ulong)(i * phEntSize)), SeekOrigin.Begin);
                var seg = new ElfSegment
                {
                    Type = (ElfSegmentType)reader.ReadUInt32(),
                    Flags = reader.ReadUInt32(),
                    Offset = reader.ReadUInt64(),
                    VirtualAddress = reader.ReadUInt64(),
                    PhysicalAddress = reader.ReadUInt64(),
                    FileSize = reader.ReadUInt64(),
                    MemorySize = reader.ReadUInt64(),
                    Alignment = reader.ReadUInt64()
                };
                Segments.Add(seg);
            }

            // Parse section headers
            ParseSectionHeaders64(reader, shOffset, shEntSize, shNum, shStrNdx);

            // Extract dynamic info (needed libraries, exports)
            ExtractDynamicInfo64(reader);
        }

        private void ParseSectionHeaders32(BinaryReader reader, uint shOffset, ushort shEntSize, ushort shNum, ushort shStrNdx)
        {
            if (shNum == 0 || shOffset == 0)
                return;

            // Read section name string table
            byte[] shStrTab = null;
            if (shStrNdx < shNum)
            {
                reader.BaseStream.Seek(shOffset + (shStrNdx * shEntSize) + 16, SeekOrigin.Begin);
                uint strTabOffset = reader.ReadUInt32();
                uint strTabSize = reader.ReadUInt32();
                if (strTabOffset > 0 && strTabSize > 0 && strTabOffset + strTabSize <= rawData.Length)
                {
                    shStrTab = new byte[strTabSize];
                    Array.Copy(rawData, (int)strTabOffset, shStrTab, 0, (int)strTabSize);
                }
            }

            for (int i = 0; i < shNum; i++)
            {
                reader.BaseStream.Seek(shOffset + (i * shEntSize), SeekOrigin.Begin);
                uint nameIdx = reader.ReadUInt32();
                var section = new ElfSection
                {
                    Type = reader.ReadUInt32(),
                    Flags = reader.ReadUInt32(),
                    Address = reader.ReadUInt32(),
                    Offset = reader.ReadUInt32(),
                    Size = reader.ReadUInt32(),
                    Name = ReadStringFromTable(shStrTab, nameIdx)
                };
                Sections.Add(section);
            }
        }

        private void ParseSectionHeaders64(BinaryReader reader, ulong shOffset, ushort shEntSize, ushort shNum, ushort shStrNdx)
        {
            if (shNum == 0 || shOffset == 0)
                return;

            // Read section name string table
            byte[] shStrTab = null;
            if (shStrNdx < shNum)
            {
                reader.BaseStream.Seek((long)(shOffset + (ulong)(shStrNdx * shEntSize) + 24), SeekOrigin.Begin);
                ulong strTabOffset = reader.ReadUInt64();
                ulong strTabSize = reader.ReadUInt64();
                if (strTabOffset > 0 && strTabSize > 0 && strTabOffset + strTabSize <= (ulong)rawData.Length)
                {
                    shStrTab = new byte[strTabSize];
                    Array.Copy(rawData, (int)(long)strTabOffset, shStrTab, 0, (int)(long)strTabSize);
                }
            }

            for (int i = 0; i < shNum; i++)
            {
                reader.BaseStream.Seek((long)(shOffset + (ulong)(i * shEntSize)), SeekOrigin.Begin);
                uint nameIdx = reader.ReadUInt32();
                var section = new ElfSection
                {
                    Type = reader.ReadUInt32(),
                    Flags = reader.ReadUInt64(),
                    Address = reader.ReadUInt64(),
                    Offset = reader.ReadUInt64(),
                    Size = reader.ReadUInt64(),
                    Name = ReadStringFromTable(shStrTab, nameIdx)
                };
                Sections.Add(section);
            }
        }

        private void ExtractDynamicInfo32(BinaryReader reader)
        {
            // Find .dynsym and .dynstr sections for symbol extraction
            ElfSection dynsym = null;
            ElfSection dynstr = null;
            ElfSection dynamic = null;

            foreach (var sec in Sections)
            {
                if (sec.Name == ".dynsym") dynsym = sec;
                else if (sec.Name == ".dynstr") dynstr = sec;
                else if (sec.Name == ".dynamic") dynamic = sec;
            }

            // Extract dynamic symbols
            if (dynsym != null && dynstr != null)
            {
                byte[] strTab = new byte[dynstr.Size];
                if (dynstr.Offset + dynstr.Size <= (ulong)rawData.Length)
                {
                    Array.Copy(rawData, (int)(long)dynstr.Offset, strTab, 0, (int)(long)dynstr.Size);
                }

                int symSize = 16; // Elf32_Sym size
                int count = (int)(dynsym.Size / (ulong)symSize);
                for (int i = 0; i < count; i++)
                {
                    reader.BaseStream.Seek((long)(dynsym.Offset + (ulong)(i * symSize)), SeekOrigin.Begin);
                    uint nameIdx = reader.ReadUInt32();
                    uint value = reader.ReadUInt32();
                    uint size = reader.ReadUInt32();
                    byte info = reader.ReadByte();

                    string name = ReadStringFromTable(strTab, nameIdx);
                    if (!string.IsNullOrEmpty(name))
                    {
                        Symbols.Add(new ElfSymbol
                        {
                            Name = name,
                            Value = value,
                            Size = size,
                            Info = info
                        });

                        // Exported functions have non-zero value and are FUNC type
                        if (value != 0 && (info & 0xF) == 2) // STT_FUNC
                            ExportedFunctions.Add(name);
                    }
                }
            }

            // Extract needed libraries from .dynamic section
            if (dynamic != null && dynstr != null)
            {
                byte[] strTab = new byte[dynstr.Size];
                if (dynstr.Offset + dynstr.Size <= (ulong)rawData.Length)
                {
                    Array.Copy(rawData, (int)(long)dynstr.Offset, strTab, 0, (int)(long)dynstr.Size);
                }

                int entSize = 8; // Elf32_Dyn size
                int count = (int)(dynamic.Size / (ulong)entSize);
                for (int i = 0; i < count; i++)
                {
                    reader.BaseStream.Seek((long)(dynamic.Offset + (ulong)(i * entSize)), SeekOrigin.Begin);
                    int tag = reader.ReadInt32();
                    uint val = reader.ReadUInt32();

                    if (tag == 1) // DT_NEEDED
                    {
                        string lib = ReadStringFromTable(strTab, val);
                        if (!string.IsNullOrEmpty(lib))
                            NeededLibraries.Add(lib);
                    }
                    else if (tag == 0) // DT_NULL - end of dynamic section
                        break;
                }
            }
        }

        private void ExtractDynamicInfo64(BinaryReader reader)
        {
            ElfSection dynsym = null;
            ElfSection dynstr = null;
            ElfSection dynamic = null;

            foreach (var sec in Sections)
            {
                if (sec.Name == ".dynsym") dynsym = sec;
                else if (sec.Name == ".dynstr") dynstr = sec;
                else if (sec.Name == ".dynamic") dynamic = sec;
            }

            if (dynsym != null && dynstr != null)
            {
                byte[] strTab = new byte[dynstr.Size];
                if (dynstr.Offset + dynstr.Size <= (ulong)rawData.Length)
                {
                    Array.Copy(rawData, (int)(long)dynstr.Offset, strTab, 0, (int)(long)dynstr.Size);
                }

                int symSize = 24; // Elf64_Sym size
                int count = (int)(dynsym.Size / (ulong)symSize);
                for (int i = 0; i < count; i++)
                {
                    reader.BaseStream.Seek((long)(dynsym.Offset + (ulong)(i * symSize)), SeekOrigin.Begin);
                    uint nameIdx = reader.ReadUInt32();
                    byte info = reader.ReadByte();
                    reader.ReadByte(); // other
                    ushort shndx = reader.ReadUInt16();
                    ulong value = reader.ReadUInt64();
                    ulong size = reader.ReadUInt64();

                    string name = ReadStringFromTable(strTab, nameIdx);
                    if (!string.IsNullOrEmpty(name))
                    {
                        Symbols.Add(new ElfSymbol
                        {
                            Name = name,
                            Value = value,
                            Size = size,
                            Info = info,
                            SectionIndex = shndx
                        });

                        if (value != 0 && (info & 0xF) == 2)
                            ExportedFunctions.Add(name);
                    }
                }
            }

            if (dynamic != null && dynstr != null)
            {
                byte[] strTab = new byte[dynstr.Size];
                if (dynstr.Offset + dynstr.Size <= (ulong)rawData.Length)
                {
                    Array.Copy(rawData, (int)(long)dynstr.Offset, strTab, 0, (int)(long)dynstr.Size);
                }

                int entSize = 16; // Elf64_Dyn size
                int count = (int)(dynamic.Size / (ulong)entSize);
                for (int i = 0; i < count; i++)
                {
                    reader.BaseStream.Seek((long)(dynamic.Offset + (ulong)(i * entSize)), SeekOrigin.Begin);
                    long tag = reader.ReadInt64();
                    ulong val = reader.ReadUInt64();

                    if (tag == 1) // DT_NEEDED
                    {
                        string lib = ReadStringFromTable(strTab, (uint)val);
                        if (!string.IsNullOrEmpty(lib))
                            NeededLibraries.Add(lib);
                    }
                    else if (tag == 0)
                        break;
                }
            }
        }

        private static string ReadStringFromTable(byte[] table, uint index)
        {
            if (table == null || index >= table.Length)
                return string.Empty;

            int end = (int)index;
            while (end < table.Length && table[end] != 0)
                end++;

            return System.Text.Encoding.UTF8.GetString(table, (int)index, end - (int)index);
        }
    }
}
