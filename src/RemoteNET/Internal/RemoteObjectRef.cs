using System;
using System.Linq;
using ScubaDiver.API;
using ScubaDiver.API.Interactions;
using ScubaDiver.API.Interactions.Dumps;

namespace RemoteNET.Internal
{
    internal class RemoteObjectRef
    {
        private bool _isReleased;
        private readonly TypeDump _typeInfo;
        private ObjectDump _remoteObjectInfo;
        private readonly DiverCommunicator _creatingCommunicator;

        // TODO: I think addresses as token should be reworked
        public ulong Token => _remoteObjectInfo.PinnedAddress;
        public DiverCommunicator Communicator => _creatingCommunicator;

        
        public RemoteObjectRef(ObjectDump remoteObjectInfo, TypeDump typeInfo, DiverCommunicator creatingCommunicator)
        {
            if (typeInfo == null)
            {
                throw new ArgumentNullException(nameof(typeInfo));
            }

            _remoteObjectInfo = remoteObjectInfo;
            _typeInfo = typeInfo;
            _creatingCommunicator = creatingCommunicator;
            _isReleased = false;
        }

        public TypeDump GetTypeDump()
        {
            return _typeInfo;
        }

        /// <summary>
        /// Gets the value of a remote field. Returned value might be a cached version unless <see cref="refresh"/> is set to True.
        /// </summary>
        /// <param name="name">Name of field to get the value of</param>
        /// <param name="refresh">Whether the value should be read again for this invocation or a cache version is good enough</param>
        public MemberDump GetFieldDump(string name, bool refresh = false)
        {
            ThrowIfReleased();
            if (refresh)
            {
                _remoteObjectInfo = _creatingCommunicator.DumpObject(_remoteObjectInfo.PinnedAddress, _remoteObjectInfo.Type);
            }

            var field = _remoteObjectInfo.Fields.Single(fld => fld.Name == name);
            if (!string.IsNullOrEmpty(field.RetrivalError))
                throw new Exception(
                    $"Field of the remote object could not be retrieved. Error: {field.RetrivalError}");

            // field has a value. Returning as-is for the user to parse
            return field;
        }
        /// <summary>
        /// Gets the value of a remote property. Returned value might be a cached version unless <see cref="refresh"/> is set to True.
        /// </summary>
        /// <param name="name">Name of property to get the value of</param>
        /// <param name="refresh">Whether the value should be read again for this invocation or a cache version is good enough</param>
        public MemberDump GetProperty(string name, bool refresh = false)
        {
            ThrowIfReleased();
            if (refresh)
            {
                throw new NotImplementedException("Refreshing property values not supported yet");
            }

            var property = _remoteObjectInfo.Properties.Single(prop => prop.Name == name);
            if (!string.IsNullOrEmpty(property.RetrivalError))
            {
                throw new Exception(
                    $"Property of the remote object could not be retrieved. Error: {property.RetrivalError}");
            }

            // property has a value. Returning as-is for the user to parse
            return property;
        }

        private void ThrowIfReleased()
        {
            if (_isReleased)
            {
                throw new ObjectDisposedException("Cannot use RemoteObjectRef object after `Release` have been called");
            }
        }

        public InvocationResults InvokeMethod(string methodName, string[] genericArgsFullTypeNames, ObjectOrRemoteAddress[] args)
        {
            ThrowIfReleased();
            return _creatingCommunicator.InvokeMethod(_remoteObjectInfo.PinnedAddress, _remoteObjectInfo.Type, methodName, genericArgsFullTypeNames, args);
        }

        public InvocationResults SetField(string fieldName, ObjectOrRemoteAddress newValue)
        {
            ThrowIfReleased();
            return _creatingCommunicator.SetField(_remoteObjectInfo.PinnedAddress, _remoteObjectInfo.Type, fieldName, newValue);
        }
        public InvocationResults GetField(string fieldName)
        {
            ThrowIfReleased();
            return _creatingCommunicator.GetField(_remoteObjectInfo.PinnedAddress, _remoteObjectInfo.Type, fieldName);
        }

        public void EventSubscribe(string eventName, DiverCommunicator.LocalEventCallback callbackProxy)
        {
            ThrowIfReleased();

            _creatingCommunicator.EventSubscribe(_remoteObjectInfo.PinnedAddress, eventName, callbackProxy);
        }

        public void EventUnsubscribe(string eventName, DiverCommunicator.LocalEventCallback callbackProxy)
        {
            ThrowIfReleased();

            _creatingCommunicator.EventUnsubscribe(callbackProxy);
        }

        /// <summary>
        /// Releases hold of the remote object in the remote process and the local proxy.
        /// </summary>
        public void RemoteRelease()
        {
            _creatingCommunicator.UnpinObject(_remoteObjectInfo.PinnedAddress);
            _isReleased = true;
        }

        public override string ToString()
        {
            return $"RemoteObjectRef. Address: {_remoteObjectInfo.PinnedAddress}, TypeFullName: {_typeInfo.Type}";
        }

        internal ObjectOrRemoteAddress GetItem(ObjectOrRemoteAddress key)
        {
            return _creatingCommunicator.GetItem(this.Token, key);
        }
    }
}