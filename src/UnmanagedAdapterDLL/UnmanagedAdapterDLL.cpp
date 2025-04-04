#include "stdafx.h"
#include "msgboxf.h"
#include <metahost.h>
#include <io.h>
#include <fcntl.h>
#include <corerror.h>
#pragma comment(lib, "mscoree.lib")

#include "UnmanagedAdapter.h"
#include "promptf.h"
#include <stdio.h>

void DebugOut(wchar_t* fmt, ...)
{
#ifdef _DEBUG
	va_list argp;
	va_start(argp, fmt);
	wchar_t dbg_out[4096];
	vswprintf_s(dbg_out, fmt, argp);
	va_end(argp);
	OutputDebugString(dbg_out);
	// fputws is like `_putws` (which is like `puts` but for wchar_t) but doesnt append a new line
	fputws(dbg_out, stdout);
#endif
}

enum FrameworkType ParseFrameworkType(const std::wstring& framework)
{
	if (icase_cmp(framework, L"netcoreapp3.0")
		|| icase_cmp(framework, L"netcoreapp3.1")
		|| icase_cmp(framework, L"net5.0-windows")
		|| icase_cmp(framework, L"net6.0-windows")
		|| icase_cmp(framework, L"net7.0-windows")
		|| icase_cmp(framework, L"net8.0-windows")
		|| icase_cmp(framework, L"net9.0-windows")
		|| icase_cmp(framework, L"native")
		)
	{
		return FrameworkType::NET_CORE;
	}

	return FrameworkType::NET_FRAMEWORK;
}

bool ShouldOpenDebugConosle() {
//#if _DEBUG
//	return true;
//#else
	GetEnvironmentVariable(L"REMOTE_NET_UA_MAGIC_DEBUG", NULL, 0);
	return GetLastError() != ERROR_ENVVAR_NOT_FOUND;
//#endif
}

DllExport void AdapterEntryPoint(const wchar_t* adapterDllArg)
{
	BOOL consoleAllocated = false;
	HRESULT hr;

	const auto parts = split(adapterDllArg, L"*");

	if (parts.size() < 5)
	{
		DebugOut(L"Not enough parameters.");
		return;
	}

	const auto& managedDllLocation = parts.at(0);
	const auto& managedDllClass = parts.at(1);
	const auto& managedDllFunction = parts.at(2);
	const auto& scubaDiverArg = parts.at(3);
	const auto& framework = parts.at(4);


	if (ShouldOpenDebugConosle()) {
		// All of this code is to spawn a console.
		consoleAllocated = AllocConsole();
		if (consoleAllocated) {
			HANDLE stdHandle;
			int hConsole;
			FILE* fp;
			stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
			hConsole = _open_osfhandle((intptr_t)stdHandle, _O_TEXT);
			fp = _fdopen(hConsole, "w");
			freopen_s(&fp, "CONOUT$", "w", stdout);
			// End of console spawning
			DebugOut(L"[UnmanagedAdapter] ConsoleAllocated and redirected in UnmanagedAdapter\n");
			DebugOut(L"[UnmanagedAdapter] WARNING: Console allocation from UA might be buggy.Consider using Diver's console allocation\n");
			DebugOut(L"[UnmanagedAdapter] WARNING: Consider using Diver's console allocation (Enable .NET env var and disable UA env var)\n");
		}
		else {
			DebugOut(L"[UnmanagedAdapter] AllocConsole returned False. Console already existed.\n");
		}
		DebugOut(L"[UnmanagedAdapter] Can you see me? v2\n");
		DebugOut(L"[UnmanagedAdapter] managedDllLocation = %s\n", managedDllLocation.c_str());
		DebugOut(L"[UnmanagedAdapter] scubaDiverArg = %s\n", scubaDiverArg.c_str());

		fflush(stdout);
	}

	ICLRRuntimeHost* pClr;
	FrameworkType frameworkType = ParseFrameworkType(framework);

	if (frameworkType == FrameworkType::NET_CORE)
	{
		DebugOut(L"[UnmanagedAdapter] Securing a handle to the Core (3/5/6/7/8) CLR \n");
		// Secure a handle to the Core (3/5/6/7/...) CLR 
		pClr = StartCLRCore();
		DebugOut(L"[UnmanagedAdapter] StartCLRCore ended with res: %p\n", pClr);
	}
	else if (frameworkType == FrameworkType::NET_FRAMEWORK)
	{
		DebugOut(L"[UnmanagedAdapter] Securing a handle to the CLR v4.0 \n");
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
		if (consoleAllocated) {
			//DebugOut(L"[UnmanagedAdapter] Freeing temp console\n");
			//FreeConsole();
		}
		DWORD result;
		DebugOut(L"[UnmanagedAdapter] ExecuteInDefaultAppDomain(%s, %s, %s, %s)\n",
			managedDllLocation.c_str(),
			managedDllClass.c_str(),
			managedDllFunction.c_str(),
			scubaDiverArg.c_str());

		hr = pClr->ExecuteInDefaultAppDomain(
			managedDllLocation.c_str(),
			managedDllClass.c_str(),
			managedDllFunction.c_str(),
			scubaDiverArg.c_str(),
			&result);

		DebugOut(L"[UnmanagedAdapter] ExecuteInDefaultAppDomain(...) returned %d\n", hr);
		if (hr == 0x80070002 && frameworkType == FrameworkType::NET_FRAMEWORK) {
			DebugOut(L"[UnmanagedAdapter] %d (0x%x) for .NET FRAMEWORK. Is this a newly relesaed .NET version and Unmanaged Adapter was not updated for it?", hr, hr);
		}
	}
	else {
		msgboxf("[UnmanagedAdapter] could not spawn CLR\n");
	}

}


