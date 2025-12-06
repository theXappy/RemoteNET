# Instance-Specific Hooking Example

This document demonstrates how to use the new instance-specific hooking feature in RemoteNET.

## Overview

Previously, when hooking a method, ALL invocations of that method across ALL instances would trigger the hook. Now you can hook a method on a SPECIFIC INSTANCE only.

## Basic Usage

### Hooking All Instances (Previous Behavior)

```csharp
using RemoteNET;
using RemoteNET.Common;
using ScubaDiver.API.Hooking;

// Connect to remote app
var app = RemoteAppFactory.Connect(...);

// Get the type and method to hook
var targetType = app.GetRemoteType("MyNamespace.MyClass");
var methodToHook = targetType.GetMethod("MyMethod");

// Hook ALL instances
app.HookingManager.HookMethod(
    methodToHook, 
    HarmonyPatchPosition.Prefix, 
    (HookContext context, dynamic instance, dynamic[] args, ref dynamic retValue) =>
    {
        Console.WriteLine($"Method called on instance: {instance}");
    }
);
```

### Hooking a Specific Instance (NEW)

```csharp
using RemoteNET;
using RemoteNET.Common;
using ScubaDiver.API.Hooking;

// Connect to remote app
var app = RemoteAppFactory.Connect(...);

// Get a specific instance to hook
var instances = app.QueryInstances("MyNamespace.MyClass");
var targetInstance = instances.First();
var remoteObject = app.GetRemoteObject(targetInstance);

// Get the method to hook
var targetType = remoteObject.GetRemoteType();
var methodToHook = targetType.GetMethod("MyMethod");

// Option 1: Hook using HookingManager with instance parameter
app.HookingManager.HookMethod(
    methodToHook, 
    HarmonyPatchPosition.Prefix, 
    (HookContext context, dynamic instance, dynamic[] args, ref dynamic retValue) =>
    {
        Console.WriteLine($"Method called on the SPECIFIC instance!");
    },
    remoteObject  // <-- Pass the specific instance here
);

// Option 2: Hook using the convenience method on RemoteObject (RECOMMENDED)
remoteObject.Hook(
    methodToHook,
    HarmonyPatchPosition.Prefix,
    (HookContext context, dynamic instance, dynamic[] args, ref dynamic retValue) =>
    {
        Console.WriteLine($"Method called on the SPECIFIC instance!");
    }
);
```

### Using Patch Method for Multiple Hooks

```csharp
// Get a specific instance
var remoteObject = app.GetRemoteObject(targetInstance);
var targetType = remoteObject.GetRemoteType();
var methodToHook = targetType.GetMethod("MyMethod");

// Patch with prefix, postfix, and finalizer on SPECIFIC instance
remoteObject.Patch(
    methodToHook,
    prefix: (HookContext ctx, dynamic inst, dynamic[] args, ref dynamic ret) =>
    {
        Console.WriteLine("PREFIX: Before method execution");
    },
    postfix: (HookContext ctx, dynamic inst, dynamic[] args, ref dynamic ret) =>
    {
        Console.WriteLine($"POSTFIX: After method execution, return value: {ret}");
    },
    finalizer: (HookContext ctx, dynamic inst, dynamic[] args, ref dynamic ret) =>
    {
        Console.WriteLine("FINALIZER: Always runs, even if exception occurred");
    }
);
```

## Multiple Hooks on Same Method

You can hook the same method on different instances:

```csharp
var instances = app.QueryInstances("MyNamespace.MyClass").Take(3);

int hookCounter = 0;
foreach (var candidate in instances)
{
    var remoteObj = app.GetRemoteObject(candidate);
    int instanceId = hookCounter++;
    
    remoteObj.Hook(
        methodToHook,
        HarmonyPatchPosition.Prefix,
        (HookContext ctx, dynamic inst, dynamic[] args, ref dynamic ret) =>
        {
            Console.WriteLine($"Hook triggered on instance #{instanceId}");
        }
    );
}

// Now each instance will trigger only its own hook
```

## Important Notes

1. **Instance Address Resolution**: The system uses the pinned object address to identify instances. For unpinned objects, it falls back to the object's identity hash code.

2. **Static Methods**: Instance-specific hooking doesn't apply to static methods (since they have no instance). For static methods, use the standard hooking approach without specifying an instance.

3. **Hook Cleanup**: When an instance-specific hook is removed, the underlying Harmony hook is only removed if it was the last hook for that method.

4. **Performance**: Instance-specific hooks add a small overhead to check the instance address on each invocation, but this is minimal compared to the callback overhead.

## Architecture

The implementation uses a `HookingCenter` class that:
- Tracks multiple hooks per method (one for each instance)
- Filters invocations based on instance address
- Manages hook cleanup when hooks are removed

When you hook a method on a specific instance:
1. The request includes the instance's address
2. ScubaDiver installs a single Harmony hook for that method (if not already hooked)
3. The hook callback checks if the current instance matches the registered instance address
4. Only matching invocations trigger the user callback

## Migration Guide

Existing code that hooks methods will continue to work unchanged. To add instance-specific hooking:

```csharp
// Before (hooks all instances)
app.HookingManager.HookMethod(method, pos, callback);

// After (hooks specific instance)
app.HookingManager.HookMethod(method, pos, callback, instanceObject);
// OR
instanceObject.Hook(method, pos, callback);
```
