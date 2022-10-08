using Microsoft.CSharp.RuntimeBinder;
using RemoteNET.Internal.ProxiedReflection;
using RemoteNET.Internal.Reflection;
using RemoteNET.Utils;
using ScubaDiver.API.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Binder = Microsoft.CSharp.RuntimeBinder.Binder;

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
    public class DynamicRemoteObject : DynamicObject, IEnumerable
    {
        public class DynamicRemoteMethod : DynamicObject
        {
            string _name;
            ProxiedMethodGroup _methods;
            DynamicRemoteObject _parent;
            Type[] _genericArguments;

            public DynamicRemoteMethod(string name, DynamicRemoteObject parent, ProxiedMethodGroup methods, Type[] genericArguments = null)
            {
                if (genericArguments == null)
                {
                    genericArguments = Array.Empty<Type>();
                }

                _name = name;
                _parent = parent;
                _methods = methods;

                _genericArguments = genericArguments;
            }

            public override bool TryInvoke(InvokeBinder binder, object[] args, out object result)
                => TryInvoke(args, out result);

            public bool TryInvoke(object[] args, out object result)
            {
                List<RemoteMethodInfo> overloads = _methods;

                // Narrow down (hopefuly to one) overload with the same amount of types
                // TODO: We COULD possibly check the args types (local ones, RemoteObjects, DynamicObjects, ...) if we still have multiple results
                overloads = overloads.Where(overload => overload.GetParameters().Length == args.Length).ToList();

                if (overloads.Count == 1)
                {
                    // Easy case - a unique function name so we can just return it.
                    RemoteMethodInfo overload = overloads.Single();
                    if (_genericArguments != null && _genericArguments.Any())
                    {
                        if (!overload.IsGenericMethod)
                        {
                            throw new ArgumentException("A non-generic method was intialized with some generic arguments.");
                        }
                        else if (overload.IsGenericMethod && overload.GetGenericArguments().Length != _genericArguments.Length)
                        {
                            throw new ArgumentException("Wrong number of generic arguments was provided to a generic method");
                        }
                        // OK, invoking with generic arguments
                        result = overloads.Single().MakeGenericMethod(_genericArguments).Invoke(_parent.__ro, args);
                    }
                    else
                    {
                        if (overload.IsGenericMethod)
                        {
                            throw new ArgumentException("A generic method was intialized with no generic arguments.");
                        }
                        // OK, invoking without generic arguments
                        result = overloads.Single().Invoke(_parent.__ro, args);
                    }
                }
                else if (overloads.Count > 1)
                {
                    // Multiple overloads. This sucks because we need to... return some "Router" func...

                    throw new NotImplementedException($"Multiple overloads aren't supported at the moment. " +
                                                      $"Method `{_methods[0]}` had {overloads.Count} overloads registered.");
                }
                else // This case is for "overloads.Count == 0"
                {
                    throw new ArgumentException($"Incorrent number of parameters provided to function.\n" +
                        $"After filtering all overloads with given amount of parameters ({args.Length}) we were left with 0 overloads.");
                }
                return true;
            }

            public override bool Equals(object obj)
            {
                return obj is DynamicRemoteMethod method &&
                       _name == method._name &&
                       EqualityComparer<DynamicRemoteObject>.Default.Equals(_parent, method._parent) &&
                       EqualityComparer<Type[]>.Default.Equals(_genericArguments, method._genericArguments);
            }

            public override int GetHashCode()
            {
                int hashCode = -734779080;
                hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(_name);
                hashCode = hashCode * -1521134295 + EqualityComparer<DynamicRemoteObject>.Default.GetHashCode(_parent);
                hashCode = hashCode * -1521134295 + EqualityComparer<Type[]>.Default.GetHashCode(_genericArguments);
                return hashCode;
            }

            // Functions to turn our base method into a "generic" one - with specific arguments
            // instead of generic placeholders.
            // I wish I could've overriden the 'MyFunc<T>' notation but I don't think that syntax is
            // modifiable in C#.
            // Instead we go to the second best solution which is use indexers:
            // MyFunc[typeof(T)]
            // - or -
            // Type t = typeof(T);
            // MyFunc[t]
            //
            // Since some methods support multiple generic arguments I also overrode some multi-dimensional indexers below.
            // This allows that to compile:
            // Type t,p,q = ...;
            // MyOtherFunc[t,p,q]

            public DynamicRemoteMethod this[Type t] =>
                    new DynamicRemoteMethod(_name, _parent, _methods, _genericArguments.Concat(new Type[] { t }).ToArray());

            public DynamicRemoteMethod this[Type t1, Type t2] => this[t1][t2];
            public DynamicRemoteMethod this[Type t1, Type t2, Type t3] => this[t1, t2][t3];
            public DynamicRemoteMethod this[Type t1, Type t2, Type t3, Type t4] => this[t1, t2, t3][t4];
            public DynamicRemoteMethod this[Type t1, Type t2, Type t3, Type t4, Type t5] => this[t1, t2, t3, t4][t5];
        }

        public RemoteApp __ra;
        public RemoteObject __ro;
        public RemoteType __type;


        private IEnumerable<MemberInfo> __ongoingMembersDumper = null;
        private IEnumerator<MemberInfo> __ongoingMembersDumperEnumerator = null;
        private List<MemberInfo> __membersInner = null;
        public IEnumerable<MemberInfo> __members => MindFuck();

        public DynamicRemoteObject(RemoteApp ra, RemoteObject ro)
        {
            __ra = ra;
            __ro = ro;
            __type = ro.GetType() as RemoteType;
            if (__type == null && ro.GetType() != null)
            {
                throw new ArgumentException("Can only create DynamicRemoteObjects of RemoteObjects with Remote Types. (As returned from GetType())");
            }
        }

        /// <summary>
        /// Gets the type of the proxied remote object, in the remote app. (This does not reutrn `typeof(DynamicRemoteMethod)`)
        /// </summary>
        public new Type GetType() => __type;

        private IEnumerable<MemberInfo> GetAllMembersRecursive()
        {
            Type lastType = __type;
            Type nextType = __type;

            // We use this dictionary to make sure overides from subclasses don't get exported twice (for the parent as well)
            Dictionary<string, List<MethodBase>> _processedOverloads = new Dictionary<string, List<MethodBase>>();
            do
            {
                var members = nextType.GetMembers((BindingFlags)0xffff);
                foreach (MemberInfo member in members)
                {
                    if (member is MethodBase newMethods)
                    {
                        if (!_processedOverloads.ContainsKey(member.Name))
                            _processedOverloads[member.Name] = new List<MethodBase>();
                        List<MethodBase> oldMethods = _processedOverloads[member.Name];

                        bool overriden = oldMethods.Any(oldMethod => oldMethod.SignatureEquals(newMethods));
                        if (overriden)
                            continue;

                        _processedOverloads[member.Name].Add(newMethods);

                    }
                    yield return member;
                }
                lastType = nextType;
                nextType = nextType.BaseType;
            }
            while (nextType != null && lastType != typeof(object));
        }

        private IEnumerable<MemberInfo> MindFuck()
        {
            if (__membersInner != null && __ongoingMembersDumper == null)
            {
                return __membersInner;
            }
            // Defining a new method so we can use "yield return" (Outter function already returned a "real" IEnumerable in the above case
            // so using "yield return" as well is forbidden.
            IEnumerable<MemberInfo> Aggregator()
            {
                __membersInner ??= new List<MemberInfo>();
                __ongoingMembersDumper ??= GetAllMembersRecursive();
                __ongoingMembersDumperEnumerator ??= __ongoingMembersDumper.GetEnumerator();
                foreach (var member in __membersInner)
                {
                    yield return member;
                }
                while(__ongoingMembersDumperEnumerator.MoveNext())
                {
                    var member = __ongoingMembersDumperEnumerator.Current;
                    __membersInner.Add(member);
                    yield return member;
                }
                __ongoingMembersDumper = null;
                __ongoingMembersDumperEnumerator = null;

            };
            return Aggregator();
        }

        public T InvokeMethod<T>(string name, params object[] args)
        {
            var matchingMethods = from member in __members
                                  where member.Name == name
                                  where ((MethodInfo)member).GetParameters().Length == args.Length
                                  select member;
            return (T)(matchingMethods.Single() as MethodInfo).Invoke(__ro, args);
        }

        #region Dynamic Object API 
        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            Type lastType = __type;
            Type nextType = __type;
            do
            {
                bool found = TryGetMember(nextType, binder.Name, out result);
                if (found)
                    return true;
                lastType = nextType;
                nextType = nextType.BaseType;
            }
            while (nextType != null || lastType == typeof(object));

            result = null;
            return false;
        }

        private bool TryGetMember(Type t, string name, out object result)
        {
            result = null;
            MemberInfo[] members = t.GetMembers((BindingFlags)0xffff);
            List<MemberInfo> matches = members.Where(member => member.Name == name).ToList();

            if (!matches.Any())
            {
                result = null;
                return false;
            }

            // At least 1 member with that name
            MemberInfo firstMember = matches[0];
            MemberTypes type = firstMember.MemberType;
            bool singleMatch = matches.Count == 1;

            switch (type)
            {
                case MemberTypes.Field:
                    if (!singleMatch)
                    {
                        throw new ArgumentException($"Multiple members were found for the name `{name}` and at least one of them was a field");
                    }
                    try
                    {
                        result = ((FieldInfo)firstMember).GetValue(__ro);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Field \"{name}\"'s getter threw an exception which sucks. Ex: " + ex);
                        throw;
                    }
                    break;
                case MemberTypes.Property:
                    if (!singleMatch)
                    {
                        throw new ArgumentException($"Multiple members were found for the name `{name}` and at least one of them was a property");
                    }
                    try
                    {
                        result = ((PropertyInfo)firstMember).GetValue(__ro);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Property \"{name}\"'s getter threw an exception which sucks. Ex: " + ex);
                        throw;
                    }
                    break;
                case MemberTypes.Method:
                    // The cases that get here are when the user is trying to:
                    // 1. Save a method in a variable:
                    //      var methodGroup = dro.Insert;
                    // 2. The user is trying to use the "RemoteNET way" of specifing generic:
                    //      Type t = typeof(SomeType);
                    //      dro.Insert[t]();
                    result = GetMethodProxy(name);
                    break;
                case MemberTypes.Event:
                    // TODO: 
                    throw new NotImplementedException("Disabled since moving to RemoteType based impl");
                default:
                    throw new Exception($"No such member \"{name}\"");
            }
            return true;
        }


        private DynamicRemoteMethod GetMethodProxy(string name)
        {
            var methods = __members.Where(member => member.Name == name).ToArray();
            if (methods.Length == 0)
            {
                throw new Exception($"Method \"{name}\" wasn't found in the members of type {__type.Name}.");
            }

            if (methods.Any(member => member.MemberType != MemberTypes.Method))
            {
                throw new Exception($"A member called \"{name}\" exists in the type and it isn't a method (It's a {methods.First(m => m.MemberType != MemberTypes.Method).MemberType})");
            }
            if (methods.Any(member => !(member is RemoteMethodInfo)))
            {
                throw new Exception($"A method overload for \"{name}\" wasn't a MethodInfo");
            }

            ProxiedMethodGroup methodGroup = new ProxiedMethodGroup();
            methodGroup.AddRange(methods.Cast<RemoteMethodInfo>());
            try
            {
                return new DynamicRemoteMethod(name, this, methodGroup);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Constructing {nameof(DynamicRemoteMethod)} of \"{name}\" threw an exception. Ex: " + ex);
                throw;
            }
        }

        public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
        {
            // If "TryInvokeMember" was called first (instead of "TryGetMember")
            // it means that the user specified generic args (if any are even requied) within '<' and '>' signs
            // or there aren't any generic args. We can just do the call here instead of letting the dynamic
            // runtime resort to calling 'TryGetMember'

            DynamicRemoteMethod drm = GetMethodProxy(binder.Name);
            Type binderType = binder.GetType();
            System.Reflection.PropertyInfo TypeArgumentsPropInfo = binderType.GetProperty("TypeArguments");
            if (TypeArgumentsPropInfo != null)
            {
                // We got ourself a binder which implemented .NET's internal "ICSharpInvokeOrInvokeMemberBinder" Interface
                // https://github.com/microsoft/referencesource/blob/master/Microsoft.CSharp/Microsoft/CSharp/ICSharpInvokeOrInvokeMemberBinder.cs
                // We can now see if the invoked for the function specified generic types
                // In that case, we can hijack and do the call here
                // Otherwise - Just let TryGetMembre return a proxy
                if (TypeArgumentsPropInfo.GetValue(binder) is IList<Type> genArgs)
                {
                    foreach (Type t in genArgs)
                    {
                        // Aggregate the generic types into the dynamic remote method
                        // Example:
                        //  * Invoke method is Insert<,>
                        //  * Given types are ['T', 'S']
                        //  * First loop iteration: Inert<,> --> Insert<T,>
                        //  * Second loop iteration: Inert<T,> --> Insert<T,S>
                        drm = drm[t];
                    }
                }
            }
            return drm.TryInvoke(args, out result);
        }

        public bool HasMember(string name) => __members.Any(member => member.Name == name);
        public override bool TrySetMember(SetMemberBinder binder, object value)
        {
            List<MemberInfo> matches = __members.Where(member => member.Name == binder.Name).ToList();

            if (!matches.Any())
            {
                return false;
            }

            // At least 1 member with that name
            MemberInfo firstMember = matches[0];
            MemberTypes type = firstMember.MemberType;
            bool singleMatch = matches.Count == 1;

            // In case we are resolving a field or property
            switch (type)
            {
                case MemberTypes.Field:
                    if (!singleMatch)
                    {
                        throw new ArgumentException($"Multiple members were found for the name `{binder.Name}` and at least one of them was a field");
                    }
                    try
                    {
                        ((FieldInfo)firstMember).SetValue(__ro, value);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Field \"{binder.Name}\"'s getter threw an exception which sucks. Ex: " + ex);
                        throw;
                    }
                    break;
                case MemberTypes.Property:
                    if (!singleMatch)
                    {
                        throw new ArgumentException($"Multiple members were found for the name `{binder.Name}` and at least one of them was a property");
                    }
                    try
                    {
                        ((PropertyInfo)firstMember).SetValue(__ro, value);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Property \"{binder.Name}\"'s getter threw an exception which sucks. Ex: " + ex);
                        throw;
                    }
                    break;
                case MemberTypes.Method:
                    throw new Exception("Can't modifying method members.");
                case MemberTypes.Event:
                    // TODO:
                    throw new NotImplementedException("Not implemented since moving to RemoteType based impl");

                //
                // =========================        OLD IMPL:     ======================================
                //
                //ProxiedEventInfo eventProxy = _events[binder.Name];
                //if (eventProxy == value)
                //{
                //    // This "setting" of the event happens after regsistering an event handler because of how the "+=" operator works.
                //    // Since the "+" operator of DynamicEventProxy returns the same instance we can spot THIS EXACT scenario and allow it without raising errors.
                //    return true;
                //}
                //else
                //{
                //    // This is an INVALID setting of the event "field". For example:
                //    // dynObject.SomeEvent = "123";
                //    //  - or even -
                //    // dynObject.SomeEvent = new EventHandler(new Action<object,EventArgs>((a,b)=>{}));

                //    // We are telling the user it's not not allowed just like normal .NET does not allow setting values to events.
                //    throw new Exception($"The event {binder.Name} can only appear on the left hand side of += or -=.");
                //}
                default:
                    throw new Exception($"No such member \"{binder.Name}\".");
            }
            return true;
        }

        /// <summary>
        /// Helper function to access the member <paramref name="memberName"/> of the object <paramref name="obj"/>.
        /// This is equivilent to explicitly compiling the expression '<paramref name="obj"/>.<paramref name="memberName"/>'.
        /// </summary>
        public static bool TryGetDynamicMember(object obj, string memberName, out object output)
        {
            var binder = Binder.GetMember(CSharpBinderFlags.None, memberName, obj.GetType(),
                new[] { CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null) });
            var callsite = CallSite<Func<CallSite, object, object>>.Create(binder);
            if (obj is DynamicRemoteObject dro)
            {
                if (dro.HasMember(memberName))
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
#endregion

        #region ToString / GetHashCode / Equals

        public override string ToString() => InvokeMethod<string>(nameof(ToString));

        public override int GetHashCode() => InvokeMethod<int>(nameof(GetHashCode));

        public override bool Equals(object obj)
        {
            throw new NotImplementedException($"Can not call `Equals` on {nameof(DynamicRemoteObject)} instances");
        }
        #endregion

        /// <summary>
        /// Array access. Key can be any primitive / RemoteObject / DynamicRemoteObject
        /// </summary>
        public dynamic this[object key]
        {
            get
            {

                ScubaDiver.API.ObjectOrRemoteAddress ooraKey = RemoteFunctionsInvokeHelper.CreateRemoteParameter(key);
                ScubaDiver.API.ObjectOrRemoteAddress item = __ro.GetItem(ooraKey);
                if (item.IsNull)
                {
                    return null;
                }
                else if (item.IsRemoteAddress)
                {
                    return this.__ra.GetRemoteObject(item.RemoteAddress).Dynamify();
                }
                else
                {
                    return PrimitivesEncoder.Decode(item.EncodedObject, item.Type);
                }
            }
            set => throw new NotImplementedException();
        }

        #region Array Casting
        private static T[] __cast_to_array<T>(DynamicRemoteObject dro)
        {
            dynamic dyn = dro;
            int length = dyn.Length;
            T[] array = new T[length];
            for (int i = 0; i < length; i++)
                array[i] = dyn[i];
            return array;
        }

        public static implicit operator bool[](DynamicRemoteObject dro) => __cast_to_array<bool>(dro);
        public static implicit operator byte[](DynamicRemoteObject dro) => __cast_to_array<byte>(dro);
        public static implicit operator char[](DynamicRemoteObject dro) => __cast_to_array<char>(dro);
        public static implicit operator short[](DynamicRemoteObject dro) => __cast_to_array<short>(dro);
        public static implicit operator ushort[](DynamicRemoteObject dro) => __cast_to_array<ushort>(dro);
        public static implicit operator int[](DynamicRemoteObject dro) => __cast_to_array<int>(dro);
        public static implicit operator uint[](DynamicRemoteObject dro) => __cast_to_array<uint>(dro);
        public static implicit operator long[](DynamicRemoteObject dro) => __cast_to_array<long>(dro);
        public static implicit operator ulong[](DynamicRemoteObject dro) => __cast_to_array<ulong>(dro);
        public static implicit operator string[](DynamicRemoteObject dro) => __cast_to_array<string>(dro);
        #endregion

        public IEnumerator GetEnumerator()
        {
            if (!__members.Any(member => member.Name == nameof(GetEnumerator)))
                throw new Exception($"No method called {nameof(GetEnumerator)} found. The remote object probably doesn't implement IEnumerable");

            dynamic enumeratorDro = InvokeMethod<object>(nameof(GetEnumerator));
            return new DynamicRemoteEnumerator(enumeratorDro);
        }
    }
}
