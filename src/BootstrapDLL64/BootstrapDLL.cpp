#include "stdafx.h"
#include "msgboxf.h"
#include <metahost.h>
#include <io.h>
#include <fcntl.h>
#pragma comment(lib, "mscoree.lib")

#include "BootstrapDLL.h"
#include <stdio.h>

DllExport void LoadManagedProject(const wchar_t * managedDllLocation)
{
	HRESULT hr;

	// All of this code is to spawn a console.
	if (false) {
		BOOL res = AllocConsole();
		HANDLE stdHandle;
		int hConsole;
		FILE* fp;
		stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
		hConsole = _open_osfhandle((long)stdHandle, _O_TEXT);
		fp = _fdopen(hConsole, "w");
		freopen_s(&fp, "CONOUT$", "w", stdout);
		// End of cosole spawning

		printf("[Bootstrap] Can you see me?\n");
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
			L"Argument",
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
