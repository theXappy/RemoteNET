using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace ScubaDiver
{
    /// <summary>
    /// This collection allows saving a "lock" for every element where:
    /// 1. Multiple threads can lock the same element at the same time
    /// 2. If a thread tries to re-lock an element which it already lock it gets notified about it
    /// 3. Also there's an API to block threads for blocking ANY element (temporarily)
    /// </summary>
    public class SmartLocksDict<T>
    {
        private ConcurrentDictionary<int, SmartLockThreadState> _threadStates = new();

        public class Entry
        {
            public object _lock = new();
            public HashSet<int> _holdersThreadIDs = new();
        }

        private ConcurrentDictionary<T, Entry> _dict = new();

        [Flags]
        public enum SmartLockThreadState
        {
            AllowAllLocks = 0, // Default state, if others aren't defined this one is implied
            ForbidLocking, // This thread is not allowed to lock any of the locks
            TemporarilyAllowLocks // When combined with ForbidLocking, it means the thread is GENERALLY not allowed to lock but temporarily it is.
        }

        public void SetSpecialThreadState(int tid, SmartLockThreadState state)
        {
            if (_threadStates.TryGetValue(tid, out var current))
            {
                if (state == SmartLockThreadState.AllowAllLocks)
                {
                    _threadStates.TryRemove(tid, out _);
                }
                else
                {
                    _threadStates.TryUpdate(tid, state, current);
                }
            }
            else
            {
                _threadStates.TryAdd(tid, state);
            }
        }

        public void Add(T item)
        {
            _dict.TryAdd(item, new Entry());
        }

        public void Remove(T item)
        {
            _dict.TryRemove(item, out _);
        }

        public enum AcquireResults
        {
            NoSuchItem,
            Acquired,
            AlreadyAcquireByCurrentThread,
            ThreadNotAllowedToLock
        }

        public AcquireResults Acquire(T item)
        {
            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (_threadStates.TryGetValue(currentThreadId, out SmartLockThreadState current))
            {
                if (current.HasFlag(SmartLockThreadState.ForbidLocking))
                {
                    if (!current.HasFlag(SmartLockThreadState.TemporarilyAllowLocks))
                    {
                        return AcquireResults.ThreadNotAllowedToLock;
                    }
                }
            }

            Entry entry;
            if (!_dict.TryGetValue(item, out entry))
            {
                return AcquireResults.NoSuchItem;
            }

            AcquireResults result;
            lock (entry._lock)
            {
                if (entry._holdersThreadIDs.Contains(currentThreadId))
                {
                    result = AcquireResults.AlreadyAcquireByCurrentThread;
                }
                else
                {
                    entry._holdersThreadIDs.Add(currentThreadId);
                    result = AcquireResults.Acquired;
                }
            }

            return result;
        }

        public void Release(T item)
        {
            Entry entry;
            if (!_dict.TryGetValue(item, out entry))
            {
                return;
            }

            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            lock (entry._lock)
            {
                entry._holdersThreadIDs.Remove(currentThreadId);
            }
        }
    }
}
