# Instance-Specific Hooking Implementation Details

This document provides technical details about the implementation of instance-specific hooking in RemoteNET.

## Problem Statement

Previously, when hooking a method in RemoteNET, ALL invocations of that method would trigger the hook, regardless of which instance was calling it. This was fine for static methods, but for instance methods, users often wanted to hook only a SPECIFIC instance.

## Solution Architecture

### Backend Changes (ScubaDiver)

#### 1. FunctionHookRequest Extension
- Added `InstanceAddress` field (ulong) to specify which instance to hook
- When `InstanceAddress` is 0, it means "hook all instances" (backward compatible)
- When `InstanceAddress` is non-zero, only hooks on that specific instance

#### 2. HookingCenter Class
A centralized manager that handles instance-specific hook registrations:

**Key Features:**
- Uses `ConcurrentDictionary<string, ConcurrentDictionary<int, HookRegistration>>` for O(1) operations
- Each method+position combination gets a unique ID
- Multiple hooks can be registered per method (one per instance)
- Thread-safe registration and unregistration

**How it Works:**
```
Method A + Prefix → uniqueHookId
    → Token 1 → (InstanceAddress: 0x1234, Callback: cb1)
    → Token 2 → (InstanceAddress: 0x5678, Callback: cb2)
    → Token 3 → (InstanceAddress: 0,     Callback: cb3) // All instances
```

When a hooked method is called:
1. The unified callback from HookingCenter is invoked
2. It resolves the current instance's address
3. It checks all registered hooks for this method
4. It invokes callbacks where:
   - `InstanceAddress == 0` (global hooks), OR
   - `InstanceAddress == current instance address` (instance-specific hooks)

#### 3. DiverBase Modifications
- Added `_hookingCenter` and `_harmonyHookLocks` fields
- Modified `HookFunctionWrapper` to:
  - Use per-method locks to prevent race conditions
  - Register callbacks with HookingCenter
  - Install Harmony hook only on first registration
  - Use HookingCenter's unified callback
- Modified `MakeUnhookMethodResponse` to:
  - Unregister from HookingCenter
  - Only remove Harmony hook when last callback is unregistered

#### 4. Instance Address Resolution
Both DotNetDiver and MsvcDiver implement `ResolveInstanceAddress`:

**DotNetDiver:**
- First tries to get pinned address from FrozenObjectsCollection
- Falls back to RuntimeHelpers.GetHashCode for unpinned objects

**MsvcDiver:**
- For NativeObject instances, uses the Address property
- Falls back to FrozenObjectsCollection or GetHashCode

### Frontend Changes (RemoteNET)

#### 1. DiverCommunicator
- Added optional `instanceAddress` parameter to `HookMethod`
- Defaults to 0 for backward compatibility

#### 2. RemoteHookingManager
- Updated `HookMethod` to accept optional `RemoteObject instance` parameter
- Added overload accepting `dynamic instance` to work with DynamicRemoteObject
- Tracks instance address in `PositionedLocalHook`
- Prevents duplicate hooks per instance+position combination
- Caches PropertyInfo for efficient dynamic→RemoteObject conversion

#### 3. RemoteObject Extensions
Both ManagedRemoteObject and UnmanagedRemoteObject now have:
- `Hook(method, position, callback)` - Convenience method for hooking this instance
- `Patch(method, prefix, postfix, finalizer)` - Convenience method for patching this instance

## Thread Safety

The implementation is thread-safe through several mechanisms:

1. **ConcurrentDictionary** usage in HookingCenter for all storage
2. **Per-method locks** in DiverBase for Harmony hook installation
3. **Atomic operations** for hook counting and removal
4. **Lock-free reads** for callback dispatching

## Performance Considerations

1. **Instance Resolution**: Pinned objects have O(1) lookup; unpinned objects use identity hash
2. **Hook Registration**: O(1) with ConcurrentDictionary
3. **Hook Unregistration**: O(1) removal
4. **Callback Dispatch**: O(n) where n = number of hooks on the method (typically small)
5. **Memory**: One HookRegistration per registered hook

## Backward Compatibility

All existing code continues to work:
- Hooks without instance parameter hook all instances (previous behavior)
- No API breaking changes
- New functionality is purely additive

## Example Call Flow

```
User Code:
    instance.Hook(method, Prefix, callback)
        ↓
RemoteHookingManager.HookMethod(method, Prefix, callback, instance)
        ↓
DiverCommunicator.HookMethod(method, Prefix, wrappedCallback, instanceAddress)
        ↓
ScubaDiver: DiverBase.HookFunctionWrapper()
    → Registers in HookingCenter
    → Installs Harmony hook (if first for method)
    → Returns token

When Method is Called:
    Harmony intercepts call
        ↓
    HookingCenter.UnifiedCallback(instance, args)
        ↓
    ResolveInstanceAddress(instance)
        ↓
    Check all registrations:
        if (reg.InstanceAddress == 0 || reg.InstanceAddress == current)
            → Invoke reg.Callback()
```

## Testing

See `InstanceHookingTests.cs` for test examples and `INSTANCE_HOOKING_EXAMPLE.md` for usage examples.

## Future Enhancements

Possible future improvements:
1. Support for hooking by instance hashcode (for unpinned objects)
2. Bulk hook registration/unregistration APIs
3. Hook metrics (call counts per instance)
4. Hook filtering by argument values
5. Conditional hooks (only invoke if predicate matches)

## Known Limitations

1. **Unpinned Objects**: For unpinned .NET objects, instance resolution uses identity hashcode, which may change across GC if objects move
2. **MSVC Objects**: Instance resolution depends on NativeObject wrapper or pinning
3. **Static Methods**: Instance-specific hooking doesn't apply (no instance to filter by)
4. **Performance Overhead**: Small overhead on each hooked method call to check instance address
