// ArmInterpreter - ARM32 / Thumb software CPU interpreter.
// Converts FLinux src/platform/arm/context.h and Android bionic ARM ABI into a
// managed C# ARM instruction decoder and executor.
//
// Supported instruction groups (ARM32 state):
//   Data-processing (AND, EOR, SUB, RSB, ADD, ADC, SBC, RSC, TST, TEQ, CMP, CMN,
//                   ORR, MOV, BIC, MVN) with all shift modes
//   Branch (B, BL), Branch-Exchange (BX, BLX)
//   Load/Store word and byte (LDR, STR, LDRB, STRB)
//   Load/Store multiple (LDM, STM – full descending / full ascending)
//   Multiply (MUL, MLA, UMULL, SMULL, UMLAL, SMLAL)
//   Software Interrupt / Supervisor Call (SVC #0 → syscall)
//   MSR/MRS (CPSR access)
//   CLZ, NOP
//
// Thumb-16 decoding is provided for the most common opcodes.
// Unimplemented opcodes are logged and skipped.

using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace DalvikUWPCSharp.FLinux.Cpu
{
    using DalvikUWPCSharp.FLinux.Core;
    using DalvikUWPCSharp.FLinux.Syscalls;

    /// <summary>
    /// ARM32 software interpreter.
    /// Register layout mirrors FLinux src/platform/arm/context.h (syscall_context).
    ///   R0–R12  : general purpose
    ///   R13     : SP (stack pointer)
    ///   R14     : LR (link register)
    ///   R15     : PC (program counter) – always points to *current* instruction + 8
    /// </summary>
    public class ArmInterpreter : ICpuInterpreter
    {
        // ── Register file ────────────────────────────────────────────────────
        private readonly uint[] _r = new uint[16]; // R0–R15
        private uint _cpsr;       // Current Program Status Register

        // Aliases for readability.
        private ref uint SP_ref  => ref _r[13];
        private ref uint LR_ref  => ref _r[14];
        private ref uint PC_ref  => ref _r[15];

        // CPSR flag bits.
        private bool N { get => (_cpsr >> 31 & 1) == 1; set => SetCpsrBit(31, value); }
        private bool Z { get => (_cpsr >> 30 & 1) == 1; set => SetCpsrBit(30, value); }
        private bool C { get => (_cpsr >> 29 & 1) == 1; set => SetCpsrBit(29, value); }
        private bool V { get => (_cpsr >> 28 & 1) == 1; set => SetCpsrBit(28, value); }
        private bool T { get => (_cpsr >>  5 & 1) == 1; set => SetCpsrBit(5, value); }  // Thumb mode

        private void SetCpsrBit(int bit, bool val)
        {
            if (val) _cpsr |=  (1u << bit);
            else     _cpsr &= ~(1u << bit);
        }

        // ── Infrastructure ───────────────────────────────────────────────────
        private readonly LinuxMemory    _memory;
        private readonly SyscallDispatcher _syscalls;
        private          LinuxProcess   _process;
        private bool _running;
        private ulong _instructionCount;

        public ArmInterpreter(LinuxMemory memory, SyscallDispatcher syscalls, LinuxProcess process)
        {
            _memory   = memory;
            _syscalls = syscalls;
            _process  = process;
        }

        // ── ICpuInterpreter ──────────────────────────────────────────────────

        public ulong PC
        {
            get => PC_ref;
            set => PC_ref = (uint)value;
        }

        public ulong SP
        {
            get => SP_ref;
            set => SP_ref = (uint)value;
        }

        public LinuxMemory Memory => _memory;

        /// <summary>
        /// Read a register by index.
        /// Indices 0–15 map to R0–R15 (R13=SP, R14=LR, R15=PC).
        /// Index 16 is a pseudo-register that reads the CPSR.
        /// </summary>
        public ulong GetRegister(int index)
        {
            if (index >= 0 && index < 16) return _r[index];
            if (index == 16) return _cpsr; // CPSR pseudo-register
            return 0;
        }

        /// <summary>
        /// Write a register by index.
        /// Indices 0–15 map to R0–R15.
        /// Index 16 writes the CPSR (used by ElfExecutor to set Thumb mode before entry).
        /// </summary>
        public void SetRegister(int index, ulong value)
        {
            if (index >= 0 && index < 16)  _r[index] = (uint)value;
            else if (index == 16)          _cpsr      = (uint)value;
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
                        Debug.WriteLine($"[ArmCPU] {_instructionCount} instructions, PC=0x{PC:X8}");
                }
            });
            Debug.WriteLine($"[ArmCPU] Execution finished after {_instructionCount} instructions.");
        }

        public bool Step()
        {
            if (_process.Exited) return false;
            _instructionCount++;

            if (T) // Thumb-16 mode
                return ExecuteThumb();
            else
                return ExecuteArm();
        }

        // ── ARM32 instruction execution ──────────────────────────────────────

        private bool ExecuteArm()
        {
            uint pc = PC_ref;
            uint insn = _memory.ReadUInt32(pc);
            PC_ref = pc + 4; // pre-increment (ARM always adds 4 before execute)

            // Condition check.
            uint cond = insn >> 28;
            if (!CheckCondition(cond))
                return true; // predicated-out, skip

            uint bits27_20 = (insn >> 20) & 0xFF;
            uint bits7_4   = (insn >>  4) & 0xF;

            // ── Branch instructions ──────────────────────────────────────────
            if ((insn & 0x0E000000) == 0x0A000000)
            {
                bool isLink = (insn & 0x01000000) != 0;
                int  offset = (int)(insn << 8) >> 6; // sign-extend and << 2
                if (isLink) LR_ref = PC_ref; // LR = instruction after branch (PC already incremented by 4)
                PC_ref = (uint)(PC_ref + (uint)offset);
                return true;
            }

            // ── BX / BLX (branch and exchange) ──────────────────────────────
            if ((insn & 0x0FFFFFF0) == 0x012FFF10) // BX
            {
                uint rn = _r[insn & 0xF];
                T      = (rn & 1) != 0;
                PC_ref = rn & ~1u;
                return true;
            }
            if ((insn & 0x0FFFFFF0) == 0x012FFF30) // BLX reg
            {
                uint rn = _r[insn & 0xF];
                LR_ref = PC_ref;
                T      = (rn & 1) != 0;
                PC_ref = rn & ~1u;
                return true;
            }

            // ── SVC (syscall) ────────────────────────────────────────────────
            if ((insn & 0x0F000000) == 0x0F000000)
            {
                // Syscall number in R7; arguments in R0–R6.
                uint sysno = _r[7];
                long result = _syscalls.Dispatch((int)sysno, this, _process);
                _r[0] = (uint)(int)result; // return value in R0
                return true;
            }

            // ── Data-processing (most common form) ──────────────────────────
            if ((insn & 0x0C000000) == 0x00000000)
            {
                return ExecuteDataProcessing(insn);
            }

            // ── Load/Store single ─────────────────────────────────────────────
            if ((insn & 0x0C000000) == 0x04000000)
            {
                return ExecuteLoadStore(insn);
            }

            // ── Load/Store multiple ───────────────────────────────────────────
            if ((insn & 0x0E000000) == 0x08000000)
            {
                return ExecuteLoadStoreMultiple(insn);
            }

            // ── Multiply ─────────────────────────────────────────────────────
            if ((insn & 0x0FC000F0) == 0x00000090) // MUL / MLA
            {
                return ExecuteMultiply(insn);
            }
            if ((insn & 0x0F8000F0) == 0x00800090) // UMULL / SMULL / UMLAL / SMLAL
            {
                return ExecuteLongMultiply(insn);
            }

            // ── MSR / MRS ────────────────────────────────────────────────────
            if ((insn & 0x0FBF0FFF) == 0x010F0000) // MRS
            {
                int rd = (int)((insn >> 12) & 0xF);
                _r[rd] = _cpsr;
                return true;
            }
            if ((insn & 0x0DB0F000) == 0x0120F000) // MSR
            {
                bool imm = (insn & 0x02000000) != 0;
                uint val = imm ? RotateRight(insn & 0xFF, (int)((insn >> 8) & 0xF) * 2)
                               : _r[insn & 0xF];
                uint mask = 0;
                if ((insn & (1 << 16)) != 0) mask |= 0x000000FF;
                if ((insn & (1 << 17)) != 0) mask |= 0x0000FF00;
                if ((insn & (1 << 18)) != 0) mask |= 0x00FF0000;
                if ((insn & (1 << 19)) != 0) mask |= 0xFF000000;
                _cpsr = (_cpsr & ~mask) | (val & mask);
                return true;
            }

            // ── CLZ ─────────────────────────────────────────────────────────
            if ((insn & 0x0FFF0FF0) == 0x016F0F10) // CLZ Rd, Rm
            {
                int rd = (int)((insn >> 12) & 0xF);
                int rm = (int)(insn & 0xF);
                _r[rd] = (uint)CountLeadingZeros(_r[rm]);
                return true;
            }

            Debug.WriteLine($"[ArmCPU] Unimplemented ARM opcode: 0x{insn:X8} at PC=0x{pc:X8}");
            return true; // skip unknown
        }

        // ── Data-processing ─────────────────────────────────────────────────

        private bool ExecuteDataProcessing(uint insn)
        {
            bool setFlags = (insn & (1u << 20)) != 0;
            int  opcode   = (int)((insn >> 21) & 0xF);
            int  rn       = (int)((insn >> 16) & 0xF);
            int  rd       = (int)((insn >> 12) & 0xF);

            uint op2; bool shiftCarry;
            if ((insn & (1u << 25)) != 0) // immediate
            {
                uint imm   = insn & 0xFF;
                int  rot   = (int)((insn >> 8) & 0xF) * 2;
                op2 = RotateRight(imm, rot);
                shiftCarry = rot != 0 && ((imm >> (rot - 1)) & 1) != 0;
            }
            else
            {
                op2 = GetShiftedRegister(insn, out shiftCarry);
            }

            uint rnVal = _r[rn];
            uint result;
            bool writeResult = true;

            switch (opcode)
            {
                case 0:  result = rnVal & op2;   break; // AND
                case 1:  result = rnVal ^ op2;   break; // EOR
                case 2:  result = Sub32(rnVal, op2, 0, setFlags, out _); break; // SUB
                case 3:  result = Sub32(op2, rnVal, 0, setFlags, out _); break; // RSB
                case 4:  result = Add32(rnVal, op2, 0, setFlags, out _); break; // ADD
                case 5:  result = Add32(rnVal, op2, C ? 1u : 0u, setFlags, out _); break; // ADC
                case 6:  result = Sub32(rnVal, op2, C ? 0u : 1u, setFlags, out _); break; // SBC
                case 7:  result = Sub32(op2, rnVal, C ? 0u : 1u, setFlags, out _); break; // RSC
                case 8:  result = rnVal & op2; writeResult = false; break; // TST
                case 9:  result = rnVal ^ op2; writeResult = false; break; // TEQ
                case 10: result = Sub32(rnVal, op2, 0, true, out _); writeResult = false; break; // CMP
                case 11: result = Add32(rnVal, op2, 0, true, out _); writeResult = false; break; // CMN
                case 12: result = rnVal | op2;  break; // ORR
                case 13: result = op2;           break; // MOV
                case 14: result = rnVal & ~op2;  break; // BIC
                case 15: result = ~op2;          break; // MVN
                default: return true;
            }

            if (setFlags && (opcode == 0 || opcode == 1 || opcode == 8 || opcode == 9 ||
                             opcode == 12 || opcode == 13 || opcode == 14 || opcode == 15))
            {
                SetNZFlags(result);
                C = shiftCarry;
            }

            if (writeResult)
            {
                if (rd == 15) // writing PC
                {
                    PC_ref = result & ~1u;
                    if (setFlags) _cpsr = _r[17]; // SPSR restore (simplified)
                }
                else
                    _r[rd] = result;
            }
            return true;
        }

        // ── Load / Store ─────────────────────────────────────────────────────

        private bool ExecuteLoadStore(uint insn)
        {
            bool isLoad  = (insn & (1u << 20)) != 0;
            bool isByte  = (insn & (1u << 22)) != 0;
            bool preIndex= (insn & (1u << 24)) != 0;
            bool addOff  = (insn & (1u << 23)) != 0;
            bool writeBack=(insn & (1u << 21)) != 0;

            int rn = (int)((insn >> 16) & 0xF);
            int rd = (int)((insn >> 12) & 0xF);

            uint offset;
            bool _c;
            if ((insn & (1u << 25)) == 0)
                offset = insn & 0xFFF; // immediate
            else
                offset = GetShiftedRegister(insn, out _c);

            uint addr = _r[rn];
            if (preIndex)  addr = addOff ? addr + offset : addr - offset;

            if (isLoad)
            {
                uint val = isByte ? _memory.ReadByte(addr) : _memory.ReadUInt32(addr);
                if (rd != 15) _r[rd] = val;
                else          PC_ref  = val & ~1u;
            }
            else
            {
                uint val = (rd == 15) ? (PC_ref + 4) : _r[rd];
                if (isByte) _memory.WriteByte(addr, (byte)val);
                else        _memory.WriteUInt32(addr, val);
            }

            if (!preIndex) addr = addOff ? _r[rn] + offset : _r[rn] - offset;
            if (!preIndex || writeBack) _r[rn] = addr;

            return true;
        }

        // ── Load/Store multiple ───────────────────────────────────────────────

        private bool ExecuteLoadStoreMultiple(uint insn)
        {
            bool isLoad  = (insn & (1u << 20)) != 0;
            bool preIndex= (insn & (1u << 24)) != 0;
            bool addOff  = (insn & (1u << 23)) != 0;
            bool writeBack=(insn & (1u << 21)) != 0;
            int  rn      = (int)((insn >> 16) & 0xF);
            uint regList = insn & 0xFFFF;

            uint addr = _r[rn];
            int  count = CountBits((int)regList);

            if (!addOff) addr -= (uint)(count * 4);

            uint startAddr = addr;
            if (preIndex == addOff) startAddr += 4; // handle IA/DB/IB/DA variants

            uint cur = startAddr;
            for (int i = 0; i < 16; i++)
            {
                if ((regList & (1u << i)) == 0) continue;
                if (isLoad)
                {
                    uint val = _memory.ReadUInt32(cur);
                    if (i == 15) PC_ref = val & ~1u;
                    else         _r[i] = val;
                }
                else
                {
                    _memory.WriteUInt32(cur, _r[i]);
                }
                cur += 4;
            }

            if (writeBack)
                _r[rn] = addOff ? startAddr + (uint)(count * 4) : startAddr - 4;

            return true;
        }

        // ── Multiply ─────────────────────────────────────────────────────────

        private bool ExecuteMultiply(uint insn)
        {
            bool setFlags = (insn & (1u << 20)) != 0;
            bool accumulate=(insn & (1u << 21)) != 0;
            int rd = (int)((insn >> 16) & 0xF);
            int rn = (int)((insn >> 12) & 0xF);
            int rs = (int)((insn >>  8) & 0xF);
            int rm = (int)(insn & 0xF);

            uint result = _r[rm] * _r[rs];
            if (accumulate) result += _r[rn];
            _r[rd] = result;
            if (setFlags) SetNZFlags(result);
            return true;
        }

        private bool ExecuteLongMultiply(uint insn)
        {
            bool setFlags = (insn & (1u << 20)) != 0;
            bool accumulate=(insn & (1u << 21)) != 0;
            bool isSigned  = (insn & (1u << 22)) != 0;
            int rdHi = (int)((insn >> 16) & 0xF);
            int rdLo = (int)((insn >> 12) & 0xF);
            int rs   = (int)((insn >>  8) & 0xF);
            int rm   = (int)(insn & 0xF);

            long result;
            if (isSigned)
                result = (long)(int)_r[rm] * (long)(int)_r[rs];
            else
                result = (long)((ulong)_r[rm] * (ulong)_r[rs]);

            if (accumulate)
                result += ((long)_r[rdHi] << 32) | _r[rdLo];

            _r[rdLo] = (uint)(ulong)result;
            _r[rdHi] = (uint)((ulong)result >> 32);
            return true;
        }

        // ── Thumb-16 mode ─────────────────────────────────────────────────────

        private bool ExecuteThumb()
        {
            uint pc   = PC_ref;
            ushort insn = (ushort)_memory.ReadUInt16(pc);
            PC_ref = pc + 2;

            uint op = (uint)(insn >> 11);

            // BL / BLX prefix+suffix (32-bit Thumb).
            if (op == 0x1E || op == 0x1F) // BL
            {
                int off11 = insn & 0x7FF;
                if (op == 0x1E) // first halfword: set LR
                {
                    int imm = ((off11 << 12) | 0xFFFFF800); // sign-extend 11 bits
                    LR_ref = (uint)(PC_ref + imm);
                }
                else // second halfword: branch
                {
                    uint newPC = (uint)(LR_ref + (off11 << 1));
                    LR_ref = (PC_ref - 2) | 1; // return address, Thumb set
                    PC_ref = newPC;
                }
                return true;
            }

            // BX LR (POP {PC}) – return from function.
            if (insn == 0x4770) // BX LR
            {
                uint target = LR_ref;
                T = (target & 1) != 0;
                PC_ref = target & ~1u;
                return true;
            }

            // SVC.
            if ((insn & 0xFF00) == 0xDF00)
            {
                uint sysno = _r[7];
                long result = _syscalls.Dispatch((int)sysno, this, _process);
                _r[0] = (uint)(int)result;
                return true;
            }

            // MOV Rd, #imm8
            if ((insn >> 11) == 0x4) // 001 00 XXX
            {
                if ((insn & 0xF800) == 0x2000) // MOV Rd, #imm8 (Thumb T1)
                {
                    int rd = (insn >> 8) & 7;
                    uint imm = (uint)(insn & 0xFF);
                    _r[rd] = imm;
                    SetNZFlags(imm); C = false; V = false;
                    return true;
                }
            }

            // PUSH {reg list}.
            if ((insn & 0xFE00) == 0xB400)
            {
                bool pushLR = (insn & 0x0100) != 0;
                uint rlist  = (uint)(insn & 0xFF);
                if (pushLR) { SP_ref -= 4; _memory.WriteUInt32(SP_ref, LR_ref); }
                for (int i = 7; i >= 0; i--)
                {
                    if ((rlist & (1u << i)) != 0)
                    { SP_ref -= 4; _memory.WriteUInt32(SP_ref, _r[i]); }
                }
                return true;
            }

            // POP {reg list}.
            if ((insn & 0xFE00) == 0xBC00)
            {
                bool popPC = (insn & 0x0100) != 0;
                uint rlist  = (uint)(insn & 0xFF);
                for (int i = 0; i <= 7; i++)
                {
                    if ((rlist & (1u << i)) != 0)
                    { _r[i] = _memory.ReadUInt32(SP_ref); SP_ref += 4; }
                }
                if (popPC)
                {
                    uint target = _memory.ReadUInt32(SP_ref); SP_ref += 4;
                    T = (target & 1) != 0;
                    PC_ref = target & ~1u;
                }
                return true;
            }

            // ADD/SUB immediate (Thumb T2).
            if ((insn & 0xF000) == 0x3000)
            {
                bool isSub = (insn & 0x0800) != 0;
                int rd = (insn >> 8) & 7;
                uint imm = (uint)(insn & 0xFF);
                _r[rd] = isSub ? Sub32(_r[rd], imm, 0, true, out _)
                                : Add32(_r[rd], imm, 0, true, out _);
                return true;
            }

            // LDR / STR Thumb immediate.
            if ((insn & 0xE000) == 0x6000)
            {
                bool isLoad = (insn & 0x0800) != 0;
                bool isByte = (insn & 0x1000) != 0;
                int  rn     = (insn >> 3) & 7;
                int  rd     =  insn       & 7;
                uint off    = (uint)((insn >> 6) & 0x1F) << (isByte ? 0 : 2);
                uint addr   = _r[rn] + off;
                if (isLoad)
                    _r[rd] = isByte ? _memory.ReadByte(addr) : _memory.ReadUInt32(addr);
                else
                    if (isByte) _memory.WriteByte(addr, (byte)_r[rd]);
                    else        _memory.WriteUInt32(addr, _r[rd]);
                return true;
            }

            // B (Thumb T2 unconditional).
            if ((insn & 0xF800) == 0xE000)
            {
                int off = ((int)(insn << 21)) >> 20; // sign-extend 11 bits, << 1
                PC_ref = (uint)(PC_ref + off);
                return true;
            }

            // B<cond> Thumb T1.
            if ((insn & 0xF000) == 0xD000)
            {
                uint cond = (uint)((insn >> 8) & 0xF);
                if (CheckCondition(cond))
                {
                    int off = ((sbyte)(insn & 0xFF)) * 2;
                    PC_ref = (uint)(PC_ref + off);
                }
                return true;
            }

            // Unimplemented Thumb opcode – skip.
            Debug.WriteLine($"[ArmCPU] Unimplemented Thumb opcode: 0x{insn:X4} at PC=0x{pc:X8}");
            return true;
        }

        // ── Condition evaluation ─────────────────────────────────────────────

        private bool CheckCondition(uint cond)
        {
            switch (cond)
            {
                case 0:  return  Z;          // EQ
                case 1:  return !Z;          // NE
                case 2:  return  C;          // CS/HS
                case 3:  return !C;          // CC/LO
                case 4:  return  N;          // MI
                case 5:  return !N;          // PL
                case 6:  return  V;          // VS
                case 7:  return !V;          // VC
                case 8:  return  C && !Z;    // HI
                case 9:  return !C ||  Z;    // LS
                case 10: return  N == V;     // GE
                case 11: return  N != V;     // LT
                case 12: return !Z && N == V;// GT
                case 13: return  Z || N != V;// LE
                case 14: return true;        // AL
                case 15: return true;        // (NV / reserved, treat as AL)
                default: return true;
            }
        }

        // ── Shifted register operand ─────────────────────────────────────────

        private uint GetShiftedRegister(uint insn, out bool carryOut)
        {
            int rm   = (int)(insn & 0xF);
            int type = (int)((insn >> 5) & 3);
            int amt;
            if ((insn & (1u << 4)) != 0) // register shift
                amt = (int)(_r[(insn >> 8) & 0xF] & 0x1F);
            else                          // immediate shift
                amt = (int)((insn >> 7) & 0x1F);

            uint val = _r[rm];
            carryOut = C;

            if (amt == 0 && (insn & (1u << 4)) == 0)
            {
                if (type == 3) { carryOut = (val >> 31) != 0; val = RotateRight(val, 1); }
                return val;
            }

            switch (type)
            {
                case 0: // LSL
                    if (amt >= 32) { carryOut = amt == 32 && (val & 1) != 0; val = 0; }
                    else { carryOut = amt > 0 && ((val >> (32 - amt)) & 1) != 0; val = val << amt; }
                    break;
                case 1: // LSR
                    if (amt >= 32) { carryOut = amt == 32 && ((val >> 31) & 1) != 0; val = 0; }
                    else { carryOut = amt > 0 && ((val >> (amt - 1)) & 1) != 0; val = val >> amt; }
                    break;
                case 2: // ASR
                    if (amt >= 32) { carryOut = (val >> 31) != 0; val = (val >> 31) != 0 ? 0xFFFFFFFF : 0; }
                    else { carryOut = amt > 0 && ((val >> (amt - 1)) & 1) != 0; val = (uint)((int)val >> amt); }
                    break;
                case 3: // ROR
                    amt &= 31; if (amt == 0) break;
                    carryOut = ((val >> (amt - 1)) & 1) != 0;
                    val = RotateRight(val, amt);
                    break;
            }
            return val;
        }

        // ── Arithmetic helpers ────────────────────────────────────────────────

        private uint Add32(uint a, uint b, uint carry, bool setFlags, out bool overflow)
        {
            ulong result = (ulong)a + b + carry;
            if (setFlags)
            {
                N = (result >> 31 & 1) != 0;
                Z = (result & 0xFFFFFFFF) == 0;
                C = result > 0xFFFFFFFF;
                V = (~(a ^ b) & (a ^ (uint)result) & 0x80000000) != 0;
            }
            overflow = V;
            return (uint)result;
        }

        private uint Sub32(uint a, uint b, uint borrow, bool setFlags, out bool overflow)
        {
            ulong result = (ulong)a - b - borrow;
            if (setFlags)
            {
                N = (result >> 31 & 1) != 0;
                Z = (result & 0xFFFFFFFF) == 0;
                C = a >= b + borrow; // borrow flag (inverted from x86)
                V = ((a ^ b) & (a ^ (uint)result) & 0x80000000) != 0;
            }
            overflow = V;
            return (uint)result;
        }

        private void SetNZFlags(uint result)
        {
            N = (result >> 31 & 1) != 0;
            Z = result == 0;
        }

        private static uint RotateRight(uint value, int amount)
        {
            amount &= 31;
            if (amount == 0) return value;
            return (value >> amount) | (value << (32 - amount));
        }

        private static int CountLeadingZeros(uint val)
        {
            if (val == 0) return 32;
            int n = 0;
            if ((val & 0xFFFF0000) == 0) { n += 16; val <<= 16; }
            if ((val & 0xFF000000) == 0) { n += 8;  val <<= 8;  }
            if ((val & 0xF0000000) == 0) { n += 4;  val <<= 4;  }
            if ((val & 0xC0000000) == 0) { n += 2;  val <<= 2;  }
            if ((val & 0x80000000) == 0)   n += 1;
            return n;
        }

        private static int CountBits(int v)
        {
            int c = 0;
            for (int i = 0; i < 16; i++) if ((v & (1 << i)) != 0) c++;
            return c;
        }

        // ── Register dump ─────────────────────────────────────────────────────

        public string DumpRegisters()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 16; i++)
                sb.Append($"R{i,2}=0x{_r[i]:X8}  ");
            sb.Append($"\nCPSR=0x{_cpsr:X8}  N={N} Z={Z} C={C} V={V} T={T}");
            return sb.ToString();
        }
    }
}
