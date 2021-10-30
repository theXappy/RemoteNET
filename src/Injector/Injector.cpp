#include "stdafx.h"
#include <Windows.h>
#include <iostream>
#include <TlHelp32.h>
#include <stdlib.h>
#include <stdio.h>
#include <string>

#include "Injection.h"


using namespace std;

int main(int argc, char** argv)
{
	if (argc < 4) {
		printf("Usage: %s PID\nPID SCUBA_DIVER_PATH SCUBA_DIVER_ARG", argv[0]);
		return 1;
	}
	printf("Starting...\n");
#ifdef _WIN64
	printf("x64 Version\n");
#else
	printf("x32 Version\n");
#endif

	// Bootstrapper
	char DllName[MAX_PATH];
	GetCurrentDirectoryA(MAX_PATH, DllName);

	// Convert arguments to wchar_t[] and concat
	wchar_t BootstrapDllArg[MAX_PATH];
	size_t convertedChars = 0;
	mbstowcs_s(&convertedChars, BootstrapDllArg, MAX_PATH, argv[2], _TRUNCATE);
	wcscat_s(BootstrapDllArg, L"*");

	wchar_t ScubaDiverDllArg[MAX_PATH];
	mbstowcs_s(&convertedChars, ScubaDiverDllArg, MAX_PATH, argv[3], _TRUNCATE);

	wcscat_s(BootstrapDllArg, ScubaDiverDllArg);
	printf("BootstrapDLL encoded argument: %ls\n", BootstrapDllArg);

	DWORD Pid = atoi(argv[1]);
#ifdef _WIN64
	strcat_s(DllName, "\\BootstrapDLL64.dll");
#else
	strcat_s(DllName, "\\BootstrapDLL.dll");
#endif

	printf("[.] Injecting BootstrapDLL into %d\n", Pid);
	InjectAndRunThenUnload(Pid, DllName, "LoadManagedProject", BootstrapDllArg);

	printf("[.] Done!");

	return 0;
}

