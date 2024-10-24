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
        public ObjectDump RemoteObjectInfo { get; private set; }
        public DiverCommunicator CreatingCommunicator { get; private set; }

        // TODO: I think addresses as token should be reworked
        public ulong Token => RemoteObjectInfo.PinnedAddress;
        public DiverCommunicator Communicator => CreatingCommunicator;

        public RemoteObjectRef(ObjectDump remoteObjectInfo, TypeDump typeInfo, DiverCommunicator creatingCommunicator)
        {
            if (typeInfo == null)
            {
                throw new ArgumentNullException(nameof(typeInfo));
            }

            RemoteObjectInfo = remoteObjectInfo;
            _typeInfo = typeInfo;
            CreatingCommunicator = creatingCommunicator;
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
                RemoteObjectInfo = CreatingCommunicator.DumpObject(RemoteObjectInfo.PinnedAddress, RemoteObjectInfo.Type);
            }

            var field = RemoteObjectInfo.Fields.Single(fld => fld.Name == name);
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

            var property = RemoteObjectInfo.Properties.Single(prop => prop.Name == name);
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
            string typeFullName = $"{_typeInfo.Assembly}!{_typeInfo.Type}";
            return CreatingCommunicator.InvokeMethod(RemoteObjectInfo.PinnedAddress, typeFullName, methodName, genericArgsFullTypeNames, args);
        }

        public InvocationResults SetField(string fieldName, ObjectOrRemoteAddress newValue)
        {
            ThrowIfReleased();
            return CreatingCommunicator.SetField(RemoteObjectInfo.PinnedAddress, RemoteObjectInfo.Type, fieldName, newValue);
        }
        public InvocationResults GetField(string fieldName)
        {
            ThrowIfReleased();
            return CreatingCommunicator.GetField(RemoteObjectInfo.PinnedAddress, RemoteObjectInfo.Type, fieldName);
        }

        public void EventSubscribe(string eventName, DiverCommunicator.LocalEventCallback callbackProxy)
        {
            ThrowIfReleased();

            CreatingCommunicator.EventSubscribe(RemoteObjectInfo.PinnedAddress, eventName, callbackProxy);
        }

        public void EventUnsubscribe(string eventName, DiverCommunicator.LocalEventCallback callbackProxy)
        {
            ThrowIfReleased();

            CreatingCommunicator.EventUnsubscribe(callbackProxy);
        }

        /// <summary>
        /// Releases hold of the remote object in the remote process and the local proxy.
        /// </summary>
        public void RemoteRelease()
        {
            CreatingCommunicator.UnpinObject(RemoteObjectInfo.PinnedAddress);
            _isReleased = true;
        }

        public override string ToString()
        {
            return $"RemoteObjectRef. Address: {RemoteObjectInfo.PinnedAddress}, TypeFullName: {_typeInfo.Type}";
        }

        internal ObjectOrRemoteAddress GetItem(ObjectOrRemoteAddress key)
        {
            return CreatingCommunicator.GetItem(this.Token, key);
        }
    }
}