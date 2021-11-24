#include "stdafx.h"
#include "msgboxf.h"
#include <metahost.h>
#include <io.h>
#include <fcntl.h>
#pragma comment(lib, "mscoree.lib")

#include "BootstrapDLL.h"
#include <stdio.h>

void DebugOut(wchar_t* fmt, ...)
{
	va_list argp;
	va_start(argp, fmt);
	wchar_t dbg_out[4096];
	vswprintf_s(dbg_out, fmt, argp);
	va_end(argp);
	OutputDebugString(dbg_out);
	// fputws is like `_putws` (which is like `puts` but for wchar_t) but doesnt append a new line
	fputws(dbg_out, stdout);
}

enum FrameworkType ParseFrameworkType(const std::wstring& framework)
{
	if (icase_cmp(framework, L"netcoreapp3.0")
		|| icase_cmp(framework, L"netcoreapp3.1")
		|| icase_cmp(framework, L"net5.0-windows")
		|| icase_cmp(framework, L"net6.0-windows"))
	{
		return FrameworkType::NET_CORE;
	}

	return FrameworkType::NET_FRAMEWORK;
}


DllExport void LoadManagedProject(const wchar_t* bootstrapDllArg)
{
	BOOL consoleAllocated;
	HRESULT hr;

	const auto parts = split(bootstrapDllArg, L"*");

	if (parts.size() < 3)
	{
		DebugOut(L"Not enough parameters.");
		return;
	}

	const auto& managedDllLocation = parts.at(0);
	const auto& scubaDiverArg = parts.at(1);
	const auto& framework = parts.at(2);

	std::wcout << framework << std::endl;
	std::wcout << managedDllLocation << std::endl;
	std::wcout << scubaDiverArg << std::endl;

	// All of this code is to spawn a console.
	if (true) {
		consoleAllocated = AllocConsole();
		if (consoleAllocated) {
			DebugOut(L"[Bootstrap] AllocConsole returned: %s\n", consoleAllocated ? "True" : "False");
			HANDLE stdHandle;
			int hConsole;
			FILE* fp;
			stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
			DebugOut(L"[Bootstrap] stdHandle = %d\n", stdHandle);
			hConsole = _open_osfhandle((intptr_t)stdHandle, _O_TEXT);
			DebugOut(L"[Bootstrap] hConsole = %d\n", hConsole);
			fflush(stdout);
			fp = _fdopen(hConsole, "w");
			freopen_s(&fp, "CONOUT$", "w", stdout);
			// End of cosole spawning
		}
		DebugOut(L"[Bootstrap] Can you see me? v2\n");
		DebugOut(L"[Bootstrap] managedDllLocation = %ls\n", managedDllLocation);
		DebugOut(L"[Bootstrap] scubaDiverArg = %ls\n", scubaDiverArg);
		fflush(stdout);
	}

	ICLRRuntimeHost* pClr;
	FrameworkType frameworkType = ParseFrameworkType(framework);

	if (frameworkType == FrameworkType::NET_CORE)
	{
		// Secure a handle to the Core (3/5/6) CLR 
		pClr = StartCLRCore();
	}
	else if (frameworkType == FrameworkType::NET_FRAMEWORK)
	{
		// Secure a handle to the CLR v4.0
		pClr = StartCLR(L"v4.0.30319");
	}
	else
	{
		DebugOut(L"Invalid framework type\n");
		return;
	}

	if (pClr != NULL)
	{
		DWORD result;
		hr = pClr->ExecuteInDefaultAppDomain(
			managedDllLocation.c_str(),
			L"ScubaDiver.Diver",
			L"EntryPoint",
			scubaDiverArg.c_str(),
			&result);
	}
	else {
		msgboxf("[Bootstrap] could not spawn CRL\n");
	}

	if (consoleAllocated) {
		FreeConsole();
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
		DebugOut(L"[Bootstrap] Created CLR instance\n");
		// Get the runtime information for the particular version of .NET
		hr = pClrMetaHost->GetRuntime(dotNetVersion, IID_PPV_ARGS(&pClrRuntimeInfo));
		if (hr == S_OK)
		{
			DebugOut(L"[Bootstrap] Got CLR runtime\n");
			// Check if the specified runtime can be loaded into the process. This
			// method will take into account other runtimes that may already be
			// loaded into the process and set pbLoadable to TRUE if this runtime can
			// be loaded in an in-process side-by-side fashion.
			BOOL fLoadable;
			hr = pClrRuntimeInfo->IsLoadable(&fLoadable);
			if ((hr == S_OK) && fLoadable)
			{
				DebugOut(L"[Bootstrap] Runtime is loadable!\n");
				// Load the CLR into the current process and return a runtime interface
				// pointer.
				hr = pClrRuntimeInfo->GetInterface(CLSID_CLRRuntimeHost,
					IID_PPV_ARGS(&pClrRuntimeHost));
				if (hr == S_OK)
				{
					DebugOut(L"[Bootstrap] Got interface.\n");
					// Start it. This is okay to call even if the CLR is already running
					hr = pClrRuntimeHost->Start();
					DebugOut(L"[Bootstrap] Started the runtime!\n");
					// Success!
					return pClrRuntimeHost;
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


typedef HRESULT(STDAPICALLTYPE* FnGetNETCoreCLRRuntimeHost)(REFIID riid, IUnknown** pUnk);

ICLRRuntimeHost* StartCLRCore()
{
	DebugOut(L"Getting handle for coreclr.dll...");
	auto* const coreCLRModule = ::GetModuleHandle(L"coreclr.dll");

	if (!coreCLRModule)
	{
		DebugOut(L"Could not get handle for coreclr.dll.");
		return nullptr;
	}

	DebugOut(L"Got handle for coreclr.dll.");

	DebugOut(L"Getting handle for GetCLRRuntimeHost...");

	const auto pfnGetCLRRuntimeHost = reinterpret_cast<FnGetNETCoreCLRRuntimeHost>(::GetProcAddress(coreCLRModule, "GetCLRRuntimeHost"));
	if (!pfnGetCLRRuntimeHost)
	{
		DebugOut(L"Could not get handle for GetCLRRuntimeHost.");
		return nullptr;
	}

	DebugOut(L"Got handle for GetCLRRuntimeHost.");

	DebugOut(L"Trying to get runtime host...");

	ICLRRuntimeHost* clrRuntimeHost = nullptr;
	const auto hr = pfnGetCLRRuntimeHost(IID_ICLRRuntimeHost, reinterpret_cast<IUnknown**>(&clrRuntimeHost));

	if (FAILED(hr))
	{
		DebugOut(L"Could not get runtime host.");
		return nullptr;
	}

	DebugOut(L"Got runtime host.");

	return clrRuntimeHost;
}