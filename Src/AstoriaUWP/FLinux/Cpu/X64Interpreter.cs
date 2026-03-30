// X64Interpreter - x86-64 software CPU interpreter.
// Implements a subset of the x86-64 instruction set in managed C# for running
// x64 Android ELF binaries on AstoriaUWP without requiring native execution.
//
// Syscall convention (Linux x86-64 ABI):
//   syscall number in RAX, args in RDI, RSI, RDX, R10, R8, R9.
//   Return value in RAX.  SYSCALL instruction triggers dispatch.
//
// Implemented instruction groups (REX-prefix + ModRM decoded):
//   MOV (r/m64, r64 / imm), MOVZX, MOVSX
//   ADD, SUB, AND, OR, XOR, NOT, NEG, CMP, TEST
//   IMUL, IDIV
//   SHL, SHR, SAR, ROL, ROR
//   PUSH, POP
//   CALL, RET, JMP (near), Jcc (all conditions)
//   LEA
//   SYSCALL, NOP, LEAVE, INT 3
//
// Registers mirror FLinux src/platform/x64/context.h (syscall_context).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace DalvikUWPCSharp.FLinux.Cpu
{
    using DalvikUWPCSharp.FLinux.Core;
    using DalvikUWPCSharp.FLinux.Syscalls;

    /// <summary>
    /// x86-64 software interpreter.
    /// </summary>
    public class X64Interpreter : ICpuInterpreter
    {
        private struct ModRmDecodeResult
        {
            public int Reg;
            public int Rm;
            public long Disp;
        }

        // ── Register file ─────────────────────────────────────────────────────
        // Index mapping (matches AMD64 REG field):
        //  0=RAX 1=RCX 2=RDX 3=RBX 4=RSP 5=RBP 6=RSI 7=RDI
        //  8=R8  9=R9  10=R10 11=R11 12=R12 13=R13 14=R14 15=R15
        private readonly ulong[] _reg = new ulong[16];
        private ulong _rip;
        private ulong _rflags;

        // Flag bits in RFLAGS.
        private bool CF  { get => (_rflags & 1) != 0;    set => SetFlag(0,  value); }
        private bool PF  { get => (_rflags & 4) != 0;    set => SetFlag(2,  value); }
        private bool AF  { get => (_rflags & 16) != 0;   set => SetFlag(4,  value); }
        private bool ZF  { get => (_rflags & 64) != 0;   set => SetFlag(6,  value); }
        private bool SF  { get => (_rflags & 128) != 0;  set => SetFlag(7,  value); }
        private bool OF  { get => (_rflags & 0x800) != 0;set => SetFlag(11, value); }
        private void SetFlag(int bit, bool v) { if (v) _rflags |= (1UL << bit); else _rflags &= ~(1UL << bit); }

        // Convenience register aliases.
        private ulong RAX { get => _reg[0]; set => _reg[0] = value; }
        private ulong RCX { get => _reg[1]; set => _reg[1] = value; }
        private ulong RDX { get => _reg[2]; set => _reg[2] = value; }
        private ulong RBX { get => _reg[3]; set => _reg[3] = value; }
        private ulong RSP { get => _reg[4]; set => _reg[4] = value; }
        private ulong RBP { get => _reg[5]; set => _reg[5] = value; }
        private ulong RSI { get => _reg[6]; set => _reg[6] = value; }
        private ulong RDI { get => _reg[7]; set => _reg[7] = value; }

        // ── Infrastructure ────────────────────────────────────────────────────
        private readonly LinuxMemory       _memory;
        private readonly SyscallDispatcher _syscalls;
        private          LinuxProcess      _process;
        private bool  _running;
        private ulong _instructionCount;

        // Simple "fetch byte at RIP and advance" helper.
        private byte Fetch()  { return _memory.ReadByte(_rip++); }
        private ushort Fetch16() { ushort v = _memory.ReadUInt16(_rip); _rip += 2; return v; }
        private uint   Fetch32() { uint   v = _memory.ReadUInt32(_rip); _rip += 4; return v; }
        private ulong  Fetch64() { ulong  v = _memory.ReadUInt64(_rip); _rip += 8; return v; }

        public X64Interpreter(LinuxMemory memory, SyscallDispatcher syscalls, LinuxProcess process)
        {
            _memory   = memory;
            _syscalls = syscalls;
            _process  = process;
        }

        // ── ICpuInterpreter ───────────────────────────────────────────────────

        public ulong PC { get => _rip; set => _rip = value; }
        public ulong SP { get => RSP;  set => RSP  = value; }
        public LinuxMemory Memory => _memory;

        public ulong GetRegister(int index) => (index >= 0 && index < 16) ? _reg[index] : 0;
        public void SetRegister(int index, ulong value) { if (index >= 0 && index < 16) _reg[index] = value; }

        public async Task RunAsync()
        {
            _running = true;
            await Task.Run(() =>
            {
                while (_running && !_process.Exited)
                {
                    if (!Step()) break;
                    if (_instructionCount % 100_000 == 0)
                        Debug.WriteLine($"[X64CPU] {_instructionCount} instr RIP=0x{_rip:X16}");
                }
            });
            Debug.WriteLine($"[X64CPU] Finished after {_instructionCount} instructions.");
        }

        public bool Step()
        {
            if (_process.Exited) return false;
            _instructionCount++;

            ulong ip = _rip;
            bool rex_w = false, rex_r = false, rex_x = false, rex_b = false;
            bool prefix66 = false; // operand-size override (16-bit)
            bool prefix67 = false; // address-size override

            // Consume legacy prefixes.
            while (true)
            {
                byte b = _memory.ReadByte(_rip);
                if (b == 0x66) { prefix66 = true; _rip++; }
                else if (b == 0x67) { prefix67 = true; _rip++; }
                else if (b == 0xF0 || b == 0xF2 || b == 0xF3) { _rip++; } // LOCK / REPNE / REP
                else if (b >= 0x40 && b <= 0x4F) // REX prefix
                {
                    rex_w = (b & 8) != 0;
                    rex_r = (b & 4) != 0;
                    rex_x = (b & 2) != 0;
                    rex_b = (b & 1) != 0;
                    _rip++;
                }
                else break;
            }

            byte opcode = Fetch();
            int  operandSize = rex_w ? 8 : (prefix66 ? 2 : 4); // default 32-bit

            return ExecuteOpcode(opcode, rex_w, rex_r, rex_b, rex_x, operandSize);
        }

        private bool ExecuteOpcode(byte op, bool rexW, bool rexR, bool rexB, bool rexX, int opsz)
        {
            switch (op)
            {
                // ── NOP ──────────────────────────────────────────────────────
                case 0x90: return true;

                // ── PUSH ─────────────────────────────────────────────────────
                case 0x50: case 0x51: case 0x52: case 0x53:
                case 0x54: case 0x55: case 0x56: case 0x57:
                {
                    int reg = (op - 0x50) | (rexB ? 8 : 0);
                    Push64(_reg[reg]);
                    return true;
                }
                // ── POP ──────────────────────────────────────────────────────
                case 0x58: case 0x59: case 0x5A: case 0x5B:
                case 0x5C: case 0x5D: case 0x5E: case 0x5F:
                {
                    int reg = (op - 0x58) | (rexB ? 8 : 0);
                    _reg[reg] = Pop64();
                    return true;
                }

                // ── MOV reg,imm64 (REX.W + B8+rd) ────────────────────────────
                case 0xB8: case 0xB9: case 0xBA: case 0xBB:
                case 0xBC: case 0xBD: case 0xBE: case 0xBF:
                {
                    int reg = (op - 0xB8) | (rexB ? 8 : 0);
                    _reg[reg] = rexW ? Fetch64() : Fetch32();
                    return true;
                }

                // ── LEAVE ─────────────────────────────────────────────────────
                case 0xC9: RSP = RBP; RBP = Pop64(); return true;

                // ── RET near ──────────────────────────────────────────────────
                case 0xC3: _rip = Pop64(); return true;
                case 0xC2: { ushort n = Fetch16(); _rip = Pop64(); RSP += n; return true; }

                // ── CALL rel32 ────────────────────────────────────────────────
                case 0xE8:
                {
                    int rel32 = (int)Fetch32();
                    Push64(_rip);
                    _rip = (ulong)((long)_rip + rel32);
                    return true;
                }

                // ── JMP rel32 ─────────────────────────────────────────────────
                case 0xE9:
                {
                    int rel32 = (int)Fetch32();
                    _rip = (ulong)((long)_rip + rel32);
                    return true;
                }

                // ── JMP rel8 ──────────────────────────────────────────────────
                case 0xEB:
                {
                    sbyte rel8 = (sbyte)Fetch();
                    _rip = (ulong)((long)_rip + rel8);
                    return true;
                }

                // ── SYSCALL ───────────────────────────────────────────────────
                case 0x0F:
                {
                    byte op2 = Fetch();
                    if (op2 == 0x05) // SYSCALL
                    {
                        long result = _syscalls.Dispatch((int)RAX, this, _process);
                        RAX = (ulong)result;
                        return true;
                    }
                    if (op2 == 0x1F) // NOP r/m (multi-byte NOP)
                    {
                        _ = Fetch(); // skip ModRM
                        return true;
                    }
                    // Jcc rel32 (0F 80..8F)
                    if (op2 >= 0x80 && op2 <= 0x8F)
                    {
                        int rel32 = (int)Fetch32();
                        if (CheckCondition64((byte)(op2 - 0x80)))
                            _rip = (ulong)((long)_rip + rel32);
                        return true;
                    }
                    // MOVZX / MOVSX
                    if (op2 == 0xB6 || op2 == 0xBE || op2 == 0xB7 || op2 == 0xBF)
                    {
                        var modRm = DecodeModRM(rexR, rexB, rexX);
                        int rd = modRm.Reg;
                        int rm = modRm.Rm;
                        long disp = modRm.Disp;
                        // 0xB6/0xBE: source is byte (8-bit); 0xB7/0xBF: source is word (16-bit).
                        int srcSz = (op2 == 0xB6 || op2 == 0xBE) ? 1 : 2;
                        ulong val = ReadRmOrMem(rm, disp, srcSz);
                        bool sign = op2 == 0xBE || op2 == 0xBF;
                        if (sign) val = (srcSz == 1) ? (ulong)(long)(sbyte)(byte)val
                                                     : (ulong)(long)(short)(ushort)val;
                        WriteReg(rd, rexW ? val : val & 0xFFFFFFFF, rexW);
                        return true;
                    }
                    Debug.WriteLine($"[X64CPU] Unimplemented 0F {op2:X2} at RIP=0x{_rip:X16}");
                    return true;
                }

                // ── MOV r/m, r  and  MOV r, r/m ─────────────────────────────
                case 0x88: case 0x89: case 0x8A: case 0x8B:
                {
                    bool isStore = (op & 2) == 0;
                    int  sz      = (op & 1) == 0 ? 1 : opsz;
                    var modRm = DecodeModRM(rexR, rexB, rexX);
                    int rd = modRm.Reg;
                    int rm = modRm.Rm;
                    long disp = modRm.Disp;
                    if (isStore)
                        WriteMemOrRm(rm, disp, ReadReg(rd, sz), sz);
                    else
                    {
                        ulong v = ReadRmOrMem(rm, disp, sz);
                        WriteReg(rd, rexW ? v : v & 0xFFFFFFFF, rexW);
                    }
                    return true;
                }

                // ── MOV r/m, imm ─────────────────────────────────────────────
                case 0xC6: case 0xC7:
                {
                    int sz = (op == 0xC6) ? 1 : opsz;
                    var modRm = DecodeModRM(false, rexB, rexX);
                    int rm = modRm.Rm;
                    long disp = modRm.Disp;
                    ulong imm = rexW ? Fetch32() : (op == 0xC6 ? Fetch() : Fetch32());
                    WriteMemOrRm(rm, disp, imm, sz);
                    return true;
                }

                // ── ALU r/m8, r8  (00-05 group) ──────────────────────────────
                case 0x00: case 0x01: case 0x02: case 0x03: case 0x04: case 0x05:
                case 0x08: case 0x09: case 0x0A: case 0x0B: case 0x0C: case 0x0D:
                case 0x10: case 0x11: case 0x12: case 0x13: case 0x14: case 0x15:
                case 0x18: case 0x19: case 0x1A: case 0x1B: case 0x1C: case 0x1D:
                case 0x20: case 0x21: case 0x22: case 0x23: case 0x24: case 0x25:
                case 0x28: case 0x29: case 0x2A: case 0x2B: case 0x2C: case 0x2D:
                case 0x30: case 0x31: case 0x32: case 0x33: case 0x34: case 0x35:
                case 0x38: case 0x39: case 0x3A: case 0x3B: case 0x3C: case 0x3D:
                    return ExecuteAluOp(op, rexW, rexR, rexB, rexX, opsz);

                // ── Group 1 immediate ─────────────────────────────────────────
                case 0x80: case 0x81: case 0x83:
                    return ExecuteGroup1(op, rexW, rexR, rexB, rexX, opsz);

                // ── TEST r/m, reg ─────────────────────────────────────────────
                case 0x84: case 0x85:
                {
                    int sz = (op == 0x84) ? 1 : opsz;
                    var modRm = DecodeModRM(rexR, rexB, rexX);
                    int rd = modRm.Reg;
                    int rm = modRm.Rm;
                    long disp = modRm.Disp;
                    ulong a = ReadReg(rd, sz), b = ReadRmOrMem(rm, disp, sz);
                    SetLogicFlags(a & b, rexW ? 8 : sz);
                    return true;
                }

                // ── XCHG ─────────────────────────────────────────────────────
                case 0x86: case 0x87:
                {
                    int sz = (op == 0x86) ? 1 : opsz;
                    var modRm = DecodeModRM(rexR, rexB, rexX);
                    int rd = modRm.Reg;
                    int rm = modRm.Rm;
                    long disp = modRm.Disp;
                    ulong a = ReadReg(rd, sz), b = ReadRmOrMem(rm, disp, sz);
                    WriteReg(rd, b, rexW);
                    WriteMemOrRm(rm, disp, a, sz);
                    return true;
                }

                // ── LEA ──────────────────────────────────────────────────────
                case 0x8D:
                {
                    var modRm = DecodeModRM(rexR, rexB, rexX);
                    int rd = modRm.Reg;
                    int rm = modRm.Rm;
                    long disp = modRm.Disp;
                    ulong ea = CalcEA(rm, disp);
                    WriteReg(rd, ea, rexW);
                    return true;
                }

                // ── Jcc rel8 ─────────────────────────────────────────────────
                case 0x70: case 0x71: case 0x72: case 0x73:
                case 0x74: case 0x75: case 0x76: case 0x77:
                case 0x78: case 0x79: case 0x7A: case 0x7B:
                case 0x7C: case 0x7D: case 0x7E: case 0x7F:
                {
                    sbyte rel8 = (sbyte)Fetch();
                    if (CheckCondition64((byte)(op - 0x70)))
                        _rip = (ulong)((long)_rip + rel8);
                    return true;
                }

                // ── INC / DEC r/m ─────────────────────────────────────────────
                case 0xFE: case 0xFF:
                {
                    byte ext = _memory.ReadByte(_rip); // peek at /digit
                    int digit = (ext >> 3) & 7;
                    int sz = (op == 0xFE) ? 1 : opsz;
                    var modRm = DecodeModRM(false, rexB, rexX);
                    int rm = modRm.Rm;
                    long disp = modRm.Disp;
                    ulong v = ReadRmOrMem(rm, disp, sz);
                    if (digit == 0) { ulong r = Add64(v, 1, sz, updateCF: false); WriteMemOrRm(rm, disp, r, sz); }      // INC
                    else if (digit == 1) { ulong r = Sub64(v, 1, sz, updateCF: false); WriteMemOrRm(rm, disp, r, sz); } // DEC
                    else if (digit == 2) { Push64(_rip); _rip = ReadRmOrMem(rm, disp, 8); } // CALL r/m64
                    else if (digit == 4) { _rip = ReadRmOrMem(rm, disp, 8); }               // JMP r/m64
                    else if (digit == 6) { Push64(ReadRmOrMem(rm, disp, sz)); }             // PUSH r/m
                    return true;
                }

                // ── INT 3 (breakpoint) ────────────────────────────────────────
                case 0xCC: Debug.WriteLine($"[X64CPU] INT3 at RIP=0x{_rip:X16}"); return true;

                default:
                    Debug.WriteLine($"[X64CPU] Unimplemented opcode 0x{op:X2} at RIP=0x{_rip-1:X16}");
                    return true;
            }
        }

        // ── ALU helpers ───────────────────────────────────────────────────────

        private bool ExecuteAluOp(byte op, bool rexW, bool rexR, bool rexB, bool rexX, int opsz)
        {
            int aluOp = op >> 3;  // 0=ADD 1=OR 2=ADC 3=SBB 4=AND 5=SUB 6=XOR 7=CMP
            bool dir    = (op & 2) != 0;  // true = r,r/m (else r/m,r)
            bool isImm  = (op & 5) == 4;  // short-form r/m=RAX, imm

            ulong a, b; int sz; int dst = -1; long disp = 0;

            if (isImm)
            {
                sz = (op & 1) == 0 ? 1 : opsz;
                a  = _reg[0]; // RAX
                b  = rexW ? (uint)Fetch32() : (op & 1) == 0 ? Fetch() : Fetch32();
                dst = 0;
            }
            else
            {
                sz = (op & 1) == 0 ? 1 : opsz;
                var modRm = DecodeModRM(rexR, rexB, rexX);
                int rd = modRm.Reg;
                int rm = modRm.Rm;
                long d = modRm.Disp;
                disp = d;
                if (dir)  { a = ReadReg(rd, sz); b = ReadRmOrMem(rm, disp, sz); dst = rd; }
                else      { a = ReadRmOrMem(rm, disp, sz); b = ReadReg(rd, sz); dst = ~rm; }
            }

            ulong result;
            switch (aluOp)
            {
                case 0: result = Add64(a, b, sz); break;
                case 1: result = a | b;  SetLogicFlags(result, sz); break;
                case 2: result = Add64(a, b + (CF ? 1u : 0u), sz); break;
                case 3: result = Sub64(a, b + (CF ? 1u : 0u), sz); break;
                case 4: result = a & b;  SetLogicFlags(result, sz); break;
                case 5: result = Sub64(a, b, sz); break;
                case 6: result = a ^ b;  SetLogicFlags(result, sz); break;
                case 7: _ = Sub64(a, b, sz); return true; // CMP – no writeback
                default: return true;
            }

            if (dst >= 0)  WriteReg(dst, rexW ? result : result & 0xFFFFFFFF, rexW);
            else           WriteMemOrRm(~dst, disp, result, sz);
            return true;
        }

        private bool ExecuteGroup1(byte op, bool rexW, bool rexR, bool rexB, bool rexX, int opsz)
        {
            int sz = (op == 0x80) ? 1 : opsz;
            var modRm = DecodeModRM(false, rexB, rexX);
            int rm = modRm.Rm;
            long disp = modRm.Disp;
            int digit = modRm.Reg & 7;
            ulong imm;
            if (op == 0x83) { sbyte s8 = (sbyte)Fetch(); imm = (ulong)(long)s8; }
            else if (op == 0x80) { imm = Fetch(); }
            else imm = rexW ? Fetch32() : Fetch32();
            ulong a = ReadRmOrMem(rm, disp, sz);
            ulong result;
            switch (digit)
            {
                case 0: result = Add64(a, imm, sz); break;
                case 1: result = a | imm; SetLogicFlags(result, sz); break;
                case 2: result = Add64(a, imm + (CF ? 1u : 0u), sz); break;
                case 3: result = Sub64(a, imm + (CF ? 1u : 0u), sz); break;
                case 4: result = a & imm; SetLogicFlags(result, sz); break;
                case 5: result = Sub64(a, imm, sz); break;
                case 6: result = a ^ imm; SetLogicFlags(result, sz); break;
                case 7: _ = Sub64(a, imm, sz); return true; // CMP
                default: return true;
            }
            WriteMemOrRm(rm, disp, result, sz);
            return true;
        }

        // ── Condition checking ────────────────────────────────────────────────

        private bool CheckCondition64(byte cond)
        {
            switch (cond)
            {
                case 0:  return  OF;          // O
                case 1:  return !OF;          // NO
                case 2:  return  CF;          // B/NAE
                case 3:  return !CF;          // AE/NB
                case 4:  return  ZF;          // E/Z
                case 5:  return !ZF;          // NE/NZ
                case 6:  return  CF || ZF;    // BE/NA
                case 7:  return !CF && !ZF;   // A/NBE
                case 8:  return  SF;          // S
                case 9:  return !SF;          // NS
                case 10: return  PF;          // P/PE
                case 11: return !PF;          // NP/PO
                case 12: return  SF != OF;    // L/NGE
                case 13: return  SF == OF;    // GE/NL
                case 14: return  ZF || SF != OF; // LE/NG
                case 15: return !ZF && SF == OF; // G/NLE
                default: return true;
            }
        }

        // ── Arithmetic ────────────────────────────────────────────────────────

        private ulong Add64(ulong a, ulong b, int sz, bool updateCF = true)
        {
            ulong result = a + b;
            ulong mask = (sz >= 8) ? ulong.MaxValue : (1UL << (sz * 8)) - 1;
            result &= mask; a &= mask; b &= mask;
            ZF = result == 0; SF = (result >> (sz * 8 - 1) & 1) != 0;
            if (updateCF) CF = result < a;
            OF = ((~(a ^ b) & (a ^ result)) >> (sz * 8 - 1) & 1) != 0;
            return result;
        }

        private ulong Sub64(ulong a, ulong b, int sz, bool updateCF = true)
        {
            return Add64(a, (~b & ((sz >= 8) ? ulong.MaxValue : (1UL << sz * 8) - 1)) + 1, sz, updateCF);
        }

        private void SetLogicFlags(ulong result, int sz)
        {
            ulong mask = (sz >= 8) ? ulong.MaxValue : (1UL << (sz * 8)) - 1;
            result &= mask;
            ZF = result == 0; SF = (result >> (sz * 8 - 1) & 1) != 0;
            CF = false; OF = false;
        }

        // ── ModRM / SIB decode ────────────────────────────────────────────────

        private ModRmDecodeResult DecodeModRM(bool rexR, bool rexB, bool rexX)
        {
            byte modrm = Fetch();
            int mod = modrm >> 6;
            int reg = ((modrm >> 3) & 7) | (rexR ? 8 : 0);
            int rm  = (modrm & 7)       | (rexB ? 8 : 0);

            long disp = 0;

            if (mod == 3) // register-direct
                return new ModRmDecodeResult { Reg = reg, Rm = rm, Disp = 0 };

            // SIB byte?
            bool hasSib = (modrm & 7) == 4;
            int sib_base = 0, sib_index = 0, sib_scale = 0;
            if (hasSib)
            {
                byte sib = Fetch();
                sib_base  = (sib & 7)       | (rexB ? 8 : 0);
                sib_index = ((sib >> 3) & 7) | (rexX ? 8 : 0);
                sib_scale = sib >> 6;
            }

            if (mod == 1) disp = (sbyte)Fetch();
            else if (mod == 2) disp = (int)Fetch32();
            else if (mod == 0 && (modrm & 7) == 5) { disp = (int)Fetch32(); rm = -1; } // RIP-relative

            if (hasSib)
                rm = -(sib_base * 1000 + sib_index * 10 + sib_scale + 1); // encode SIB

            return new ModRmDecodeResult { Reg = reg, Rm = rm, Disp = disp };
        }

        private ulong CalcEA(int rm, long disp)
        {
            if (rm < 0)
            {
                // RIP-relative
                if (rm == -1) return (ulong)((long)_rip + disp);
                // SIB encoding: -(base*1000 + index*10 + scale + 1)
                int encoded = -(rm + 1);
                int base_  = encoded / 1000;
                int index_ = (encoded % 1000) / 10;
                int scale_ = encoded % 10;
                ulong b = base_ == 5 ? RSP : _reg[base_];
                ulong i = index_ == 4 ? 0 : _reg[index_];
                return (ulong)((long)(b + (i << scale_)) + disp);
            }
            return (ulong)((long)_reg[rm] + disp);
        }

        private ulong ReadRmOrMem(int rm, long disp, int sz)
        {
            if (rm >= 0 && rm < 16 && disp == 0) return ReadReg(rm, sz); // register direct
            ulong addr = CalcEA(rm, disp);
            switch (sz)
            {
                case 1: return _memory.ReadByte(addr);
                case 2: return _memory.ReadUInt16(addr);
                case 4: return _memory.ReadUInt32(addr);
                case 8: return _memory.ReadUInt64(addr);
                default: return _memory.ReadUInt32(addr);
            }
        }

        private void WriteMemOrRm(int rm, long disp, ulong val, int sz)
        {
            if (rm >= 0 && rm < 16 && disp == 0) { WriteReg(rm, val, sz == 8); return; }
            ulong addr = CalcEA(rm, disp);
            switch (sz)
            {
                case 1: _memory.WriteByte(addr,   (byte)val);  break;
                case 2: _memory.WriteUInt16(addr, (ushort)val); break;
                case 4: _memory.WriteUInt32(addr, (uint)val);  break;
                case 8: _memory.WriteUInt64(addr, val);         break;
            }
        }

        private ulong ReadReg(int reg, int sz)
        {
            ulong v = (reg >= 0 && reg < 16) ? _reg[reg] : 0;
            switch (sz)
            {
                case 1: return v & 0xFF;
                case 2: return v & 0xFFFF;
                case 4: return v & 0xFFFFFFFF;
                default: return v;
            }
        }

        private void WriteReg(int reg, ulong val, bool rexW)
        {
            if (reg < 0 || reg >= 16) return;
            if (rexW) _reg[reg] = val;
            else      _reg[reg] = val & 0xFFFFFFFF; // 32-bit write zero-extends
        }

        private void Push64(ulong val) { RSP -= 8; _memory.WriteUInt64(RSP, val); }
        private ulong Pop64() { ulong v = _memory.ReadUInt64(RSP); RSP += 8; return v; }

        // ── Register dump ─────────────────────────────────────────────────────

        public string DumpRegisters()
        {
            string[] names = { "RAX","RCX","RDX","RBX","RSP","RBP","RSI","RDI",
                               "R8 ","R9 ","R10","R11","R12","R13","R14","R15" };
            var sb = new StringBuilder();
            for (int i = 0; i < 16; i++)
                sb.Append($"{names[i]}=0x{_reg[i]:X16}  ");
            sb.AppendLine();
            sb.Append($"RIP=0x{_rip:X16}  RFLAGS=0x{_rflags:X16}  CF={CF} ZF={ZF} SF={SF} OF={OF}");
            return sb.ToString();
        }
    }
}
