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
	if (argc < 3) {
		printf("Usage: %s PID ARG_FOR_INJECTED_DLL", argv[0]);
		return 1;
	}
	printf("Starting...\n");
#ifdef _WIN64
	printf("x64 Version\n");
#else
	printf("x32 Version\n");
#endif

	// Unmanaged Adapter
	char DllName[MAX_PATH];
	GetCurrentDirectoryA(MAX_PATH, DllName);

	// Convert arguments to wchar_t[] and concat
	wchar_t adapterDllArg[MAX_PATH];
	size_t convertedChars = 0;
	mbstowcs_s(&convertedChars, adapterDllArg, MAX_PATH, argv[2], _TRUNCATE);


	printf("UnmanagedAdapterDLL encoded argument: %ls\n", adapterDllArg);

	DWORD Pid = atoi(argv[1]);
#ifdef _WIN64
	strcat_s(DllName, "\\UnmanagedAdapterDLL_x64.dll");
#else
	strcat_s(DllName, "\\UnmanagedAdapterDLL.dll");
#endif

	printf("[.] Injecting UnmanagedAdapterDLL into %d\n", Pid);
	InjectAndRunThenUnload(Pid, DllName, "AdapterEntryPoint", adapterDllArg);

	printf("[.] Done!");

	return 0;
}

