// stdafx.cpp : source file that includes just the standard includes
// Ace-Launcher.pch will be the pre-compiled header
// stdafx.obj will contain the pre-compiled type information

#include "stdafx.h"

// TODO: reference any additional headers you need in STDAFX.H
// and not in this file

ULONG_PTR GetParentProcessId() {
	DWORD pid = 0;
	DWORD curpid = GetCurrentProcessId();
	BOOL res;
	HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);

	PROCESSENTRY32 procinfo;
	procinfo.dwSize = sizeof(PROCESSENTRY32);
	res = Process32First(snapshot, &procinfo);

	while (res) {
		if (curpid == procinfo.th32ProcessID) {
			pid = procinfo.th32ParentProcessID;
		}
		res = !pid && Process32Next(snapshot, &procinfo);
	}
	return pid;
}