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
        public override EventAttributes Attributes => throw new NotImplementedException();

        public override Type DeclaringType { get; }

        public override string Name { get; }

        public override Type ReflectedType => throw new NotImplementedException();

        public RemoteMethodInfo AddMethod { get; set; }
        public RemoteMethodInfo RemoveMethod { get; set; }
        public override Type EventHandlerType { get; }
        public RemoteEventInfo(RemoteType declaringType, Type eventHandlerType, string name)
        {
            DeclaringType = declaringType;
            this.EventHandlerType = eventHandlerType;
            Name = name;
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