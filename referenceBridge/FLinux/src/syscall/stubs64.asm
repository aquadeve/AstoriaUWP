;
; This file is part of Foreign Linux.
;
; Copyright (C) 2014, 2015 Xiangyan Sun <wishstudio@gmail.com>
;
; This program is free software: you can redistribute it and/or modify
; it under the terms of the GNU General Public License as published by
; the Free Software Foundation, either version 3 of the License, or
; (at your option) any later version.
;
; This program is distributed in the hope that it will be useful,
; but WITHOUT ANY WARRANTY; without even the implied warranty of
; MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
; GNU General Public License for more details.
;
; You should have received a copy of the GNU General Public License
; along with this program. If not, see <http://www.gnu.org/licenses/>.
;

.code

; Offsets for the Windows x64 CONTEXT structure from winnt.h.
; We only need the integer/control register block for restore_context().
CONTEXT_RAX EQU 078h
CONTEXT_RCX EQU 080h
CONTEXT_RDX EQU 088h
CONTEXT_RBX EQU 090h
CONTEXT_RSP EQU 098h
CONTEXT_RBP EQU 0A0h
CONTEXT_RSI EQU 0A8h
CONTEXT_RDI EQU 0B0h
CONTEXT_R8  EQU 0B8h
CONTEXT_R9  EQU 0C0h
CONTEXT_R10 EQU 0C8h
CONTEXT_R11 EQU 0D0h
CONTEXT_R12 EQU 0D8h
CONTEXT_R13 EQU 0E0h
CONTEXT_R14 EQU 0E8h
CONTEXT_R15 EQU 0F0h
CONTEXT_RIP EQU 0F8h

goto_entrypoint PROC ; stack: QWORD, entrypoint: QWORD

mov rax, rdx ; entrypoint
mov rsp, rcx ; stack
push rax
xor rax, rax
xor rbx, rbx
xor rcx, rcx
xor rdx, rdx
xor rsi, rsi
xor rdi, rdi
xor rbp, rbp
xor r8, r8
xor r9, r9
xor r10, r10
xor r11, r11
xor r12, r12
xor r13, r13
xor r14, r14
xor r15, r15
ret

goto_entrypoint ENDP

restore_context PROC ; ctx: QWORD

mov rax, rcx ; ctx
mov rcx, [rax + CONTEXT_RCX]
mov rdx, [rax + CONTEXT_RDX]
mov rbx, [rax + CONTEXT_RBX]
mov rsi, [rax + CONTEXT_RSI]
mov rdi, [rax + CONTEXT_RDI]
mov rsp, [rax + CONTEXT_RSP]
mov rbp, [rax + CONTEXT_RBP]
mov r8, [rax + CONTEXT_R8]
mov r9, [rax + CONTEXT_R9]
mov r10, [rax + CONTEXT_R10]
mov r11, [rax + CONTEXT_R11]
mov r12, [rax + CONTEXT_R12]
mov r13, [rax + CONTEXT_R13]
mov r14, [rax + CONTEXT_R14]
mov r15, [rax + CONTEXT_R15]
push [rax + CONTEXT_RIP]
mov rax, [rax + CONTEXT_RAX]
ret

restore_context ENDP

PUBLIC mm_check_read_begin, mm_check_read_end, mm_check_read_fail
mm_check_read PROC ; check_addr: QWORD, check_size: QWORD
xchg rcx, rdx
; rcx = check_size
; rdx = check_addr
jrcxz SUCC

mm_check_read_begin LABEL PTR
mov al, byte ptr [rdx]
; test first page which may be unaligned

mov rax, rdx
shr rax, 12
; rax - start page
lea rcx, [rdx + rcx - 1]
shr rcx, 12
; rcx - end page
sub rcx, rax
; rcx - remaining pages
je SUCC

and dx, 0f000h
L:
add rdx, 01000h
mov al, byte ptr [rdx]
loop L
mm_check_read_end LABEL PTR

SUCC:
xor rax, rax
inc eax
ret

mm_check_read_fail LABEL PTR
xor rax, rax
ret
mm_check_read ENDP

PUBLIC mm_check_read_string_begin, mm_check_read_string_end, mm_check_read_string_fail
mm_check_read_string PROC ; check_addr: QWORD
mov rdx, rcx ; check_addr

mm_check_read_string_begin LABEL PTR
L:
mov al, byte ptr [rdx]
test al, al
jz SUCC
inc rdx
mm_check_read_string_end LABEL PTR

SUCC:
xor rax, rax
inc eax
ret

mm_check_read_string_fail LABEL PTR
xor rax, rax
ret
mm_check_read_string ENDP

PUBLIC mm_check_write_begin, mm_check_write_end, mm_check_write_fail
mm_check_write PROC ; check_addr: QWORD, check_size: QWORD
xchg rcx, rdx
; rcx = check_size
; rdx = check_addr
jrcxz SUCC

mm_check_write_begin LABEL PTR
mov al, byte ptr [rdx]
mov byte ptr [rdx], al
; test first page which may be unaligned

mov rax, rdx
shr rax, 12
; rax - start page
lea rcx, [rdx + rcx - 1]
shr rcx, 12
; rcx - end page
sub rcx, rax
; rcx - remaining pages
je SUCC

and dx, 0f000h
L:
add rdx, 01000h
mov al, byte ptr [rdx]
mov byte ptr [rdx], al
loop L
mm_check_write_end LABEL PTR

SUCC:
xor rax, rax
inc eax
ret

mm_check_write_fail LABEL PTR
xor rax, rax
ret
mm_check_write ENDP

fpu_fxsave PROC ; save_area: QWORD (rcx = pointer to 16-byte aligned 512-byte save area)
fxsave64 [rcx]
ret
fpu_fxsave ENDP

fpu_fxrstor PROC ; save_area: QWORD (rcx = pointer to 16-byte aligned 512-byte save area)
fxrstor64 [rcx]
ret
fpu_fxrstor ENDP

OPTION PROLOGUE: NONE
OPTION EPILOGUE: NONE
; this function will be called by signal return path
; syscall 15 = rt_sigreturn on x86_64
signal_restorer PROC
mov eax, 15
syscall
signal_restorer ENDP

END
