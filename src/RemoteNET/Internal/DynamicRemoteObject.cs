using Microsoft.CSharp.RuntimeBinder;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace RemoteNET.Internal
{
    /// <summary>
    /// A proxy of a remote object.
    /// Usages of this class should be strictly as a `dynamic` variable.
    /// Field/Property reads/writes are redirect to reading/writing to the fields of the remote object
    /// Function calls are redirected to functions calls in the remote process on the remote object
    /// 
    /// </summary>
    [DebuggerDisplay("Dynamic Proxy of {" + nameof(__ro) + "}")]
    class DynamicRemoteObject : DynamicObject
    {
        enum MemberType
        {
            Unknown,
            Field,
            Property,
            Method
        }

        private class MethodOverload
        {
            public List<Type> ArgumentsTypes { get; set; }
            public Func<object[],object> Proxy { get; set; }
        }

        private Dictionary<string, MemberType> _members = new Dictionary<string, MemberType>();

        private Dictionary<string, Action<object>> _fieldsSetters = new Dictionary<string, Action<object>>();
        private Dictionary<string, Func<object>> _fieldsGetters = new Dictionary<string, Func<object>>();
        private Dictionary<string, Action<object>> _propertiesSetters = new Dictionary<string, Action<object>>();
        private Dictionary<string, Func<object>> _propertiesGetters = new Dictionary<string, Func<object>>();
        private Dictionary<string, List<MethodOverload>> _methods = new Dictionary<string, List<MethodOverload>>();

        public RemoteObject __ro;
        private string __typeFullName;
        public DynamicRemoteObject(RemoteObject ro)
        {
            __ro = ro;
            __typeFullName = ro.GetType().FullName;
        }

        // Expansion API
        /// <summary>
        /// Define a new field for the remote object
        /// </summary>
        public void AddField(string propName, Func<object> getter, Action<object> setter)
        {
            if (_members.ContainsKey(propName))
                throw new Exception($"A member with the name \"{propName}\" already exists");

            if (getter == null && setter == null)
                throw new Exception("A property must be set with at least a setter/getter.");

            _members[propName] = MemberType.Field;
            if (getter != null)
                _fieldsGetters[propName] = getter;
            if (setter != null)
                _fieldsSetters[propName] = setter;
        }

        /// <summary>
        /// Define a new property for the remote object
        /// </summary>
        /// <param name="propName">Name of the property</param>
        /// <param name="getter">Getter function. Can be null if getting is not available</param>
        /// <param name="setter">Setter function. Can be null if setting is not available</param>
        public void AddProperty(string propName, Func<object> getter, Action<object> setter)
        {
            if (_members.ContainsKey(propName))
                throw new Exception($"A member with the name \"{propName}\" already exists");

            if (getter == null && setter == null)
                throw new Exception("A property must be set with at least a setter/getter.");

            _members[propName] = MemberType.Property;
            if (getter != null)
                _propertiesGetters[propName] = getter;
            if (setter != null)
                _propertiesSetters[propName] = setter;
        }

        /// <summary>
        /// Defines a new method for the remote object
        /// </summary>
        /// <param name="methodName">Method name</param>
        /// <param name="proxy">Function to invoke when the method is called by the <see cref="DynamicRemoteObject"/></param>
        public void AddMethod(string methodName, List<Type> argTypes, Func<object[],object> proxy)
        {
            // Disallowing other members of this name except other methods
            // overloading is allowed.
            if (_members.TryGetValue(methodName, out MemberType memType) && memType != MemberType.Method)
                throw new Exception($"A member with the name \"{methodName}\" already exists");

            _members[methodName] = MemberType.Method;
            if (!_methods.ContainsKey(methodName))
            {
                _methods[methodName] = new List<MethodOverload>();
            }
            _methods[methodName].Add(new MethodOverload{ArgumentsTypes = argTypes, Proxy = proxy});
        }

        //
        // Dynamic Object API 
        //

        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            if (!_members.TryGetValue(binder.Name, out MemberType memberType))
                throw new Exception($"No such member \"{binder.Name}\"");

            Func<object> getter;

            switch (memberType)
            {
                case MemberType.Field:
                    if (!_fieldsGetters.TryGetValue(binder.Name, out getter))
                    {
                        throw new Exception($"Field \"{binder.Name}\" does not have a getter.");
                    }
                    result = getter();
                    break;
                case MemberType.Property:
                    if (!_propertiesGetters.TryGetValue(binder.Name, out getter))
                    {
                        throw new Exception($"Property \"{binder.Name}\" does not have a getter.");
                    }
                    result = getter();
                    break;
                case MemberType.Method:
                    // Methods should go to "TryInvokeMember"
                    result = null;
                    return false;
                default:
                    throw new Exception($"No such member \"{binder.Name}\"");
            }
            return true;
        }

        public override bool TryInvokeMember(
            InvokeMemberBinder binder,
            object[] args,
            out object result)
        {
            Logger.Debug("[DynamicRemoteObject] TryInvokeMember called ~");
            if (!_members.TryGetValue(binder.Name, out MemberType memberType))
                throw new Exception($"No such member \"{binder.Name}\"");

            switch (memberType)
            {
                case MemberType.Method:
                    List<MethodOverload> overloads = _methods[binder.Name];

                    // Narrow down (hopefuly to one) overload with the same amount of types
                    // TODO: We COULD possibly check the args types (local ones, RemoteObjects, DynamicObjects, ...) if we still have multiple results
                    overloads = overloads.Where(overload => overload.ArgumentsTypes.Count == args.Length).ToList();

                    if (overloads.Count == 1)
                    {
                        // Easy case - a unique function name so we can just return it.
                        result = overloads.Single().Proxy(args);
                    }
                    else
                    {
                        // Multiple overloads. This sucks because we need to... return some "Router" func...
                        throw new NotImplementedException($"Multiple overloads aren't supported at the moment. " +
                                                          $"Method `{binder.Name}` had {overloads.Count} overloads registered.");
                    }
                    break;
                case MemberType.Field:
                case MemberType.Property:
                default:
                    throw new Exception($"No such method \"{binder.Name}\"");
            }
            return true;
        }


        public override bool TrySetMember(SetMemberBinder binder, object value)
        {
            if (!_members.TryGetValue(binder.Name, out MemberType memberType))
                throw new Exception($"No such member \"{binder.Name}\"");

            Action<object> setter;
            switch (memberType)
            {
                case MemberType.Field:
                    if (!_fieldsSetters.TryGetValue(binder.Name, out setter))
                    {
                        throw new Exception($"Field \"{binder.Name}\" does not have a setter.");
                    }
                    setter(value);
                    break;
                case MemberType.Property:
                    if (!_propertiesSetters.TryGetValue(binder.Name, out setter))
                    {
                        throw new Exception($"Property \"{binder.Name}\" does not have a setter.");
                    }
                    setter(value);
                    break;
                case MemberType.Method:
                    throw new Exception("Can't modifying method members.");
                default:
                    throw new Exception($"No such member \"{binder.Name}\".");
            }
            return true;
        }

        static object GetDynamicMember(object obj, string memberName)
        {
            var binder = Binder.GetMember(CSharpBinderFlags.None, memberName, obj.GetType(),
                new[] { CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null) });
            var callsite = CallSite<Func<CallSite, object, object>>.Create(binder);
            return callsite.Target(callsite, obj);
        }

        public override string ToString()
        {
            return _methods[nameof(ToString)].Single().Proxy(new object[0]) as string;
        }

        public override int GetHashCode()
        {
            // ReSharper disable once NonReadonlyMemberInGetHashCode
            return (int)(_methods[nameof(GetHashCode)].Single().Proxy(new object[0]));
        }

        public override bool Equals(object obj)
        {
            throw new NotImplementedException($"Can not call `Equals` on {nameof(DynamicRemoteObject)} instances");
        }
    }
}
