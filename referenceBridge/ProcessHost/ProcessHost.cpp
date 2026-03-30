#include "pch.h"
#include "ProcessHost.h"
#include <Windows.h>
#include <Shellapi.h>
#include <stdio.h>
#include <cstdlib>
#include <string>
#include <vector>
#include "android_init.h"
extern "C"
{
#include "../FLinux/src/ntdll.h"
}



void DebugLog(char* pszFormat, ...) {

	static char s_acBuf[2048]; // this here is a caveat!

	va_list args;

	va_start(args, pszFormat);

	vsnprintf(s_acBuf, 2048, pszFormat, args);

	OutputDebugStringA(s_acBuf);

	va_end(args);
}

struct intstack15
{
	intptr_t pos[15];
};


typedef void(*entrypoint_t)(...);

std::wstring atow(const char* str)
{
	size_t len = strlen(str);
	std::wstring ws(len, L' '); // Overestimate number of code points.
	ws.resize(std::mbstowcs(&ws[0], str, len)); // Shrink to fit.
	return ws;
}

int filterException(int code, PEXCEPTION_POINTERS ex) {
	DebugLog("Filtering 0x%x", code);
	DebugLog("Exception code 0x%x", ex->ExceptionRecord->ExceptionCode);
	return EXCEPTION_EXECUTE_HANDLER;
}

void init(const wchar_t* root)
{
	__try
	{
		flinit(root, NULL);
	}
	__except (filterException(GetExceptionCode(), GetExceptionInformation())) {
		DebugLog("caught\n");
	}

}

int start()
{
	DebugLog("loading dll\n");

	HMODULE app_process = LoadPackagedLibrary(L"patchoat.dll", 0);

	DebugLog("module handle: 0x%x\n", app_process);

	if (app_process != 0)
	{
		entrypoint_t _module_entry_point = (entrypoint_t)::GetProcAddress(app_process, "_module_entry_point_");


		DebugLog("module entry point 0x%x", _module_entry_point);

		if (_module_entry_point == NULL)
		{
			DebugLog("start: _module_entry_point_ not found in patchoat.dll\n");
			return -1;
		}

		intstack15 stack_params;

		int i = 0;
		/*int j = 0;
		stack_params.pos[i++] = argc;
		for (; j <= argc; i++, j++)
		stack_params.pos[i] = (intptr_t)argv[j];

		stack_params.pos[i++] = 0;

		j = 0;
		for (; j < envc; i++, j++)
		stack_params.pos[i] = (intptr_t)envp[j];*/

		stack_params.pos[i++] = 0;

		__try
		{
			_module_entry_point(
#ifdef _M_ARM
				0, 0, 0, 0, //skip register params on ARM (r0-r3), copy all params to stack
#elif defined(_M_AMD64)
				0, 0, 0, 0, //skip register params on x64 (RCX, RDX, R8, R9), copy all params to stack
#endif
				stack_params);
		}
		__except (filterException(GetExceptionCode(), GetExceptionInformation()))
		{
			DebugLog("start: exception in module entry point\n");
			return -1;
		}

		return 0;

	}

	return -1;

}

// UWP-compatible fork implementation.
// RtlCloneUserProcess is not permitted inside the UWP AppContainer security sandbox.
// Instead we spawn a fresh child instance of this process via CreateProcess, forwarding
// the current --root and --params arguments plus a --child marker.
// Returns:
//   > 0 (child PID)  in the parent process
//   0               if this process is already the child (isChildProcess == true)
//   -1              on error
static int fork_uwp(bool isChildProcess, const std::wstring& wroot, int argc_val)
{
	if (isChildProcess)
	{
		// We are already the child; no need to spawn again.
		return 0;
	}

	WCHAR szExePath[MAX_PATH] = {};
	if (!GetModuleFileNameW(NULL, szExePath, MAX_PATH))
	{
		DebugLog("fork_uwp: GetModuleFileNameW failed (error 0x%x)\n", GetLastError());
		return -1;
	}

	// Build command line: forward current root/params args and append --child.
	// Use a vector<wchar_t> so that CreateProcessW gets the required writable buffer.
	std::wstring cmdLineStr = L"\"";
	cmdLineStr += szExePath;
	cmdLineStr += L"\"";
	if (!wroot.empty())
	{
		cmdLineStr += L" --root=";
		cmdLineStr += wroot;
	}
	if (argc_val > 0)
	{
		cmdLineStr += L" --params=";
		cmdLineStr += std::to_wstring(argc_val);
	}
	cmdLineStr += L" --child";

	std::vector<wchar_t> cmdLineBuf(cmdLineStr.begin(), cmdLineStr.end());
	cmdLineBuf.push_back(L'\0');

	STARTUPINFOW si = {};
	si.cb = sizeof(si);
	PROCESS_INFORMATION pi = {};

	BOOL ok = CreateProcessW(NULL, cmdLineBuf.data(), NULL, NULL,
	                          FALSE, CREATE_SUSPENDED, NULL, NULL, &si, &pi);
	if (!ok)
	{
		DebugLog("fork_uwp: CreateProcessW failed (error 0x%x)\n", GetLastError());
		return -1;
	}

	// Resume the child just like the RTL_CLONE_PARENT path used to do.
	ResumeThread(pi.hThread);
	DWORD childPid = pi.dwProcessId;
	CloseHandle(pi.hThread);
	CloseHandle(pi.hProcess);

	DebugLog("fork_uwp: spawned child PID %u\n", (unsigned)childPid);
	return static_cast<int>(childPid);
}

int main()
{
	LPCSTR cmdLine = GetCommandLineA();
	DebugLog("command line: %s\n", cmdLine);


	std::wstring wroot;
	int argc = 0;
	bool isChild = false;

	int i = 0;
	int bufferSize = strlen(cmdLine) + 1;
	char* buffer = new char[bufferSize];

	strcpy_s(buffer, bufferSize, cmdLine);

	while (i < bufferSize && buffer[i] == '-' && buffer[i + 1] == '-')
	{
		i += 2; //skip --

		if (!strncmp(buffer + i, "root=", 5))
		{
			i += 5;
			int strindex = i;
			while (buffer[i] != ' ' && buffer[i] != 0)
				i++;
			buffer[i++] = 0;
			wroot = atow(buffer + strindex);

		}
		else if (!strncmp(buffer + i, "params=", 7))
		{
			i += 7;
			argc = atoi(buffer + i);
			while (buffer[i] != ' ' && buffer[i] != 0)
				i++;
			if (buffer[i] == ' ') i++;
		}
		else if (!strncmp(buffer + i, "child", 5))
		{
			// This process was spawned as the fork() child.
			isChild = true;
			i += 5;
			break;
		}
		else
		{
			// Unknown argument; skip to next token.
			while (buffer[i] != ' ' && buffer[i] != 0)
				i++;
			if (buffer[i] == ' ') i++;
		}
	}

	// Simulate fork() for UWP: spawn a suspended child and return its PID to the parent.
	// The child detects the --child flag above and skips re-forking.
	int forkResult = fork_uwp(isChild, wroot, argc);

	delete[] buffer;

	if (forkResult > 0)
	{
		// Parent path: child has been spawned and resumed; parent exits cleanly.
		DebugLog("fork_uwp: parent, child PID = %d\n", forkResult);
	}
	else if (forkResult == 0)
	{
		// Child path: continue below and execute the module entry point.
		DebugLog("fork_uwp: executing as child process\n");
	}
	else
	{
		DebugLog("fork_uwp: failed, continuing without fork\n");
	}

	init(wroot.c_str());

	return start();
}
