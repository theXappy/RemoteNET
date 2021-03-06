#pragma once
#include <stdio.h>
#include <Windows.h>

// A macro to allow invocation with a format string
// Beware of overflows! Keep your prints short or expand the buffer.
#define msgboxf(format, ...)  \
    { \
        char msg[500]; \
        sprintf_s(msg, 500, format, __VA_ARGS__); \
        MessageBoxA(NULL, msg, NULL, 0); \
    }

#define debugf(format, ...)  \
    { \
        char msg[500]; \
        sprintf_s(msg, 500, format, __VA_ARGS__); \
        OutputDebugStringA(msg); \
    }
