using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using ScubaDiver.API;

namespace RemoteNET.Internal.Reflection
{
    public class RemoteEventInfo : EventInfo
    {
        private Lazy<Type> _eventHandlerType;

        public override EventAttributes Attributes => throw new NotImplementedException();

        public override Type DeclaringType { get; }

        public override string Name { get; }

        public override Type ReflectedType => throw new NotImplementedException();

        public RemoteMethodInfo AddMethod { get; set; }
        public RemoteMethodInfo RemoveMethod { get; set; }
        public override Type EventHandlerType => _eventHandlerType.Value;
        public RemoteEventInfo(RemoteType declaringType, Lazy<Type> eventHandlerType, string name)
        {
            DeclaringType = declaringType;
            _eventHandlerType = eventHandlerType;
            Name = name;
        }

        public RemoteEventInfo(RemoteType declaringType, Type eventHandlerType, string name) :
            this(declaringType, new Lazy<Type>(() => eventHandlerType), name)
        {
        }

        public RemoteEventInfo(RemoteType declaringType, EventInfo ei) : this(declaringType, new Lazy<Type>(() => ei.EventHandlerType), ei.Name)
        {
        }

        public override MethodInfo GetAddMethod(bool nonPublic) => AddMethod;
        public override MethodInfo GetRemoveMethod(bool nonPublic) => RemoveMethod;

        public override object[] GetCustomAttributes(bool inherit)
        {
            throw new NotImplementedException();
        }

        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            throw new NotImplementedException();
        }

        public override MethodInfo GetRaiseMethod(bool nonPublic)
        {
            throw new NotImplementedException();
        }


        public override bool IsDefined(Type attributeType, bool inherit)
        {
            throw new NotImplementedException();
        }
    }
}