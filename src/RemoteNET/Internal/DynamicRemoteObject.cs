using Microsoft.CSharp.RuntimeBinder;
using RemoteNET.Internal.ProxiedReflection;
using ScubaDiver.API.Utils;
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
    public class DynamicRemoteObject : DynamicObject
    {

        private Dictionary<string, ProxiedMemberType> _members = new Dictionary<string, ProxiedMemberType>();
        private Dictionary<string, ProxiedValueMemberInfo> _fields = new Dictionary<string, ProxiedValueMemberInfo>();
        private Dictionary<string, ProxiedValueMemberInfo> _properties = new Dictionary<string, ProxiedValueMemberInfo>();
        private Dictionary<string, ProxiedMethodGroup> _methods = new Dictionary<string, ProxiedMethodGroup>();
        private Dictionary<string, ProxiedEventInfo> _events = new Dictionary<string, ProxiedEventInfo>();

        public RemoteApp __ra;
        public RemoteObject __ro;
        private string __typeFullName;

 
        public DynamicRemoteObject(RemoteApp ra, RemoteObject ro)
        {
            __ra = ra;
            __ro = ro;
            __typeFullName = ro.GetType().FullName;
        }


        /// <summary>
        /// Gets the type of the proxied remote object, in the remote app. (This does not reutrn `typeof(RemoteObject)`)
        /// </summary>
        public new Type GetType()
        {
            return __ro.GetType();
        }

        // Expansion API
        /// <summary>
        /// Define a new field for the remote object
        /// </summary>
        public void AddField(string field, string fullTypeName, Func<object> getter, Action<object> setter)
        {
            if (_members.ContainsKey(field))
                throw new Exception($"A member with the name \"{field}\" already exists");

            if (getter == null && setter == null)
                throw new Exception("A property must be set with at least a setter/getter.");

            _members[field] = ProxiedMemberType.Field;
            ProxiedValueMemberInfo proxyInfo = new ProxiedValueMemberInfo(ProxiedMemberType.Field)
            {
                FullTypeName = fullTypeName
            };
            if (getter != null)
                proxyInfo.Getter = getter;
            if (setter != null)
                proxyInfo.Setter = setter;  

            _fields.Add(field, proxyInfo);
        }

        /// <summary>
        /// Define a new property for the remote object
        /// </summary>
        /// <param name="propName">Name of the property</param>
        /// <param name="getter">Getter function. Can be null if getting is not available</param>
        /// <param name="setter">Setter function. Can be null if setting is not available</param>
        public void AddProperty(string propName, string fullTypeName, Func<object> getter, Action<object> setter)
        {
            if (_members.ContainsKey(propName))
                throw new Exception($"A member with the name \"{propName}\" already exists");

            if (getter == null && setter == null)
                throw new Exception("A property must be set with at least a setter/getter.");

            _members[propName] = ProxiedMemberType.Property;
            ProxiedValueMemberInfo proxyInfo = new ProxiedValueMemberInfo(ProxiedMemberType.Property)
            {
                FullTypeName = fullTypeName
            };
            if (getter != null)
                proxyInfo.Getter = getter;
            if (setter != null)
                proxyInfo.Setter = setter;

            _properties.Add(propName, proxyInfo);
        }

        /// <summary>
        /// Defines a new method for the remote object
        /// </summary>
        /// <param name="methodName">Method name</param>
        /// <param name="proxy">Function to invoke when the method is called by the <see cref="DynamicRemoteObject"/></param>
        public void AddMethod(string methodName, List<Tuple<Type,string>> parameters, Type retType, Func<object[], object> proxy)
        {
            // Disallowing other members of this name except other methods
            // overloading is allowed.
            if (_members.TryGetValue(methodName, out ProxiedMemberType memType) && memType != ProxiedMemberType.Method)
                throw new Exception($"A member with the name \"{methodName}\" already exists");

            _members[methodName] = ProxiedMemberType.Method;
            if (!_methods.ContainsKey(methodName))
            {
                _methods[methodName] = new ProxiedMethodGroup();
            }
            _methods[methodName].Add(new ProxiedMethodOverload { ReturnType = retType, Parameters = parameters, Proxy = proxy });
        }

        public void AddEvent(string eventName, List<Type> argTypes)
        {
            // TODO: Make sure it's not defined twice
            _members[eventName] = ProxiedMemberType.Event;
            _events[eventName] = new ProxiedEventInfo(__ro, eventName, argTypes);
        }

        public IReadOnlyDictionary<string, IProxiedMember> GetDynamicallyAddedMembers()
        {
            Dictionary<string, IProxiedMember> output = new Dictionary<string, IProxiedMember>();
            foreach(KeyValuePair<string, ProxiedMemberType> memberAndType in _members)
            {
                string memberName = memberAndType.Key;
                switch (memberAndType.Value)
                {
                    case ProxiedMemberType.Field:
                        output[memberName] = _fields[memberName];
                        break;
                    case ProxiedMemberType.Property:
                        output[memberName] = _properties[memberName];
                        break;
                    case ProxiedMemberType.Method:
                        output[memberName] = _methods[memberName];
                        break;
                    case ProxiedMemberType.Event:
                        output[memberName] = _events[memberName];
                        break;
                }
            }
            return output;
        }

        //
        // Dynamic Object API 
        //


        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            if (!_members.TryGetValue(binder.Name, out ProxiedMemberType memberType))
            {
                result = null;
                return false;
            }

            // In case we are resolving a field or property
            ProxiedValueMemberInfo proxiedInfo;

            switch (memberType)
            {
                case ProxiedMemberType.Field:
                    if (!_fields.TryGetValue(binder.Name, out proxiedInfo))
                    {
                        throw new Exception($"Field \"{binder.Name}\" does not have a getter.");
                    }
                    try
                    {
                        result = proxiedInfo.Getter();
                    }
                    catch(Exception ex)
                    {
                        Console.WriteLine($"Field \"{binder.Name}\"'s getter threw an exception which sucks. Ex: "+ex);
                        throw;
                    }
                    break;
                case ProxiedMemberType.Property:
                    if (!_properties.TryGetValue(binder.Name, out proxiedInfo))
                    {
                        throw new Exception($"Property \"{binder.Name}\" does not have a getter.");
                    }
                    try
                    {
                        result = proxiedInfo.Getter();
                    }
                    catch(Exception ex)
                    {
                        Console.WriteLine($"Property \"{binder.Name}\"'s getter threw an exception which sucks. Ex: "+ex);
                        throw;
                    }
                    break;
                case ProxiedMemberType.Method:
                    // Methods should go to "TryInvokeMember"
                    result = null;
                    return false;
                case ProxiedMemberType.Event:
                    result = _events[binder.Name];
                    break;
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
            string name = binder.Name;
            return InvokeMethodMember(name, args, out result);
        }

        private bool InvokeMethodMember(string name, object[] args, out object result)
        {
            Logger.Debug("[DynamicRemoteObject] TryInvokeMember called ~");
            if (!_members.TryGetValue(name, out ProxiedMemberType memberType))
                throw new Exception($"No such member \"{name}\"");

            switch (memberType)
            {
                case ProxiedMemberType.Method:
                    List<ProxiedMethodOverload> overloads = _methods[name];

                    // Narrow down (hopefuly to one) overload with the same amount of types
                    // TODO: We COULD possibly check the args types (local ones, RemoteObjects, DynamicObjects, ...) if we still have multiple results
                    overloads = overloads.Where(overload => overload.Parameters.Count == args.Length).ToList();

                    if (overloads.Count == 1)
                    {
                        // Easy case - a unique function name so we can just return it.
                        result = overloads.Single().Proxy(args);
                    }
                    else
                    {
                        // Multiple overloads. This sucks because we need to... return some "Router" func...
                        throw new NotImplementedException($"Multiple overloads aren't supported at the moment. " +
                                                          $"Method `{name}` had {overloads.Count} overloads registered.");
                    }
                    break;
                case ProxiedMemberType.Field:
                case ProxiedMemberType.Property:
                default:
                    throw new Exception($"No such method \"{name}\"");
            }
            return true;
        }

        public bool HasMember(string name) => _members.ContainsKey(name);
        public override bool TrySetMember(SetMemberBinder binder, object value)
        {
            if (!_members.TryGetValue(binder.Name, out ProxiedMemberType memberType))
                throw new Exception($"No such member \"{binder.Name}\"");

            // In case we are resolving a field or property
            ProxiedValueMemberInfo proxiedInfo;

            switch (memberType)
            {
                case ProxiedMemberType.Field:
                    if (!_fields.TryGetValue(binder.Name, out proxiedInfo))
                    {
                        throw new Exception($"Field \"{binder.Name}\" does not have a setter.");
                    }
                    try
                    {
                        proxiedInfo.Setter(value);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Field \"{binder.Name}\"'s setter threw an exception which sucks. Ex: " + ex);
                        throw;
                    }
                    break;
                case ProxiedMemberType.Property:
                    if (!_properties.TryGetValue(binder.Name, out proxiedInfo))
                    {
                        throw new Exception($"Property \"{binder.Name}\" does not have a setter.");
                    }
                    try
                    {
                        proxiedInfo.Setter(value);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Property \"{binder.Name}\"'s setter threw an exception which sucks. Ex: " + ex);
                        throw;
                    }
                    break;
                case ProxiedMemberType.Method:
                    throw new Exception("Can't modifying method members.");
                case ProxiedMemberType.Event:
                    ProxiedEventInfo eventProxy = _events[binder.Name];
                    if (eventProxy == value)
                    {
                        // This "setting" of the event happens after regsistering an event handler because of how the "+=" operator works.
                        // Since the "+" operator of DynamicEventProxy returns the same instance we can spot THIS EXACT scenario and allow it without raising errors.
                        return true;
                    }
                    else
                    {
                        // This is an INVALID setting of the event "field". For example:
                        // dynObject.SomeEvent = "123";
                        //  - or even -
                        // dynObject.SomeEvent = new EventHandler(new Action<object,EventArgs>((a,b)=>{}));

                        // We are telling the user it's not not allowed just like normal .NET does not allow setting values to events.
                        throw new Exception($"The event {binder.Name} can only appear on the left hand side of += or -=.");
                    }
                default:
                    throw new Exception($"No such member \"{binder.Name}\".");
            }
            return true;
        }

        /// <summary>
        /// Helper function to access the member 'memberName' of the object 'obj.
        /// This is equivilent to explicitly compiling the expression 'obj.memberName'.
        /// </summary>
        public static bool TryGetDynamicMember(object obj, string memberName, out object output)
        {
            var binder = Binder.GetMember(CSharpBinderFlags.None, memberName, obj.GetType(),
                new[] { CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null) });
            var callsite = CallSite<Func<CallSite, object, object>>.Create(binder);
            if (obj is DynamicRemoteObject dro)
            {
                if (dro._members.ContainsKey(memberName))
                {
                    if (dro.TryGetMember(binder as GetMemberBinder, out output))
                    {
                        return true;
                    }
                }
            }

            // Fallback? Does it always just result in TryGetMember?
            try
            {
                output = callsite.Target(callsite, obj);
                return true;
            }
            catch
            {
                output = null;
                return false;
            }
        }

        public override string ToString()
        {
            return _methods[nameof(ToString)].Single(mi=>mi.Parameters.Count == 0).Proxy(new object[0]) as string;
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

        // TODO: key should be dynamic and even encoded as ObjectOrRemoteAddress later in the calls chain if required.
        public dynamic this[int key]
        {
            get
            {
                ScubaDiver.API.ObjectOrRemoteAddress oora = __ro.GetItem(key);
                if (oora.IsRemoteAddress)
                {
                    return this.__ra.GetRemoteObject(oora.RemoteAddress).Dynamify();
                }
                else
                {
                    return PrimitivesEncoder.Decode(oora.EncodedObject, oora.Type);
                }
            }
            set => throw new NotImplementedException();
        }
    }
}
