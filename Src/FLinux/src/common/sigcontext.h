#pragma once

#include <common/types.h>
#include <common/signal.h>

struct i387_fsave_struct
{
	uint32_t cwd; /* FPU Control Word */
	uint32_t swd; /* FPU Status Word */
	uint32_t twd; /* FPU Tag Word */
	uint32_t fip; /* FPU IP Offset */
	uint32_t fcs; /* FPU IP Selector */
	uint32_t foo; /* FPU Operand Pointer Offset */
	uint32_t fos; /* FPU Operand Pointer Selector */
	
	/* 8*10 bytes for each FP-reg = 80 bytes: */
	uint32_t st_space[20];
	/* Software status information [not touched by FSAVE ]: */
	uint32_t status;
};

struct i387_fxsave_struct
{
	uint16_t cwd; /* Control Word */
	uint16_t swd; /* Status Word */
	uint16_t twd; /* Tag Word */
	uint16_t fop; /* Last Instruction Opcode */
	union {
		struct {
			uint64_t rip; /* Instruction Pointer */
			uint64_t rdp; /* Data Pointer */
		};
		struct {
			uint32_t fip; /* FPU IP Offset */
			uint32_t fcs; /* FPU IP Selector */
			uint32_t foo; /* FPU Operand Offset */
			uint32_t fos; /* FPU Operand Selector */
		};
	};
	uint32_t mxcsr; /* MXCSR Register State */
	uint32_t mxcsr_mask; /* MXCSR Mask */

	/* 8*16 bytes for each FP-reg = 128 bytes: */
	uint32_t st_space[32];

	/* 16*16 bytes for each XMM-reg = 256 bytes: */
	uint32_t xmm_space[64];

	uint32_t padding[12];
	union {
		uint32_t padding1[12];
		uint32_t sw_reserved[12];
	};
};

struct fpx_sw_bytes
{
	uint32_t magic1; /* FP_XSTATE_MAGIC1 */
	uint32_t extended_size; /* total size of the layout referred by fpstate pointer in the sigcontext. */
	uint64_t xstate_bv; /* feature bit mask (including fp/sse/extended state) that is present in the memory layout.*/
	uint32_t xstate_size; /* actual xsave state size, based on the features saved in the layout, 'extended_size' will be greater than 'xstate_size'. */
	uint32_t padding[7]; /* for future use. */
};

struct fpreg
{
	uint16_t significand[4];
	uint16_t exponent;
};

struct fpxreg
{
	uint16_t significand[4];
	uint16_t exponent;
	uint16_t padding[3];
};

struct xmmreg
{
	uint32_t element[4];
};

struct fpstate
{
	/* Regular FPU environment */
	uint32_t cw;
	uint32_t sw;
	uint32_t tag; /* not compatible to 64bit twd */
	uint32_t ipoff;
	uint32_t cssel;
	uint32_t dataoff;
	uint32_t datasel;
	struct fpreg _st[8];
	uint16_t status;
	uint16_t magic; /* 0xffff = regular FPU data only */

	/* FXSR FPU environment */
	uint32_t _fxsr_env[6]; /* FXSR FPU env is ignored */
	uint32_t mxcsr;
	uint32_t reserved;
	struct fpxreg _fxsr_st[8]; /* FXSR FPU reg data is ignored */
	struct xmmreg _xmm[8];
	uint32_t padding1[44];
	union
	{
		uint32_t padding2[12];
		struct fpx_sw_bytes sw_reserved; /* represents the extended state info */
	};
};

struct xsave_hdr
{
	uint64_t xstate_bv;
	uint64_t reserved1[2];
	uint64_t reserved2[5];
};

struct ymmh_state
{
	uint32_t ymmh_space[64];
};

struct xstate
{
	struct fpstate fpstate;
	struct xsave_hdr xstate_hdr;
	struct ymmh_state ymmh;
};

#ifdef _WIN64
/* 64-bit signal context (mirrors Linux sigcontext_64 from arch/x86/include/uapi/asm/sigcontext.h) */
struct sigcontext
{
	uint64_t r8;
	uint64_t r9;
	uint64_t r10;
	uint64_t r11;
	uint64_t r12;
	uint64_t r13;
	uint64_t r14;
	uint64_t r15;
	uint64_t di;
	uint64_t si;
	uint64_t bp;
	uint64_t bx;
	uint64_t dx;
	uint64_t ax;
	uint64_t cx;
	uint64_t sp;
	uint64_t ip;
	uint64_t flags;
	uint16_t cs;
	uint16_t gs;
	uint16_t fs;
	uint16_t ss;
	uint64_t err;
	uint64_t trapno;
	uint64_t oldmask;
	uint64_t cr2;
	uint64_t fpstate; /* pointer to _fpstate_64 or _xstate */
	uint64_t reserved1[8];
};

struct ucontext
{
	uint64_t     uc_flags;
	uint64_t     uc_link;
	stack_t      uc_stack;
	uint64_t     uc_sigmask;
	struct sigcontext uc_mcontext;
};
#else
/* 32-bit signal context (mirrors Linux sigcontext_32 from arch/x86/include/uapi/asm/sigcontext.h) */
struct sigcontext
{
	uint16_t gs, __gsh;
	uint16_t fs, __fsh;
	uint16_t es, __esh;
	uint16_t ds, __dsh;
	uint32_t di;
	uint32_t si;
	uint32_t bp;
	uint32_t sp;
	uint32_t bx;
	uint32_t dx;
	uint32_t cx;
	uint32_t ax;
	uint32_t trapno;
	uint32_t err;
	uint32_t ip;
	uint16_t cs, __csh;
	uint32_t flags;
	uint32_t sp_at_signal;
	uint16_t ss, __ssh;

	void *fpstate;
	uint32_t oldmask;
	uint32_t cr2;
};

struct ucontext
{
	uint32_t uc_flags;
	uint32_t uc_link;
	stack_t uc_stack;
	struct sigcontext uc_mcontext;
	sigset_t uc_sigmask;
};
#endif
