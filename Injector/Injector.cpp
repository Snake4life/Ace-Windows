#include "stdafx.h"

void EnterDebugLoop(const LPDEBUG_EVENT debug) {
	while (true) {
		if (!WaitForDebugEvent(debug, INFINITE) || debug->dwDebugEventCode == EXIT_PROCESS_DEBUG_EVENT)
			return;
		ContinueDebugEvent(debug->dwProcessId,
			debug->dwThreadId,
			DBG_EXCEPTION_NOT_HANDLED);
	}
}

int main(int argc, CHAR* argv[]) {
	assert(argc > 1);

	CHAR arg_line[1024 * 4];
	CHAR CURR_DIR[MAX_PATH];
	CHAR ACE_DIR[MAX_PATH];

	arg_line[0] = 0;

	for (int c = 1; c < argc; c++) {
		strcat(arg_line, "\"");
		strcat(arg_line, argv[c]);
		strcat(arg_line, "\" ");
	}

	GetCurrentDirectoryA(MAX_PATH, CURR_DIR);

	strcpy(ACE_DIR, getenv("APPDATA"));
	strcat(ACE_DIR, "\\Ace");

	PROCESS_INFORMATION pi;
	STARTUPINFO info;
	ZeroMemory(&info, sizeof(info));
	info.cb = sizeof(info);

	BOOL succ = CreateProcessA(NULL, arg_line, NULL, NULL, FALSE, DEBUG_ONLY_THIS_PROCESS, NULL, CURR_DIR, &info, &pi);
	if (succ) {
		CHAR path[MAX_PATH];
		strcpy(path, ACE_DIR);
		strcat(path, "\\payload.dll");

		void* pPathBuffer = VirtualAllocEx(pi.hProcess, NULL, MAX_PATH * sizeof(CHAR), MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
		assert(pPathBuffer);

		SIZE_T written;
		WriteProcessMemory(pi.hProcess, pPathBuffer, path, MAX_PATH * sizeof(CHAR), &written);

		HANDLE hRemoteThread = CreateRemoteThread(pi.hProcess, nullptr, 0,
			(PTHREAD_START_ROUTINE)GetProcAddress(GetModuleHandle("kernel32"), "LoadLibraryA"),
			pPathBuffer, 0, nullptr);

		if (hRemoteThread) {
			WaitForSingleObject(hRemoteThread, 1000);
		}

		CloseHandle(hRemoteThread);

		DEBUG_EVENT debug = {0};

		EnterDebugLoop(&debug);
	}

	return 0;
}