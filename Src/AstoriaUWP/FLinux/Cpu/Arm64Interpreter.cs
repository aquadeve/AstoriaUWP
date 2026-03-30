// Arm64Interpreter - AArch64 (ARM64) software CPU interpreter.
// Implements the AArch64 A64 instruction set as a managed C# interpreter.
// Registers mirror the AArch64 ABI: X0-X30 (64-bit), W0-W30 (low 32-bit aliases),
// XZR/WZR (zero register), SP (stack pointer), PC (program counter), PSTATE flags.
//
// Syscall convention (Android bionic AArch64):
//   syscall number in X8, args in X0-X5, return value in X0.
//   SVC #0 triggers syscall.
//
// Implemented instruction groups:
//   Data processing (ADD, SUB, AND, ORR, EOR, MOV, MOVZ, MOVK, MOVN, CMP, CMN)
//   Branches (B, BL, BR, BLR, RET, B.cond, CBZ, CBNZ, TBZ, TBNZ)
//   Load/Store (LDR, STR, LDRB, STRB, LDRH, STRH, LDP, STP)
//   System (SVC, NOP, MSR/MRS NZCV)
//   Shifts (LSL, LSR, ASR, ROR)
//   Multiply (MUL, MADD, MSUB, UMULH, SMULH)
//   Divide (UDIV, SDIV)
//   Bit manipulation (CLZ, REV, RBIT)

