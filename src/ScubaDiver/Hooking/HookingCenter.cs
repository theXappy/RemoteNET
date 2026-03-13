using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace ScubaDiver.Hooking
{
    /// <summary>
    /// Centralized hooking manager that handles instance-specific hooks.
    /// When a method is hooked with a specific instance, this center wraps callbacks
    /// to filter invocations based on the instance address.
    /// </summary>
    public class HookingCenter
    {
        /// <summary>
        /// Information about a registered hook
        /// </summary>
        public class HookRegistration
        {
            public ulong InstanceAddress { get; set; }
            public HarmonyWrapper.HookCallback OriginalCallback { get; set; }
            public int Token { get; set; }
        }

        /// <summary>
        /// Information about a Harmony hook installation
        /// </summary>
        private class HarmonyHookInfo
        {
            public Action UnhookAction { get; set; }
            public ConcurrentDictionary<int, HookRegistration> Registrations { get; set; }
        }

        /// <summary>
        /// Key: Unique hook identifier (method + position)
        /// Value: Harmony hook info containing unhook action and registrations
        /// </summary>
        private readonly ConcurrentDictionary<string, HarmonyHookInfo> _harmonyHooks;
        
        /// <summary>
        /// Locks for synchronizing hook installation/uninstallation per unique hook ID
        /// </summary>
        private readonly ConcurrentDictionary<string, object> _hookLocks;

        public HookingCenter()
        {
            _harmonyHooks = new ConcurrentDictionary<string, HarmonyHookInfo>();
            _hookLocks = new ConcurrentDictionary<string, object>();
        }

        /// <summary>
        /// Registers a hook callback for a specific instance (or all instances if instanceAddress is 0)
        /// and installs the Harmony hook if this is the first registration for this method.
        /// </summary>
        /// <param name="uniqueHookId">Unique identifier for the method hook (includes position)</param>
        /// <param name="instanceAddress">Address of the instance to hook, or 0 for all instances</param>
        /// <param name="callback">The callback to invoke</param>
        /// <param name="token">Token identifying this hook registration</param>
        /// <param name="hookInstaller">Function that installs the Harmony hook and returns an unhook action</param>
        /// <param name="instanceResolver">Function to resolve an object to its address</param>
        /// <returns>True if this was the first hook and Harmony was installed, false otherwise</returns>
        public bool RegisterHookAndInstall(string uniqueHookId, ulong instanceAddress, HarmonyWrapper.HookCallback callback, int token, 
            Func<HarmonyWrapper.HookCallback, Action> hookInstaller, Func<object, ulong> instanceResolver)
        {
            object hookLock = _hookLocks.GetOrAdd(uniqueHookId, _ => new object());
            
            lock (hookLock)
            {
                // Get or create the harmony hook info
                var hookInfo = _harmonyHooks.GetOrAdd(uniqueHookId, _ => new HarmonyHookInfo
                {
                    Registrations = new ConcurrentDictionary<int, HookRegistration>()
                });
                
                // Add this registration
                hookInfo.Registrations[token] = new HookRegistration
                {
                    InstanceAddress = instanceAddress,
                    OriginalCallback = callback,
                    Token = token
                };
                
                // Check if we need to install the Harmony hook
                bool isFirstHook = hookInfo.Registrations.Count == 1;
                if (isFirstHook)
                {
                    // First hook for this method - install the actual Harmony hook
                    HarmonyWrapper.HookCallback unifiedCallback = CreateUnifiedCallback(uniqueHookId, instanceResolver);
                    hookInfo.UnhookAction = hookInstaller(unifiedCallback);
                    return true;
                }
                
                return false;
            }
        }

        /// <summary>
        /// Unregisters a hook callback by token and uninstalls the Harmony hook if this was the last registration.
        /// </summary>
        /// <param name="uniqueHookId">Unique identifier for the method hook</param>
        /// <param name="token">Token identifying the hook registration to remove</param>
        /// <returns>True if the hook was removed, false if not found</returns>
        public bool UnregisterHookAndUninstall(string uniqueHookId, int token)
        {
            if (!_harmonyHooks.TryGetValue(uniqueHookId, out var hookInfo))
                return false;
                
            object hookLock = _hookLocks.GetOrAdd(uniqueHookId, _ => new object());
            
            lock (hookLock)
            {
                bool removed = hookInfo.Registrations.TryRemove(token, out _);
                
                if (removed && hookInfo.Registrations.IsEmpty)
                {
                    // Last hook for this method - uninstall the Harmony hook
                    hookInfo.UnhookAction?.Invoke();
                    _harmonyHooks.TryRemove(uniqueHookId, out _);
                    _hookLocks.TryRemove(uniqueHookId, out _);
                }
                
                return removed;
            }
        }

        /// <summary>
        /// Creates a unified callback that dispatches to instance-specific callbacks.
        /// This wraps the individual callbacks to filter by instance.
        /// </summary>
        /// <param name="uniqueHookId">Unique identifier for the method hook</param>
        /// <param name="instanceResolver">Function to resolve an object to its address</param>
        /// <returns>A callback that handles instance filtering</returns>
        private HarmonyWrapper.HookCallback CreateUnifiedCallback(string uniqueHookId, Func<object, ulong> instanceResolver)
        {
            return (object instance, object[] args, ref object retValue) =>
            {
                if (!_harmonyHooks.TryGetValue(uniqueHookId, out HarmonyHookInfo hookInfo) || hookInfo.Registrations.IsEmpty)
                {
                    // This should ideally not happen since we only create unified callbacks when hooks exist
                    // If it does, it means hooks were removed between callback creation and invocation
                    Logger.Debug($"[HookingCenter] Warning: Unified callback invoked for {uniqueHookId} but no registrations found");
                    return true;
                }

                // Resolve the instance address
                ulong instanceAddress = 0;
                if (instance != null && instanceResolver != null)
                {
                    try
                    {
                        instanceAddress = instanceResolver(instance);
                    }
                    catch (Exception ex)
                    {
                        // Log the exception for debugging but continue with address 0
                        Logger.Debug($"[HookingCenter] Failed to resolve instance address for {uniqueHookId}: {ex.Message}");
                        instanceAddress = 0;
                    }
                }

                // Invoke all matching callbacks
                bool callOriginal = true;

                foreach (KeyValuePair<int, HookRegistration> kvp in hookInfo.Registrations)
                {
                    var registration = kvp.Value;
                    // Check if this callback matches
                    bool shouldInvoke = registration.InstanceAddress == 0 || // Global hook (all instances)
                                       registration.InstanceAddress == instanceAddress; // Instance-specific match

                    if (shouldInvoke)
                    {
                        bool thisCallOriginal = registration.OriginalCallback(instance, args, ref retValue);
                        // If any callback says skip original, we skip it
                        callOriginal = callOriginal && thisCallOriginal;
                    }
                }

                return callOriginal;
            };
        }

        /// <summary>
        /// Checks if there are any hooks registered for a specific method
        /// </summary>
        public bool HasHooks(string uniqueHookId)
        {
            return _harmonyHooks.TryGetValue(uniqueHookId, out var hookInfo) && !hookInfo.Registrations.IsEmpty;
        }

        /// <summary>
        /// Gets the count of registered hooks for a method
        /// </summary>
        public int GetHookCount(string uniqueHookId)
        {
            if (_harmonyHooks.TryGetValue(uniqueHookId, out var hookInfo))
            {
                return hookInfo.Registrations.Count;
            }
            return 0;
        }
    }
}
