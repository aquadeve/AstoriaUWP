/*
 * This file is part of Foreign Linux.
 *
 * Copyright (C) 2014, 2015 Xiangyan Sun <wishstudio@gmail.com>
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <http://www.gnu.org/licenses/>.
 */

#pragma once

#define WIN32_LEAN_AND_MEAN
#include <Windows.h>

struct syscall_context
{
	/* DO NOT REORDER */
	/* Context for fork() - mirrors the 64-bit register set */
	DWORD64 rbx;
	DWORD64 rcx;
	DWORD64 rdx;
	DWORD64 rsi;
	DWORD64 rdi;
	DWORD64 rbp;
	union
	{
		DWORD64 sp;
		DWORD64 rsp;
	};
	union
	{
		DWORD64 pc;
		DWORD64 rip;
	};

	/* The following are not used by fork() */
	union
	{
		DWORD64 r0;
		DWORD64 rax;
	};
	DWORD64 rflags;

	/* x64-only extended registers */
	DWORD64 r8;
	DWORD64 r9;
	DWORD64 r10;
	DWORD64 r11;
	DWORD64 r12;
	DWORD64 r13;
	DWORD64 r14;
	DWORD64 r15;
};