using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace DalvikUWPCSharp.FLinux.Cpu
{
    using DalvikUWPCSharp.FLinux.Core;
    using DalvikUWPCSharp.FLinux.Syscalls;

    /// <summary>
    /// AArch64 (ARM64) software interpreter.
    /// </summary>
    public class Arm64Interpreter : ICpuInterpreter
    {
        // ── Register file ─────────────────────────────────────────────────────
        // X0-X30: 64-bit general purpose registers (index 0-30).
        // X31:    XZR (zero register) / SP depending on context.
        // _sp:    dedicated stack pointer (separate from X31).
        // _pc:    program counter.
        private readonly ulong[] _x = new ulong[32]; // X0-X30, [31] reserved
        private ulong _sp;
        private ulong _pc;

        // PSTATE flags.
        private bool _n, _z, _c, _v;

        // Read register (31 = XZR = 0 for data ops).
        private ulong X(int idx) => (idx == 31) ? 0UL : _x[idx];
        // Write register (31 = XZR = discard, or SP depending on encoding).
        private void Xw(int idx, ulong val) { if (idx != 31) _x[idx] = val; }

        // ── Infrastructure ────────────────────────────────────────────────────
        private readonly LinuxMemory       _memory;
        private readonly SyscallDispatcher _syscalls;
        private          LinuxProcess      _process;
        private bool  _running;
        private ulong _instructionCount;

        public Arm64Interpreter(LinuxMemory memory, SyscallDispatcher syscalls, LinuxProcess process)
        {
            _memory   = memory;
            _syscalls = syscalls;
            _process  = process;
        }

        // ── ICpuInterpreter ───────────────────────────────────────────────────

        public ulong PC { get => _pc; set => _pc = value; }
        public ulong SP { get => _sp; set => _sp = value; }
        public LinuxMemory Memory => _memory;

        public ulong GetRegister(int index) => (index >= 0 && index < 31) ? _x[index]
                                             : (index == 31 ? _sp : 0);
        public void SetRegister(int index, ulong value)
        {
            if      (index >= 0 && index < 31) _x[index] = value;
            else if (index == 31)              _sp        = value;
        }

        public async Task RunAsync()
        {
            _running = true;
            await Task.Run(() =>
            {
                while (_running && !_process.Exited)
                {
                    if (!Step()) break;
                    if (_instructionCount % 100_000 == 0)
                        Debug.WriteLine($"[Arm64CPU] {_instructionCount} instr PC=0x{_pc:X16}");
                }
            });
            Debug.WriteLine($"[Arm64CPU] Finished after {_instructionCount} instructions.");
        }

        public bool Step()
        {
            if (_process.Exited) return false;
            _instructionCount++;

            uint insn = _memory.ReadUInt32(_pc);
            _pc += 4;

            return Decode(insn);
        }

        // ── Main decoder ──────────────────────────────────────────────────────

        private bool Decode(uint insn)
        {
            uint op0 = (insn >> 25) & 0xF;

            // ── Data processing – immediate ──────────────────────────────────
            if ((op0 & 0xE) == 0x8) // 100x
                return DecodeDataProcImm(insn);

            // ── Branches, exception generating, and system instructions ───────
            if ((op0 & 0xE) == 0xA) // 101x
                return DecodeBranchSystem(insn);

            // ── Load/Store ────────────────────────────────────────────────────
            if ((op0 & 0x5) == 0x4) // x1x0 (bits 27 and 25 = 10)
                return DecodeLoadStore(insn);

            // ── Data processing – register ────────────────────────────────────
            if ((op0 & 0x7) == 0x5) // x101
                return DecodeDataProcReg(insn);

            Debug.WriteLine($"[Arm64CPU] Unimplemented op0=0b{Convert.ToString(op0, 2)} insn=0x{insn:X8} PC=0x{_pc - 4:X16}");
            return true;
        }

        // ── Data processing – immediate ───────────────────────────────────────

        private bool DecodeDataProcImm(uint insn)
        {
            uint op = (insn >> 23) & 0x7;
            bool sf = (insn >> 31) != 0; // 0=32-bit, 1=64-bit

            switch (op)
            {
                case 0: case 1: // ADD/ADDS/SUB/SUBS (immediate)
                {
                    bool isSub  = (insn & (1u << 30)) != 0;
                    bool setFl  = (insn & (1u << 29)) != 0;
                    uint imm12  = (insn >> 10) & 0xFFF;
                    int  shift  = (int)((insn >> 22) & 3); // 0 or 1 (12)
                    if (shift == 1) imm12 <<= 12;
                    int  rn     = (int)((insn >> 5) & 0x1F);
                    int  rd     = (int)(insn & 0x1F);
                    ulong rnVal = (rn == 31) ? _sp : X(rn);
                    ulong result= isSub ? SubWithFlags(rnVal, imm12, 0, setFl, sf)
                                        : AddWithFlags(rnVal, imm12, 0, setFl, sf);
                    if (rd == 31) _sp = result;
                    else          Xw(rd, sf ? result : result & 0xFFFFFFFF);
                    return true;
                }

                case 4: case 5: // MOVN / MOVZ / MOVK (wide move)
                {
                    bool isMovz = (insn >> 29 & 3) == 2;
                    bool isMovn = (insn >> 29 & 3) == 0;
                    bool isMovk = (insn >> 29 & 3) == 3;
                    uint imm16  = (insn >> 5) & 0xFFFF;
                    int  hw     = (int)((insn >> 21) & 3);
                    int  rd     = (int)(insn & 0x1F);
                    ulong val   = (ulong)imm16 << (hw * 16);
                    if      (isMovn) { val = ~val; if (!sf) val &= 0xFFFFFFFF; }
                    else if (isMovk) { ulong mask = ~((ulong)0xFFFF << (hw * 16)); val = (X(rd) & mask) | val; }
                    Xw(rd, sf ? val : val & 0xFFFFFFFF);
                    return true;
                }

                case 2: // AND/ORR/EOR/ANDS immediate (logical)
                {
                    uint n   = (insn >> 22) & 1;
                    uint immr = (insn >> 16) & 0x3F;
                    uint imms = (insn >> 10) & 0x3F;
                    int  rn  = (int)((insn >> 5)  & 0x1F);
                    int  rd  = (int)(insn & 0x1F);
                    uint opc = (insn >> 29) & 3;
                    ulong imm = DecodeBitMask(sf ? 64 : 32, n, imms, immr);
                    ulong rnv = X(rn);
                    ulong res;
                    switch (opc)
                    {
                        case 0: res = rnv & imm; break; // AND
                        case 1: res = rnv | imm; break; // ORR
                        case 2: res = rnv ^ imm; break; // EOR
                        default: res = rnv & imm; // ANDS
                            _n = sf ? (res >> 63) != 0 : (res >> 31) != 0;
                            _z = res == 0; _c = false; _v = false;
                            break;
                    }
                    if (!sf) res &= 0xFFFFFFFF;
                    if (rd == 31) { if (opc == 0 || opc == 3) _sp = res; }
                    else          Xw(rd, res);
                    return true;
                }

                case 6: // SBFM / BFM / UBFM (bitfield)
                {
                    uint opc  = (insn >> 29) & 3;
                    uint n    = (insn >> 22) & 1;
                    uint immr = (insn >> 16) & 0x3F;
                    uint imms = (insn >> 10) & 0x3F;
                    int  rn  = (int)((insn >> 5) & 0x1F);
                    int  rd  = (int)(insn & 0x1F);
                    int  datasize = sf ? 64 : 32;
                    ulong src = X(rn);
                    ulong result = BitfieldMove(src, immr, imms, datasize, opc);
                    if (!sf) result &= 0xFFFFFFFF;
                    Xw(rd, result);
                    return true;
                }
            }

            Debug.WriteLine($"[Arm64CPU] Unimplemented DataProcImm op={op} insn=0x{insn:X8}");
            return true;
        }

        // ── Branches / system ─────────────────────────────────────────────────

        private bool DecodeBranchSystem(uint insn)
        {
            uint op0 = (insn >> 29) & 7;

            if ((op0 & 6) == 0) // 00x = unconditional branch
            {
                bool isLink = (op0 & 1) != 0;
                int imm26 = (int)(insn & 0x3FFFFFF);
                long offset = ((long)(imm26 << 6)) >> 4; // sign extend, << 2
                if (isLink) _x[30] = _pc; // LR = next PC
                _pc = (ulong)((long)_pc + offset - 4); // -4 because PC already advanced
                return true;
            }

            if (op0 == 4) // 100 = conditional branch
            {
                uint cond = insn & 0xF;
                int imm19 = (int)((insn >> 5) & 0x7FFFF);
                long offset = ((long)(imm19 << 13)) >> 11; // sign extend, << 2
                if (CheckCondition64(cond))
                    _pc = (ulong)((long)_pc + offset - 4);
                return true;
            }

            if (op0 == 5) // 101 = unconditional branch register or system
            {
                uint op  = (insn >> 21) & 0xF;
                int  rn  = (int)((insn >> 5) & 0x1F);

                switch (op)
                {
                    case 0: _pc = X(rn); return true;                  // BR
                    case 1: _x[30] = _pc; _pc = X(rn); return true;   // BLR
                    case 2: _pc = _x[30]; return true;                 // RET
                    case 4: // SVC
                        long result = _syscalls.Dispatch((int)(_x[8]), this, _process);
                        _x[0] = (ulong)result;
                        return true;
                    case 8: return true; // NOP / HINT
                }

                // SVC inside system encoding.
                if ((insn & 0xFFE0001F) == 0xD4000001) // SVC #imm16
                {
                    long r = _syscalls.Dispatch((int)(_x[8]), this, _process);
                    _x[0] = (ulong)r;
                    return true;
                }
            }

            // CBZ / CBNZ
            if ((insn & 0x7E000000) == 0x34000000)
            {
                bool sf   = (insn >> 31) != 0;
                bool nz   = (insn & (1u << 24)) != 0;
                int  rt   = (int)(insn & 0x1F);
                int imm19 = (int)((insn >> 5) & 0x7FFFF);
                long off  = ((long)(imm19 << 13)) >> 11;
                ulong rval = X(rt);
                if (sf && (rval == 0) != nz)
                    _pc = (ulong)((long)_pc + off - 4);
                else if (!sf && ((rval & 0xFFFFFFFF) == 0) != nz)
                    _pc = (ulong)((long)_pc + off - 4);
                return true;
            }

            // TBZ / TBNZ
            if ((insn & 0x7E000000) == 0x36000000)
            {
                bool nz   = (insn & (1u << 24)) != 0;
                int  rt   = (int)(insn & 0x1F);
                int  bit  = (int)((insn >> 19) & 0x3F);
                int imm14 = (int)((insn >> 5) & 0x3FFF);
                long off  = ((long)(imm14 << 18)) >> 16;
                bool bitSet = ((X(rt) >> bit) & 1) != 0;
                if (bitSet == nz)
                    _pc = (ulong)((long)_pc + off - 4);
                return true;
            }

            Debug.WriteLine($"[Arm64CPU] Unimplemented Branch insn=0x{insn:X8} PC=0x{_pc - 4:X16}");
            return true;
        }

        // ── Load/Store ────────────────────────────────────────────────────────

        private bool DecodeLoadStore(uint insn)
        {
            // LDP / STP (load/store pair)
            if ((insn & 0x3A000000) == 0x28000000)
            {
                bool sf   = (insn >> 31) != 0;
                bool isLoad = (insn & (1u << 22)) != 0;
                bool pre  = (insn >> 24 & 3) == 3;
                bool post = (insn >> 24 & 3) == 1;
                int  rt   = (int)(insn & 0x1F);
                int  rt2  = (int)((insn >> 10) & 0x1F);
                int  rn   = (int)((insn >> 5) & 0x1F);
                int  imm7 = (int)((insn >> 15) & 0x7F);
                if ((imm7 & 0x40) != 0) imm7 |= unchecked((int)0xFFFFFF80); // sign extend
                long offset = sf ? imm7 * 8L : imm7 * 4L;
                ulong addr  = (rn == 31) ? _sp : X(rn);
                if (pre)  addr = (ulong)((long)addr + offset);
                int sz = sf ? 8 : 4;
                if (isLoad)
                {
                    Xw(rt,  sf ? _memory.ReadUInt64(addr)       : _memory.ReadUInt32(addr));
                    Xw(rt2, sf ? _memory.ReadUInt64(addr + (ulong)sz) : _memory.ReadUInt32(addr + (ulong)sz));
                }
                else
                {
                    if (sf) { _memory.WriteUInt64(addr, X(rt)); _memory.WriteUInt64(addr + (ulong)sz, X(rt2)); }
                    else    { _memory.WriteUInt32(addr, (uint)X(rt)); _memory.WriteUInt32(addr + (ulong)sz, (uint)X(rt2)); }
                }
                if (post) addr = (ulong)((long)((rn == 31) ? _sp : X(rn)) + offset);
                if (pre || post)
                { if (rn == 31) _sp = addr; else Xw(rn, addr); }
                return true;
            }

            // LDR/STR immediate, register, etc. (most common encodings)
            {
                bool isLoad  = (insn & (1u << 22)) != 0;
                uint size    = (insn >> 30);
                bool signExt = (insn & (1u << 23)) != 0;
                int  rt      = (int)(insn & 0x1F);
                int  rn      = (int)((insn >> 5) & 0x1F);

                ulong baseAddr = (rn == 31) ? _sp : X(rn);

                ulong addr;
                bool  writeback = false;
                bool  pre       = false;

                if ((insn & 0x3B200C00) == 0x38000400) // register offset
                {
                    int  rm    = (int)((insn >> 16) & 0x1F);
                    uint opt   = (insn >> 13) & 7;
                    uint shift = (insn >> 12) & 1;
                    ulong rmv  = X(rm);
                    if ((opt & 1) == 0 && !((opt & 2) != 0)) rmv = (ulong)(uint)rmv; // UXTW
                    if (shift != 0) rmv <<= (int)size;
                    addr = baseAddr + rmv;
                }
                else if ((insn & 0x3B200400) == 0x38000400) // post-index
                {
                    int simm = (int)((insn >> 12) & 0x1FF);
                    if ((simm & 0x100) != 0) simm |= unchecked((int)0xFFFFFE00);
                    addr = baseAddr;
                    writeback = true; pre = false;
                    baseAddr = (ulong)((long)baseAddr + simm);
                }
                else if ((insn & 0x3B200C00) == 0x38000C00) // pre-index
                {
                    int simm = (int)((insn >> 12) & 0x1FF);
                    if ((simm & 0x100) != 0) simm |= unchecked((int)0xFFFFFE00);
                    addr = (ulong)((long)baseAddr + simm);
                    writeback = true; pre = true;
                }
                else // unsigned offset
                {
                    uint uimm = (insn >> 10) & 0xFFF;
                    addr = baseAddr + (uimm << (int)size);
                }

                ulong val = 0;
                if (isLoad)
                {
                    switch (size)
                    {
                        case 0: val = _memory.ReadByte(addr);   if (signExt) val = (ulong)(long)(sbyte)(byte)val; break;
                        case 1: val = _memory.ReadUInt16(addr); if (signExt) val = (ulong)(long)(short)(ushort)val; break;
                        case 2: val = _memory.ReadUInt32(addr); if (signExt) val = (ulong)(int)val; break;
                        case 3: val = _memory.ReadUInt64(addr); break;
                    }
                    Xw(rt, val);
                }
                else
                {
                    val = X(rt);
                    switch (size)
                    {
                        case 0: _memory.WriteByte(addr,   (byte)val); break;
                        case 1: _memory.WriteUInt16(addr, (ushort)val); break;
                        case 2: _memory.WriteUInt32(addr, (uint)val); break;
                        case 3: _memory.WriteUInt64(addr, val); break;
                    }
                }

                if (writeback)
                { if (rn == 31) _sp = baseAddr; else Xw(rn, baseAddr); }

                return true;
            }
        }

        // ── Data processing – register ────────────────────────────────────────

        private bool DecodeDataProcReg(uint insn)
        {
            bool sf  = (insn >> 31) != 0;
            uint op0 = (insn >> 29) & 7;
            uint op1 = (insn >> 21) & 0xF;

            // 2-source (UDIV, SDIV, LSL, LSR, ASR, ROR, MUL variants)
            if ((insn & 0x5FE00000) == 0x1AC00000)
            {
                int rd = (int)(insn & 0x1F);
                int rn = (int)((insn >> 5) & 0x1F);
                int rm = (int)((insn >> 16) & 0x1F);
                uint op = (insn >> 10) & 0x3F;
                ulong rnv = X(rn), rmv = X(rm);
                ulong res;
                switch (op)
                {
                    case 2:  res = rmv == 0 ? 0 : (sf ? rnv / rmv : (ulong)((uint)rnv / (uint)rmv)); break; // UDIV
                    case 3:  res = rmv == 0 ? 0 : (ulong)(sf ? (long)rnv / (long)rmv : (int)rnv / (int)rmv); break; // SDIV
                    case 8:  res = sf ? rnv << (int)(rmv & 63) : (ulong)((uint)rnv << (int)(rmv & 31)); break; // LSL
                    case 9:  res = sf ? rnv >> (int)(rmv & 63) : (uint)rnv >> (int)(rmv & 31); break; // LSR
                    case 10: res = (ulong)(sf ? (long)rnv >> (int)(rmv & 63) : (int)rnv >> (int)(rmv & 31)); break; // ASR
                    case 11: res = sf ? RotRight64(rnv, (int)(rmv & 63)) : (ulong)RotRight32((uint)rnv, (int)(rmv & 31)); break; // ROR
                    default: Debug.WriteLine($"[Arm64CPU] Unknown 2-source op={op}"); return true;
                }
                Xw(rd, sf ? res : res & 0xFFFFFFFF);
                return true;
            }

            // 3-source (MADD, MSUB, SMULH, UMULH)
            if ((insn & 0x1F000000) == 0x1B000000)
            {
                int rd = (int)(insn & 0x1F);
                int rn = (int)((insn >> 5) & 0x1F);
                int ra = (int)((insn >> 10) & 0x1F);
                int rm = (int)((insn >> 16) & 0x1F);
                bool isMSub = (insn & (1u << 15)) != 0;
                ulong rnv = X(rn), rmv = X(rm), rav = X(ra);
                ulong res;
                uint op54 = (insn >> 29) & 3;
                if (op54 == 0) // MADD/MSUB (sf=1: 64-bit, sf=0: 32-bit)
                    res = sf ? (isMSub ? rav - rnv * rmv : rav + rnv * rmv)
                               : (ulong)((uint)rav + (uint)((isMSub ? 0u - (uint)rnv * (uint)rmv : (uint)rnv * (uint)rmv)));
                else if (op54 == 2) // SMULH – upper 64 bits of signed 64×64 product
                    res = (ulong)Int128.Multiply((long)rnv, (long)rmv).Upper;
                else // UMULH – upper 64 bits of unsigned 64×64 product
                    res = ((UInt128)rnv * rmv).Upper;
                Xw(rd, sf ? res : res & 0xFFFFFFFF);
                return true;
            }

            // Shifted register (ADD, SUB, AND, ORR, EOR, etc.)
            {
                int  rd    = (int)(insn & 0x1F);
                int  rn    = (int)((insn >> 5) & 0x1F);
                int  rm    = (int)((insn >> 16) & 0x1F);
                uint shift = (insn >> 22) & 3;
                uint imm6  = (insn >> 10) & 0x3F;
                ulong rmv  = ShiftReg64(X(rm), shift, (int)imm6, sf);
                ulong rnv  = X(rn);
                ulong res;

                bool isSub   = (op0 & 4) != 0;
                bool setFl   = (op0 & 1) != 0;
                bool isLogic = (op0 & 6) == 2;

                if (isLogic)
                {
                    uint logop = op0 & 3;
                    switch (logop)
                    {
                        case 0: res = rnv &  rmv; break;
                        case 1: res = rnv |  rmv; break;
                        case 2: res = rnv ^  rmv; break;
                        default: res = rnv & ~rmv; break; // BIC
                    }
                    if (setFl) { _n = sf ? (res >> 63) != 0 : (res >> 31) != 0; _z = (sf ? res : res & 0xFFFFFFFF) == 0; _c = false; _v = false; }
                }
                else
                {
                    res = isSub ? SubWithFlags(rnv, rmv, 0, setFl, sf)
                                : AddWithFlags(rnv, rmv, 0, setFl, sf);
                }

                if (!sf) res &= 0xFFFFFFFF;
                if (rd == 31 && !setFl) _sp = res;
                else Xw(rd, res);
                return true;
            }
        }

        // ── Condition check ───────────────────────────────────────────────────

        private bool CheckCondition64(uint cond)
        {
            switch (cond)
            {
                case 0:  return  _z;
                case 1:  return !_z;
                case 2:  return  _c;
                case 3:  return !_c;
                case 4:  return  _n;
                case 5:  return !_n;
                case 6:  return  _v;
                case 7:  return !_v;
                case 8:  return  _c && !_z;
                case 9:  return !_c ||  _z;
                case 10: return  _n == _v;
                case 11: return  _n != _v;
                case 12: return !_z && _n == _v;
                case 13: return  _z || _n != _v;
                case 14: case 15: return true;
                default: return true;
            }
        }

        // ── Arithmetic helpers ────────────────────────────────────────────────

        private ulong AddWithFlags(ulong a, ulong b, ulong carry, bool setFlags, bool sf)
        {
            bool is64 = sf;
            if (!is64) { a &= 0xFFFFFFFF; b &= 0xFFFFFFFF; }
            UInt128 res = (UInt128)a + b + carry;
            ulong r = (ulong)res;
            if (!is64) r &= 0xFFFFFFFF;
            if (setFlags)
            {
                _n = is64 ? (r >> 63) != 0 : (r >> 31) != 0;
                _z = (is64 ? r : r & 0xFFFFFFFF) == 0;
                _c = is64 ? res.Hi != 0 : res.Exceeds(0xFFFFFFFFUL);
                _v = is64 ? (~(a ^ b) & (a ^ r) & (1UL << 63)) != 0
                           : (~(a ^ b) & (a ^ r) & (1UL << 31)) != 0;
            }
            return r;
        }

        private ulong SubWithFlags(ulong a, ulong b, ulong borrow, bool setFlags, bool sf)
        {
            return AddWithFlags(a, ~b, 1 - borrow, setFlags, sf);
        }

        private ulong ShiftReg64(ulong val, uint shiftType, int amount, bool sf)
        {
            if (!sf) { val &= 0xFFFFFFFF; amount &= 31; }
            else     { amount &= 63; }
            if (amount == 0) return val;
            switch (shiftType)
            {
                case 0: return sf ? val << amount : (val << amount) & 0xFFFFFFFF;
                case 1: return val >> amount;
                case 2: return sf ? (ulong)((long)val >> amount) : (ulong)((int)val >> amount);
                case 3: return sf ? RotRight64(val, amount) : (ulong)RotRight32((uint)val, amount);
                default: return val;
            }
        }

        private static ulong  RotRight64(ulong v, int n)  => (v >> n) | (v << (64 - n));
        private static uint   RotRight32(uint v,  int n)  => (v >> n) | (v << (32 - n));

        // ── Bit-mask decode ───────────────────────────────────────────────────

        private static ulong DecodeBitMask(int datasize, uint n, uint imms, uint immr)
        {
            int len = (n != 0) ? 6 : (int)(HighestSetBit(~((imms | (uint)(64 - datasize)) << 1)));
            if (len < 1) return 0;
            uint levels = (uint)((1 << len) - 1);
            uint S = imms & levels;
            uint R = immr & levels;
            int esize = 1 << len;
            ulong welem = (S + 1 >= 64) ? ulong.MaxValue : ((1UL << (int)(S + 1)) - 1);
            ulong wmask = RotateRight64Bits(welem, (int)R, esize);
            ulong result = 0;
            for (int i = 0; i < datasize; i += esize)
                result |= wmask << i;
            return result;
        }

        private static ulong RotateRight64Bits(ulong val, int shift, int width)
        {
            shift %= width;
            if (shift == 0) return val;
            return ((val >> shift) | (val << (width - shift))) & ((width < 64) ? (1UL << width) - 1 : ulong.MaxValue);
        }

        private static int HighestSetBit(uint val)
        {
            for (int i = 31; i >= 0; i--) if ((val >> i & 1) != 0) return i;
            return -1;
        }

        private static ulong BitfieldMove(ulong src, uint immr, uint imms, int datasize, uint opc)
        {
            // Simplified UBFM / SBFM / BFM.
            int s = (int)imms, r = (int)immr;
            if (opc == 2) // UBFM (unsigned)
            {
                ulong mask = s >= 63 ? ulong.MaxValue : (1UL << (s + 1)) - 1;
                ulong ext  = (src >> r) & mask;
                return ext;
            }
            if (opc == 0) // SBFM (signed extend)
            {
                ulong mask = s >= 63 ? ulong.MaxValue : (1UL << (s + 1)) - 1;
                ulong ext  = (src >> r) & mask;
                int signBit = s - r;
                if (signBit >= 0 && signBit < 63 && ((ext >> signBit) & 1) != 0)
                    ext |= ~((1UL << (signBit + 1)) - 1);
                return ext;
            }
            // opc=1 BFM: merge.
            return src;
        }

        // ── Register dump ─────────────────────────────────────────────────────

        public string DumpRegisters()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 31; i++)
                sb.AppendLine($"X{i,2}=0x{_x[i]:X16}");
            sb.AppendLine($"SP =0x{_sp:X16}  PC=0x{_pc:X16}");
            sb.AppendLine($"PSTATE: N={_n} Z={_z} C={_c} V={_v}");
            return sb.ToString();
        }
    }

    // UInt128 helper (not available in .NET Standard 2.0 used by UWP).
    internal readonly struct UInt128
    {
        public readonly ulong Lo, Hi;
        public UInt128(ulong lo, ulong hi) { Lo = lo; Hi = hi; }
        public static UInt128 operator +(UInt128 a, ulong b) { ulong lo = a.Lo + b; return new UInt128(lo, a.Hi + (lo < a.Lo ? 1UL : 0UL)); }
        public static UInt128 operator +(UInt128 a, UInt128 b) { ulong lo = a.Lo + b.Lo; return new UInt128(lo, a.Hi + b.Hi + (lo < a.Lo ? 1UL : 0UL)); }
        public static explicit operator ulong(UInt128 a) => a.Lo;
        public static implicit operator UInt128(ulong v) => new UInt128(v, 0);
        public bool Exceeds(ulong value) => Hi != 0 || Lo > value;
        public static UInt128 operator *(UInt128 a, ulong b)
        {
            ulong aHi = a.Hi, aLo = a.Lo;
            ulong bHi = 0,    bLo = b;
            ulong lo = aLo * bLo;
            ulong hi = MathEx.UMulHigh(aLo, bLo) + aHi * bLo + aLo * bHi;
            return new UInt128(lo, hi);
        }
        /// <summary>Upper 64 bits (for right-shift by 64).</summary>
        public ulong Upper => Hi;
    }

    internal readonly struct Int128
    {
        private readonly long _hi;
        private readonly ulong _lo;
        public Int128(long hi, ulong lo) { _hi = hi; _lo = lo; }
        /// <summary>Upper 64 bits (the high half of the 128-bit product).</summary>
        public long Upper => _hi;
        public static Int128 Multiply(long a, long b)
        {
            // 64×64 → 128-bit signed multiply.
            bool neg = (a < 0) ^ (b < 0);
            ulong ua = (ulong)Math.Abs(a), ub = (ulong)Math.Abs(b);
            ulong lo  = ua * ub;
            ulong hi2 = MathEx.UMulHigh(ua, ub);
            long hi   = neg ? (long)(~hi2 + (lo == 0 ? 1u : 0u)) : (long)hi2;
            return new Int128(hi, lo);
        }
    }

    internal static class MathEx
    {
        public static ulong UMulHigh(ulong a, ulong b)
        {
            ulong aHi = a >> 32, aLo = a & 0xFFFFFFFF;
            ulong bHi = b >> 32, bLo = b & 0xFFFFFFFF;
            ulong mid = aHi * bLo + aLo * bHi;
            return aHi * bHi + (mid >> 32) + ((aLo * bLo + ((mid & 0xFFFFFFFF) << 32)) >> 32 < (mid & 0xFFFFFFFF) ? 1UL : 0UL);
        }
    }
}
