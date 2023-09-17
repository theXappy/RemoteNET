using System;
using System.Linq;
using System.Reflection;
using RemoteNET.Internal;
using RemoteNET.Internal.Reflection;
using RemoteNET.Internal.Reflection.DotNet;
using ScubaDiver.API;
using ScubaDiver.API.Interactions;
using ScubaDiver.API.Utils;

namespace RemoteNET.RttiReflection
{
    /// <summary>
    /// In this context: "function" = Methdods + Constructors.
    /// </summary>
    internal static class UnmanagedRemoteFunctionsInvokeHelper
    {
        public static ObjectOrRemoteAddress CreateRemoteParameter(object parameter)
        {
            if (parameter == null)
            {
                return ObjectOrRemoteAddress.Null;
            }
            else if (parameter.GetType().IsPrimitiveEtc() || parameter.GetType().IsPrimitiveEtcArray())
            {
                return ObjectOrRemoteAddress.FromObj(parameter);
            }
            else if (parameter is RemoteObject remoteArg)
            {
                return ObjectOrRemoteAddress.FromToken(remoteArg.RemoteToken, remoteArg.GetRemoteType().FullName);
            }
            else if (parameter is DynamicRemoteObject dro)
            {
                RemoteObject originRemoteObject = dro.__ro;
                return ObjectOrRemoteAddress.FromToken(originRemoteObject.RemoteToken, originRemoteObject.GetRemoteType().FullName);
            }
            else if (parameter is Type t)
            {
                return ObjectOrRemoteAddress.FromType(t);
            }
            else
            {
                throw new Exception(
                    $"{nameof(RemoteRttiMethodInfo)}.{nameof(Invoke)} only works with primitive (int, " +
                    $"double, string,...) or remote (in {nameof(RemoteObject)}) parameters. " +
                    $"One of the parameter was of unsupported type {parameter.GetType()}");
            }
        }

        public static object Invoke(UnmanagedRemoteApp app, Type declaringType, string funcName, object obj, object[] parameters)
        {
            // invokeAttr, binder and culture currently ignored
            // TODO: Actually validate parameters and expected parameters.

            object[] paramsNoEnums = parameters.ToArray();
            ObjectOrRemoteAddress[] remoteParams = paramsNoEnums.Select(ManagedRemoteFunctionsInvokeHelper.CreateRemoteParameter).ToArray();

            bool hasResults;
            ObjectOrRemoteAddress oora;
            if (obj == null)
            {
                if (app == null)
                {
                    throw new InvalidOperationException($"Trying to invoke a static call (null target object) " +
                                                        $"on a {nameof(RemoteMethodInfo)} but it's associated " +
                                                        $"Declaring Type ({declaringType}) does not have a ManagedRemoteApp associated. " +
                                                        $"The type was either mis-constructed or it's not a {nameof(RemoteType)} object");
                }

                InvocationResults invokeRes = app.Communicator.InvokeStaticMethod(declaringType.FullName, funcName, remoteParams);
                if (invokeRes.VoidReturnType)
                {
                    hasResults = false;
                    oora = null;
                }
                else
                {
                    hasResults = true;
                    oora = invokeRes.ReturnedObjectOrAddress;
                }
            }
            else
            {
                switch (obj)
                {
                    // obj is NOT null. Make sure it's a RemoteObject.
                    case UnmanagedRemoteObject ro:
                        (hasResults, oora) = ro.InvokeMethod(funcName, remoteParams);
                        break;
                    case IntPtr ptr:
                        {
                            ulong remoteAddr = (ulong)ptr.ToInt64();
                            InvocationResults invokeRes = app.Communicator.InvokeMethod(remoteAddr,
                                declaringType.FullName,
                                funcName,
                                Array.Empty<string>(),
                                remoteParams);

                            (hasResults, oora) = (false, null);
                            if (!invokeRes.VoidReturnType)
                            {
                                (hasResults, oora) = (true, invokeRes.ReturnedObjectOrAddress);
                            }
                            break;
                        }
                    default:
                        throw new NotImplementedException(
                            $"{nameof(RemoteMethodInfo)}.{nameof(Invoke)} only supports {nameof(UnmanagedRemoteObject)} targets at the moment.");
                }
            }

            if (!hasResults)
                return null;

            // Non-void function.
            if (oora.IsNull)
                return null;
            if (!oora.IsRemoteAddress)
            {
                return PrimitivesEncoder.Decode(oora);
            }
            else
            {
                RemoteObject ro = app.GetRemoteObject(oora);
                return ro.Dynamify();
            }
        }

    }
}