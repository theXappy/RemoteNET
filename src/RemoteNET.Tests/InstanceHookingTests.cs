//using Xunit;
//using RemoteNET;
//using RemoteNET.Common;
//using ScubaDiver.API.Hooking;
//using System.Reflection;

//namespace RemoteNET.Tests;

///// <summary>
///// Tests for instance-specific hooking functionality.
///// These tests demonstrate the new API for hooking methods on specific instances.
///// </summary>
//public class InstanceHookingTests
//{
//    // NOTE: These are integration tests that require a running target process
//    // They serve as examples of the API usage and will be skipped if no target is available
    
//    [Fact(Skip = "Integration test - requires target process")]
//    public void HookSpecificInstance_OnlyTriggersForThatInstance()
//    {
//        // Arrange
//        // var app = RemoteAppFactory.Connect(...);
//        // var instances = app.QueryInstances("MyClass").ToList();
//        // var instance1 = app.GetRemoteObject(instances[0]);
//        // var instance2 = app.GetRemoteObject(instances[1]);
//        // var method = instance1.GetRemoteType().GetMethod("SomeMethod");
        
//        // int hook1Called = 0;
//        // int hook2Called = 0;
        
//        // Act - Hook only instance1
//        // instance1.Hook(method, HarmonyPatchPosition.Prefix, 
//        //     (HookContext ctx, dynamic inst, dynamic[] args, ref dynamic ret) =>
//        //     {
//        //         hook1Called++;
//        //     });
        
//        // Invoke method on both instances
//        // instance1.Dynamify().SomeMethod();
//        // instance2.Dynamify().SomeMethod();
        
//        // Assert
//        // Assert.Equal(1, hook1Called); // Only instance1 hook should trigger
//        // Assert.Equal(0, hook2Called); // instance2 was not hooked
//    }
    
//    [Fact(Skip = "Integration test - requires target process")]
//    public void HookMultipleInstances_EachTriggersItsOwnHook()
//    {
//        // Arrange
//        // var app = RemoteAppFactory.Connect(...);
//        // var instances = app.QueryInstances("MyClass").Take(3).ToList();
//        // var remoteObjects = instances.Select(i => app.GetRemoteObject(i)).ToList();
//        // var method = remoteObjects[0].GetRemoteType().GetMethod("SomeMethod");
        
//        // var callCounts = new Dictionary<int, int>();
        
//        // Act - Hook each instance
//        // for (int i = 0; i < remoteObjects.Count; i++)
//        // {
//        //     int instanceIndex = i; // Capture for closure
//        //     callCounts[instanceIndex] = 0;
//        //     
//        //     remoteObjects[i].Hook(method, HarmonyPatchPosition.Prefix,
//        //         (HookContext ctx, dynamic inst, dynamic[] args, ref dynamic ret) =>
//        //         {
//        //             callCounts[instanceIndex]++;
//        //         });
//        // }
        
//        // Invoke method on each instance
//        // foreach (var obj in remoteObjects)
//        // {
//        //     obj.Dynamify().SomeMethod();
//        // }
        
//        // Assert - Each hook should have been called exactly once
//        // foreach (var kvp in callCounts)
//        // {
//        //     Assert.Equal(1, kvp.Value);
//        // }
//    }
    
//    [Fact(Skip = "Integration test - requires target process")]
//    public void HookWithoutInstance_TriggersForAllInstances()
//    {
//        // Arrange
//        // var app = RemoteAppFactory.Connect(...);
//        // var type = app.GetRemoteType("MyClass");
//        // var method = type.GetMethod("SomeMethod");
//        // var instances = app.QueryInstances("MyClass").Take(3).ToList();
//        // var remoteObjects = instances.Select(i => app.GetRemoteObject(i)).ToList();
        
//        // int totalCalls = 0;
        
//        // Act - Hook without specifying instance (global hook)
//        // app.HookingManager.HookMethod(method, HarmonyPatchPosition.Prefix,
//        //     (HookContext ctx, dynamic inst, dynamic[] args, ref dynamic ret) =>
//        //     {
//        //         totalCalls++;
//        //     });
        
