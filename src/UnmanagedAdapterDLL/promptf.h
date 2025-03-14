#pragma once
#include <windows.h>

// A macro to allow invocation with a format string
#define promptf(output, format, ...) \
    { \
        int _promptf_len = snprintf(NULL, 0, format, __VA_ARGS__); \
        char* _promptf_buf = (char*)malloc(_promptf_len + 1); \
        snprintf(_promptf_buf, _promptf_len + 1, format, __VA_ARGS__); \
        PromptA(output, "Prompt", _promptf_buf); \
        free(_promptf_buf); \
    }

char gUserInput[4096];

#define ID_LABEL  200
#define ID_TEXTBOX  300

LPWORD lpwAlign(LPWORD lpIn)
{
	unsigned long long ul;

	ul = (unsigned long long)lpIn;
	ul = (ul + 3) & ~3ULL; // Align up to the nearest 4-byte boundary
	return (LPWORD)ul;
}

BOOL CALLBACK PromptDialogProc(HWND hwndDlg, UINT message, WPARAM wParam, LPARAM lParam)
{
	if (message != WM_COMMAND)
		return FALSE;

	switch (LOWORD(wParam))
	{
	case IDOK: // OK button clicked or 'ENTER' pressed
		if (!GetDlgItemTextA(hwndDlg, ID_TEXTBOX, gUserInput, 4096))
			gUserInput[0] = '\0';
		EndDialog(hwndDlg, wParam);
		return TRUE;
	case WM_DESTROY: // 'X' button clicked
		gUserInput[0] = '\0';
		EndDialog(hwndDlg, wParam);
		return TRUE;
	default:
		return TRUE;
	}
}

void PromptA(char* output, const char* title, const char* body)
{
	output[0] = '\0';
	HGLOBAL hgbl = GlobalAlloc(GMEM_ZEROINIT, 1024);
	if (!hgbl) return;

	//-----------------------------------------------------------------
	// Define the dialog box
	//-----------------------------------------------------------------
	LPDLGTEMPLATE lpdt = (LPDLGTEMPLATE)GlobalLock(hgbl);
	if (!lpdt) return;
	*lpdt = DLGTEMPLATE{ WS_POPUP | WS_BORDER | WS_SYSMENU | DS_MODALFRAME | WS_CAPTION | DS_SETFONT, 0, 3, 100, 100, 320, 65 };

	LPDWORD lpdw = (LPDWORD)(lpdt + 1);
	*lpdw++ = 0x00000000; // No menu (0x....0000) and Dialog box class (0x0000....)

	int nchar;
	nchar = MultiByteToWideChar(CP_ACP, 0, title, -1, (LPWSTR)lpdw, 50);
	LPWORD lpw = (LPWORD)lpdw + nchar;

	// Set the font
	*lpw++ = 8;  // Font size
	nchar = MultiByteToWideChar(CP_ACP, 0, "MS Shell Dlg", -1, (LPWSTR)lpw, 50);
	lpw += nchar;

	//-----------------------------------------------------------------
	// Define a static text message
	//-----------------------------------------------------------------
	LPDLGITEMTEMPLATE lpdit = (LPDLGITEMTEMPLATE)lpwAlign(lpw);
	*lpdit = DLGITEMTEMPLATE{ WS_CHILD | WS_VISIBLE | SS_LEFT, 0, 10, 10, 290, 10, ID_LABEL };

	lpdw = (LPDWORD)(lpdit + 1);
	*lpdw++ = 0x0082FFFF; // "Static" class (Text block)

	nchar = MultiByteToWideChar(CP_ACP, 0, body, -1, (LPWSTR)lpdw, 50);
	lpw = (LPWORD)lpdw + nchar;
	*lpw++ = 0;             // No creation data

	//-----------------------------------------------------------------
	// Define a text box
	//-----------------------------------------------------------------
	lpdit = (LPDLGITEMTEMPLATE)lpwAlign(lpw);
	*lpdit = DLGITEMTEMPLATE{ ES_LEFT | WS_BORDER | WS_TABSTOP | WS_CHILD | WS_VISIBLE | ES_AUTOHSCROLL , 0, 10, 22, 300, 14, ID_TEXTBOX };

	lpdw = (LPDWORD)(lpdit + 1);
	*lpdw++ = 0x0081FFFF; // "edit" class (textbox)
	*lpdw++ = 0;             // No creation data
	lpw = (LPWORD)lpdw;

	//-----------------------------------------------------------------
	// Define a OK button
	//-----------------------------------------------------------------
	lpdit = (LPDLGITEMTEMPLATE)lpwAlign(lpw);
	*lpdit = DLGITEMTEMPLATE{ WS_CHILD | WS_VISIBLE | WS_TABSTOP, 0, 265, 45, 45, 14, IDOK };

	lpdw = (LPDWORD)(lpdit + 1);
	*lpdw++ = 0x0080FFFF; // Button class

	nchar = 1 + MultiByteToWideChar(CP_ACP, 0, "OK", -1, (LPWSTR)lpdw, 50);
	lpw = (LPWORD)lpdw + nchar;
	*lpw++ = 0;             // No creation data

	GlobalUnlock(hgbl);
	LRESULT ret = DialogBoxIndirect(NULL,
		(LPDLGTEMPLATE)hgbl,
		NULL,
		(DLGPROC)PromptDialogProc);
	GlobalFree(hgbl);
	if (ret != IDOK)
		gUserInput[0] = '\0';

	// Hack: Assuming user's buffer is large enough. This is not safe but IDC.
	strcpy_s(output, 4096, gUserInput);
}
