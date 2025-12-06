using System;
using System.Collections.Concurrent;
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
        /// Key: Unique hook identifier (method + position)
        /// Value: Dictionary mapping token to hook registration
        /// </summary>
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, HookRegistration>> _instanceHooks;

        public HookingCenter()
        {
            _instanceHooks = new ConcurrentDictionary<string, ConcurrentDictionary<int, HookRegistration>>();
        }

        /// <summary>
        /// Registers a hook callback for a specific instance (or all instances if instanceAddress is 0)
        /// </summary>
        /// <param name="uniqueHookId">Unique identifier for the method hook (includes position)</param>
        /// <param name="instanceAddress">Address of the instance to hook, or 0 for all instances</param>
        /// <param name="callback">The callback to invoke</param>
        /// <param name="token">Token identifying this hook registration</param>
        public void RegisterHook(string uniqueHookId, ulong instanceAddress, HarmonyWrapper.HookCallback callback, int token)
        {
            var registrations = _instanceHooks.GetOrAdd(uniqueHookId, _ => new ConcurrentDictionary<int, HookRegistration>());
            registrations[token] = new HookRegistration
            {
                InstanceAddress = instanceAddress,
                OriginalCallback = callback,
                Token = token
            };
        }

        /// <summary>
        /// Unregisters a hook callback by token
        /// </summary>
        /// <param name="uniqueHookId">Unique identifier for the method hook</param>
        /// <param name="token">Token identifying the hook registration to remove</param>
        public bool UnregisterHook(string uniqueHookId, int token)
        {
            if (_instanceHooks.TryGetValue(uniqueHookId, out var registrations))
            {
                bool removed = registrations.TryRemove(token, out _);
                
                if (removed && registrations.IsEmpty)
                {
                    _instanceHooks.TryRemove(uniqueHookId, out _);
                }
                
                return removed;
            }
            return false;
        }

        /// <summary>
        /// Creates a unified callback that dispatches to instance-specific callbacks.
        /// This wraps the individual callbacks to filter by instance.
        /// </summary>
        /// <param name="uniqueHookId">Unique identifier for the method hook</param>
        /// <param name="instanceResolver">Function to resolve an object to its address</param>
        /// <returns>A callback that handles instance filtering</returns>
        public HarmonyWrapper.HookCallback CreateUnifiedCallback(string uniqueHookId, Func<object, ulong> instanceResolver)
        {
            return (object instance, object[] args, ref object retValue) =>
            {
                if (!_instanceHooks.TryGetValue(uniqueHookId, out var registrations) || registrations.IsEmpty)
                {
                    // No callbacks registered, call original
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
                    catch
                    {
                        // If resolution fails, treat as 0 (unknown)
                        instanceAddress = 0;
                    }
                }

                // Invoke all matching callbacks
                bool callOriginal = true;

                foreach (var kvp in registrations)
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
            return _instanceHooks.TryGetValue(uniqueHookId, out var dict) && !dict.IsEmpty;
        }

        /// <summary>
        /// Gets the count of registered hooks for a method
        /// </summary>
        public int GetHookCount(string uniqueHookId)
        {
            if (_instanceHooks.TryGetValue(uniqueHookId, out var dict))
            {
                return dict.Count;
            }
            return 0;
        }
    }
}