DllExport void PromptEntryPoint()
{
	size_t size = 1024;
	char* prompt = new char[size];
	wchar_t* wcprompt = new wchar_t[size];

	promptf(prompt, "Insert UnmanagedAdapter's arguments:");
	// Convert to wchar_t
	size_t convertedChars = 0;
	mbstowcs_s(&convertedChars, wcprompt, size, prompt, _TRUNCATE);
	AdapterEntryPoint(wcprompt);
}

typedef HRESULT(STDAPICALLTYPE* FnGetNETCoreCLRRuntimeHost)(REFIID riid, IUnknown** pUnk);

ICLRRuntimeHost* StartCLRCore()
{
	auto* const coreCLRModule = ::GetModuleHandle(L"coreclr.dll");

	if (!coreCLRModule)
	{
		return nullptr;
	}

	const auto pfnGetCLRRuntimeHost = reinterpret_cast<FnGetNETCoreCLRRuntimeHost>(::GetProcAddress(coreCLRModule, "GetCLRRuntimeHost"));
	if (!pfnGetCLRRuntimeHost)
	{
		return nullptr;
	}

	ICLRRuntimeHost* clrRuntimeHost = nullptr;
	const auto hr = pfnGetCLRRuntimeHost(IID_ICLRRuntimeHost, reinterpret_cast<IUnknown**>(&clrRuntimeHost));

	if (FAILED(hr))
	{
		return nullptr;
	}

	return clrRuntimeHost;
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
		// Get the runtime information for the particular version of .NET
		hr = pClrMetaHost->GetRuntime(dotNetVersion, IID_PPV_ARGS(&pClrRuntimeInfo));
		if (hr == S_OK)
		{
			// Check if the specified runtime can be loaded into the process. This
			// method will take into account other runtimes that may already be
			// loaded into the process and set pbLoadable to TRUE if this runtime can
			// be loaded in an in-process side-by-side fashion.
			BOOL fLoadable;
			hr = pClrRuntimeInfo->IsLoadable(&fLoadable);
			if ((hr == S_OK) && fLoadable)
			{
				// Load the CLR into the current process and return a runtime interface
				// pointer.
				hr = pClrRuntimeInfo->GetInterface(CLSID_CLRRuntimeHost,
					IID_PPV_ARGS(&pClrRuntimeHost));
				if (hr == S_OK)
				{
					// Start it. This is okay to call even if the CLR is already running
					hr = pClrRuntimeHost->Start();
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