//        // Invoke method on all instances
//        // foreach (var obj in remoteObjects)
//        // {
//        //     obj.Dynamify().SomeMethod();
//        // }
        
//        // Assert - Hook should trigger for all instances
//        // Assert.Equal(remoteObjects.Count, totalCalls);
//    }
    
//    [Fact(Skip = "Integration test - requires target process")]
//    public void PatchMethod_WithInstanceSpecificHooks()
//    {
//        // Arrange
//        // var app = RemoteAppFactory.Connect(...);
//        // var instance = app.GetRemoteObject(app.QueryInstances("MyClass").First());
//        // var method = instance.GetRemoteType().GetMethod("SomeMethod");
        
//        // bool prefixCalled = false;
//        // bool postfixCalled = false;
//        // bool finalizerCalled = false;
        
//        // Act - Patch with multiple hooks on specific instance
//        // instance.Patch(
//        //     method,
//        //     prefix: (HookContext ctx, dynamic inst, dynamic[] args, ref dynamic ret) =>
//        //     {
//        //         prefixCalled = true;
//        //     },
//        //     postfix: (HookContext ctx, dynamic inst, dynamic[] args, ref dynamic ret) =>
//        //     {
//        //         postfixCalled = true;
//        //     },
//        //     finalizer: (HookContext ctx, dynamic inst, dynamic[] args, ref dynamic ret) =>
//        //     {
//        //         finalizerCalled = true;
//        //     });
        
//        // Invoke method
//        // instance.Dynamify().SomeMethod();
        
//        // Assert
//        // Assert.True(prefixCalled);
//        // Assert.True(postfixCalled);
//        // Assert.True(finalizerCalled);
//    }

//    /// <summary>
//    /// Demonstrates the API for instance-specific hooking.
//    /// This is a documentation/example test.
//    /// </summary>
//    [Fact(Skip = "Example/Documentation test")]
//    public void ExampleUsage_InstanceSpecificHooking()
//    {
//        // This test demonstrates the complete API for instance-specific hooking
        
//        // 1. Connect to remote app
//        // var app = RemoteAppFactory.Connect(endpoint);
        
//        // 2. Get instances
//        // var instances = app.QueryInstances("TargetClass.FullName");
//        // var instance1 = app.GetRemoteObject(instances.First());
//        // var instance2 = app.GetRemoteObject(instances.Skip(1).First());
        
//        // 3. Get method to hook
//        // var targetType = instance1.GetRemoteType();
//        // var methodToHook = targetType.GetMethod("MethodName");
        
//        // 4. Hook specific instance - Option A: Using RemoteObject.Hook()
//        // instance1.Hook(
//        //     methodToHook,
//        //     HarmonyPatchPosition.Prefix,
//        //     (HookContext context, dynamic instance, dynamic[] args, ref dynamic retValue) =>
//        //     {
//        //         Console.WriteLine($"Instance 1 method called with {args.Length} arguments");
//        //         // context.skipOriginal = true; // Optional: skip original method
//        //     });
        
//        // 5. Hook specific instance - Option B: Using HookingManager
//        // app.HookingManager.HookMethod(
//        //     methodToHook,
//        //     HarmonyPatchPosition.Prefix,
//        //     (HookContext context, dynamic instance, dynamic[] args, ref dynamic retValue) =>
//        //     {
//        //         Console.WriteLine($"Instance 2 method called");
//        //     },
//        //     instance2); // Pass the instance as the last parameter
        
//        // 6. Hook all instances (previous behavior, still supported)
//        // app.HookingManager.HookMethod(
//        //     methodToHook,
//        //     HarmonyPatchPosition.Postfix,
//        //     (HookContext context, dynamic instance, dynamic[] args, ref dynamic retValue) =>
//        //     {
//        //         Console.WriteLine($"Any instance method called");
//        //     }); // No instance parameter = hooks all instances
//    }
//}
