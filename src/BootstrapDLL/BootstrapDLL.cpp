#include "stdafx.h"
#include "msgboxf.h"
#include <metahost.h>
#include <io.h>
#include <fcntl.h>
#pragma comment(lib, "mscoree.lib")

#include "BootstrapDLL.h"
#include <stdio.h>

DllExport void LoadManagedProject(const wchar_t * bootstrapDllArg)
{
	HRESULT hr;
	wchar_t argCopy[MAX_PATH];
	wcscpy_s(argCopy, bootstrapDllArg);
	argCopy[MAX_PATH - 1] = 0;
	wchar_t* separator = wcsstr(argCopy, L"*");
	if (separator == NULL)
	{
		printf("[Bootstrap] ERROR: Failed to find separator (*) in BootstrapDLL argument:");
		wprintf(L"%s\n", argCopy);
		return;
	}
	// Splitting argument by replacing separator with null
	*separator = NULL;
	wchar_t* managedDllLocation = argCopy;
	wchar_t* scubaDiverArg = separator + 1;

	// All of this code is to spawn a console.
	if (true) {
		BOOL res = AllocConsole();
		HANDLE stdHandle;
		int hConsole;
		FILE* fp;
		stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
		hConsole = _open_osfhandle((intptr_t)stdHandle, _O_TEXT);
		fp = _fdopen(hConsole, "w");
		freopen_s(&fp, "CONOUT$", "w", stdout);
		// End of cosole spawning

		printf("[Bootstrap] Can you see me?\n");
		printf("[Bootstrap] managedDllLocation = %ls\n", managedDllLocation);
		printf("[Bootstrap] scubaDiverArg = %ls\n", scubaDiverArg);
		fflush(stdout);
	}

	// Secure a handle to the CLR v4.0
	ICLRRuntimeHost* pClr = StartCLR(L"v4.0.30319");
	if (pClr != NULL)
	{
		DWORD result;
		hr = pClr->ExecuteInDefaultAppDomain(
			managedDllLocation,
			L"ScubaDiver.Diver",
			L"EntryPoint",
			scubaDiverArg,
			&result);
	}
	else {
		msgboxf("[Bootstrap] could not spawn CRL\n");
	}
}

ICLRRuntimeHost* StartCLR(LPCWSTR dotNetVersion)
{
	HRESULT hr;

	ICLRMetaHost* pClrMetaHost = NULL;
	ICLRRuntimeInfo* pClrRuntimeInfo = NULL;
	ICLRRuntimeHost* pClrRuntimeHost = NULL;

	// Get the CLRMetaHost that tells us about .NET on this machine
	hr = CLRCreateInstance(CLSID_CLRMetaHost, IID_ICLRMetaHost, (LPVOID*)&pClrMetaHost);

	if (hr == S_OK)
	{
		OutputDebugStringW(L"[Bootstrap] Created CLR instance\n");
		// Get the runtime information for the particular version of .NET
		hr = pClrMetaHost->GetRuntime(dotNetVersion, IID_PPV_ARGS(&pClrRuntimeInfo));
		if (hr == S_OK)
		{
			OutputDebugStringW(L"[Bootstrap] Got CLR runtime\n");
			// Check if the specified runtime can be loaded into the process. This
			// method will take into account other runtimes that may already be
			// loaded into the process and set pbLoadable to TRUE if this runtime can
			// be loaded in an in-process side-by-side fashion.
			BOOL fLoadable;
			hr = pClrRuntimeInfo->IsLoadable(&fLoadable);
			if ((hr == S_OK) && fLoadable)
			{
				OutputDebugStringW(L"[Bootstrap] Runtime is loadable!\n");
				// Load the CLR into the current process and return a runtime interface
				// pointer.
				hr = pClrRuntimeInfo->GetInterface(CLSID_CLRRuntimeHost,
					IID_PPV_ARGS(&pClrRuntimeHost));
				if (hr == S_OK)
				{
					OutputDebugStringW(L"[Bootstrap] Got interface.\n");
					// Start it. This is okay to call even if the CLR is already running
					hr = pClrRuntimeHost->Start();
					//       if (hr == S_OK)
					//     {
					OutputDebugStringW(L"[Bootstrap] Started the runtime!\n");
					// Success!
					return pClrRuntimeHost;
					//   }
				}
			}
		}
	}
	// Cleanup if failed
	if (pClrRuntimeHost)
	{
		pClrRuntimeHost->Release();
		pClrRuntimeHost = NULL;
	}
	if (pClrRuntimeInfo)
	{
		pClrRuntimeInfo->Release();
		pClrRuntimeInfo = NULL;
	}
	if (pClrMetaHost)
	{
		pClrMetaHost->Release();
		pClrMetaHost = NULL;
	}

	return NULL;
}
