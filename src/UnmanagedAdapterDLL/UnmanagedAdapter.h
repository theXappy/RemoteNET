#pragma once
#include "stdafx.h"
#include <metahost.h>
#include <locale>
#include <memory>
#include <string>
#include <array>
#include <vector>
#include <iostream>
#include <stdexcept>
#pragma comment(lib, "mscoree.lib")

// For exporting functions without name-mangling
#define DllExport extern "C" __declspec( dllexport )

// Our sole export for the time being
DllExport void AdapterEntryPoint(const wchar_t* managedDllLocation);

// Not exporting, so go ahead and name-mangle
ICLRRuntimeHost* StartCLR(LPCWSTR dotNetVersion);

ICLRRuntimeHost* StartCLRCore();


static std::vector<std::wstring> split(const std::wstring& input, const std::wstring& delimiter)
{
	std::vector<std::wstring> parts;
	std::wstring::size_type startIndex = 0;
	std::wstring::size_type endIndex;

	while ((endIndex = input.find(delimiter, startIndex)) < input.size())
	{
		auto val = input.substr(startIndex, endIndex - startIndex);
		parts.push_back(val);
		startIndex = endIndex + delimiter.size();
	}

	if (startIndex < input.size())
	{
		const auto val = input.substr(startIndex);
		parts.push_back(val);
	}

	return parts;
}

static bool icase_wchar_cmp(const wchar_t a, const wchar_t b)
{
	return std::tolower(a) == std::tolower(b);
}

static bool icase_cmp(std::wstring const& s1, std::wstring const& s2)
{
	return s1.size() == s2.size()
		&& std::equal(s1.begin(), s1.end(), s2.begin(), icase_wchar_cmp);
}

enum class FrameworkType
{
	UNKNOWN,
	NET_FRAMEWORK,
	NET_CORE
};