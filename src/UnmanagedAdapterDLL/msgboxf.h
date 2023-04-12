#pragma once
#include <stdio.h>
#include <stdlib.h>
#include <Windows.h>

// A macro to allow invocation with a format string
#define msgboxf(format, ...) \
    { \
        int _msgboxf_len = snprintf(NULL, 0, format, __VA_ARGS__); \
        char* _msgboxf_buf = (char*)malloc(_msgboxf_len + 1); \
        snprintf(_msgboxf_buf, _msgboxf_len + 1, format, __VA_ARGS__); \
        MessageBoxA(NULL, _msgboxf_buf, NULL, 0); \
        free(_msgboxf_buf); \
    }

#define debugf(format, ...) \
    { \
        int _debugf_len = snprintf(NULL, 0, format, __VA_ARGS__); \
        char* _debugf_buf = (char*)malloc(_debugf_len + 1); \
        snprintf(_debugf_buf, _debugf_len + 1, format, __VA_ARGS__); \
        OutputDebugStringA(_debugf_buf); \
        free(_debugf_buf); \
    }
