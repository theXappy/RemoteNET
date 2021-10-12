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
	if (argc < 2) {
		printf("Usage: %s PID\nPID - Process ID to inject into.", argv[0]);
	}
	printf("Starting...");
	// Bootstrapper
	char DllName[MAX_PATH];
	GetCurrentDirectoryA(MAX_PATH, DllName);

	// Path to DLL - Injected payload C# code
	wchar_t DllNameW[MAX_PATH];
	GetCurrentDirectory(MAX_PATH, DllNameW);
	wcscat_s(DllNameW, L"\\ScubaDiver.dll");
	printf("Payload DLL Path: %ls\n", DllNameW);


	DWORD Pid = atoi(argv[1]);
	strcat_s(DllName, "\\BootstrapDLL.dll");

	printf("[.] Injecting BootstrapDLL into %d\n", Pid);
	InjectAndRunThenUnload(Pid, DllName, "LoadManagedProject", DllNameW);

	printf("[.] Done!");

	return 0;
}

