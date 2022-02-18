using RemoteNET.Internal.ProxiedReflection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RemoteNET.Internal
{
    public class ProxiedEventInfo : IProxiedMember
    {
        public ProxiedMemberType Type => ProxiedMemberType.Event;

        private readonly RemoteObject _ro;
        private string Name { get; set; }
        private List<Type> ArgumentsTypes { get; set; }


        public ProxiedEventInfo(RemoteObject ro, string name, List<Type> args)
        {
            this._ro = ro;
            this.Name = name;
            this.ArgumentsTypes = args;
        }

        public static ProxiedEventInfo operator +(ProxiedEventInfo c1, Delegate x)
        {
            System.Reflection.ParameterInfo[] parameters = x.Method.GetParameters();

            if (parameters.Length != c1.ArgumentsTypes.Count)
            {
                throw new Exception($"The '{c1.Name}' event expects {c1.ArgumentsTypes.Count} parameters, " +
                    $"the callback that was being registered have {parameters.Length}");
            }

            if (parameters.Any(p => p.GetType().IsAssignableFrom(typeof(DynamicRemoteObject))))
            {
                throw new Exception("A Remote event's local callback must have only 'dynamic' parameters");
            }

            c1._ro.EventSubscribe(c1.Name, x);

            return c1;
        }

        public static ProxiedEventInfo operator -(ProxiedEventInfo c1, Delegate x)
        {
            c1._ro.EventUnsubscribe(c1.Name, x);
            return c1;
        }
    }
}
