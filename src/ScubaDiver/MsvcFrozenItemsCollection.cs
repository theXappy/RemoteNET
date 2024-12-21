using ScubaDiver.Rtti;
using System;
using System.Collections.Generic;

namespace ScubaDiver
{
    internal class MsvcFrozenItemsCollection
    {

        private object _lock = new();
        private Dictionary<ulong, List<TypeInfo>> _frozenItemsToDestructors = new();

        MsvcOffensiveGC _gc;

        public MsvcFrozenItemsCollection(MsvcOffensiveGC gc)
        {
            if (gc == null)
                throw new ArgumentNullException(nameof(gc));
            _gc = gc;
        }

        public ulong Pin(ulong objAddress)
        {
            lock (_lock)
            {
                _frozenItemsToDestructors.Add(objAddress, new List<TypeInfo>());

                _gc.Pin(objAddress, RegisterDestructor);

                return objAddress;
            }
        }

        public bool IsFrozen(ulong objAddress)
        {
            lock (_lock)
            {
                return _frozenItemsToDestructors.ContainsKey(objAddress);
            }
        }

        /// <summary>
        /// Unpins an object
        /// </summary>
        /// <returns>True if it was pinned, false if not.</returns>
        public bool Unpin(ulong objAddress)
        {
            lock (_lock)
            {
                _gc.Unpin(objAddress);
                if (!_frozenItemsToDestructors.Remove(objAddress, out var destructorsList))
                    return false;

                // TODO: run destructors
                foreach (var destructor in destructorsList)
                {
                    Logger.Debug($"[MsvcFrozenItemsCollection][WARNING] Trying to unpin object with pending destructor: {destructor} - NOT IMPLEMENTED!");
                }

                Logger.Debug($"[MsvcFrozenItemsCollection][INFO] Address 0x{objAddress:X16} removed from dict.");
                return true;
            }
        }

        private void RegisterDestructor(ulong objAddress, TypeInfo destructorType)
        {
            lock (_lock)
            {
                if (!_frozenItemsToDestructors.TryGetValue(objAddress, out List<TypeInfo> dtors))
                {
                    Logger.Debug($"[MsvcFrozenItemsCollection][WARNING] RegisterDestructor Invoked for address 0x{objAddress:X16} but it was not in the dict...");
                    return;
                }
                dtors.Add(destructorType);
            }
        }
    }
}
