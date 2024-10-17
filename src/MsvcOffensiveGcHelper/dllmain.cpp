// dllmain.cpp : Defines the entry point for the DLL application.
#include "pch.h"
#include <stdio.h>
#include <stdlib.h>
#include <stdbool.h>
#include <windows.h>

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





#define EXPORT __declspec(dllexport)

#define INITIAL_SIZE 10

void** trackedAddresses = NULL;
size_t trackedCount = 0;
size_t arraySize = INITIAL_SIZE;

// Pointer to the original free function
void (*OriginalFree)(void* ptr) = NULL;
// Pointer to the original "_free" function
void (*Original_free)(void* ptr) = NULL;
// Pointer to the original "_free_dbg" function
void (*Original_free_dbg)(void* ptr) = NULL;

// ----------------
// Address Tracking
// ----------------

// Helper to grow the array if necessary
void GrowArrayIfNeeded() {
    if (trackedCount >= arraySize) {
        arraySize *= 2;
        trackedAddresses = (void**)realloc(trackedAddresses, arraySize * sizeof(void*));
    }
}

// Exported method to add an address to the tracking list
EXPORT void AddAddress(void* address) {
    if (!trackedAddresses) {
        trackedAddresses = (void**)malloc(arraySize * sizeof(void*));
    }
    GrowArrayIfNeeded();
    trackedAddresses[trackedCount++] = address;
}

// Exported method to remove an address from the tracking list
EXPORT void RemoveAddress(void* address) {
    for (size_t i = 0; i < trackedCount; ++i) {
        if (trackedAddresses[i] == address) {
            trackedAddresses[i] = trackedAddresses[--trackedCount];  // Replace with the last element
            break;
        }
    }
}

// ----------------
// Hooks
// ----------------

#define MAX_HOOKS 10

// Array of original free function pointers
void* originalFreeFunctions[MAX_HOOKS] = { nullptr };

// Macro to define the hook function for each index
// garbgeOrDebugArg is an ugly hack to support both free/_free (1 argument) and _free_dbg (2 arguments)
// I'm risking violating the stack and this only works because both my function and the calles use `cdecl`
#define DEFINE_HOOK_FOR_FREE(index) \
    EXPORT void HookForFree##index(void* ptr, void* garbgeOrDebugArg) { \
        debugf("[mogHelper] HookForFree"#index" called for ptr = %p\n", ptr); \
        for (size_t i = 0; i < trackedCount; ++i) { \
            if (trackedAddresses[i] == ptr) { \
                debugf("[mogHelper] HookForFree"#index" ptr = %p found in list! Not freeing\n", ptr); \
                return; /* Do not free */ \
            } \
        } \
        debugf("[mogHelper] HookForFree"#index" ptr = %p not in list! Freeing\n", ptr); \
        if (originalFreeFunctions[index] != nullptr) { \
            reinterpret_cast<void(*)(void*, void*)>(originalFreeFunctions[index])(ptr, garbgeOrDebugArg); \
        } else { \
            debugf("[mogHelper][ERROR] HookForFree"#index" could not free ptr = %p because OriginalFree func ptr was null...\n", ptr); \
        } \
    }


// Use the macro to define the hook functions for each slot
DEFINE_HOOK_FOR_FREE(0)
DEFINE_HOOK_FOR_FREE(1)
DEFINE_HOOK_FOR_FREE(2)
DEFINE_HOOK_FOR_FREE(3)
DEFINE_HOOK_FOR_FREE(4)
DEFINE_HOOK_FOR_FREE(5)
DEFINE_HOOK_FOR_FREE(6)
DEFINE_HOOK_FOR_FREE(7)
DEFINE_HOOK_FOR_FREE(8)
DEFINE_HOOK_FOR_FREE(9)

// Dynamically assign a slot to a module, ensuring no duplicates
EXPORT int AllocateHookForModule(void* originalFree) {
    // Get existing index for free or allocate a new index
    for (int i = 0; i < MAX_HOOKS; ++i) {
        if (originalFreeFunctions[i] == originalFree) {
            return i;  // Return the index if the function is already assigned
        }
        if (originalFreeFunctions[i] == nullptr) {
            originalFreeFunctions[i] = originalFree;
            return i;  // Return the index of the newly assigned slot
        }
    }

    return -1;
}




























// Free resources when done
BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved) {
    switch (ul_reason_for_call) {
    case DLL_PROCESS_ATTACH:
        trackedAddresses = NULL;
        trackedCount = 0;
        arraySize = INITIAL_SIZE;
        OriginalFree = NULL;
        break;
    case DLL_PROCESS_DETACH:
        if (trackedAddresses) {
            free(trackedAddresses);
        }
        break;
    }
    return TRUE;
}
