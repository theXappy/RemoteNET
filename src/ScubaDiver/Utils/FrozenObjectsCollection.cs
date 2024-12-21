using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ScubaDiver;

public class FrozenObjectsCollection
{
    private object _lock;
    Dictionary<object, ulong> _frozenObjects;
    private Task _freezerTask;
    private ManualResetEvent _unfreezeRequested;


    public FrozenObjectsCollection()
    {
        _lock = new object();
        _frozenObjects = new Dictionary<object, ulong>();
        _freezerTask = null;
        _unfreezeRequested = null;
    }

    /// <summary>
    /// Return the address where an object is pinned. If the object isn't pinned, false is returned.
    /// </summary>
    /// <returns>True if it was pinned, False if it wasn't</returns>
    public bool TryGetPinningAddress(object o, out ulong addr)
    {
        lock (_lock)
        {
            return _frozenObjects.TryGetValue(o, out addr);
        }
    }

    private void PinInternal(object[] newfrozenObjects)
    {
        lock (_lock)
        {
            if (newfrozenObjects.Length == 0)
            {
                UnpinAll();
                return;
            }

            ulong[] addresses = new ulong[newfrozenObjects.Length];
            ManualResetEvent frozenFeedback = new ManualResetEvent(false);
            ManualResetEvent unfreezeRequested = new ManualResetEvent(false);

            // Call freeze
            var func = FreezeFuncsFactory.Generate(newfrozenObjects.Length);
            Task freezerTask = Task.Run(() => func(newfrozenObjects, addresses, frozenFeedback, unfreezeRequested));

            // Wait for the freezer task to signal to us
            frozenFeedback.WaitOne();


            // Dispose of last Freezer
            _unfreezeRequested?.Set();
            _freezerTask?.Wait();

            // Save new Task & event
            _unfreezeRequested = unfreezeRequested;
            _freezerTask = freezerTask;

            // Now all addresses are set in the array. Re-create dict
            _frozenObjects.Clear();
            for (int i = 0; i < newfrozenObjects.Length; i++)
            {
                _frozenObjects[newfrozenObjects[i]] = addresses[i];
            }
        }
    }

    public ulong Pin(object o)
    {
        lock (_lock)
        {
            if (_frozenObjects.TryGetValue(o, out ulong addr))
                return addr;

            // Prepare parameters
            object[] objs = _frozenObjects.Keys.Concat(new object[] { o }).ToArray();
            PinInternal(objs);

            return _frozenObjects[o];
        }
    }

    public bool TryGetPinnedObject(ulong addr, out object o)
    {
        lock (_lock)
        {
            foreach (var frozenObject in _frozenObjects)
            {
                if (frozenObject.Value == addr)
                {
                    o = frozenObject.Key;
                    return true;
                }
            }

            o = null;
            return false;
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
            object[] objs = _frozenObjects
                .Where(kvp => kvp.Value != objAddress)
                .Select(kvp => kvp.Key)
                .ToArray();

            // Making sure that adress was even in the dictionary.
            // Otherwise, we don't need to re-pin all objects.
            if (objs.Length == _frozenObjects.Count)
                return false;

            PinInternal(objs);

            return true;
        }
    }

    public void UnpinAll()
    {
        lock (_lock)
        {
            // Dispose of last Freezer
            _unfreezeRequested?.Set();
            _freezerTask?.Wait();
            _unfreezeRequested = null;
            _freezerTask = null;

            _frozenObjects.Clear();
        }
    }
}