﻿using System;
using System.Linq;
using ScubaDiver.API;
using ScubaDiver.API.Extensions;
using ScubaDiver.API.Interactions;
using ScubaDiver.API.Utils;

namespace RemoteNET.Internal.Reflection
{
    /// <summary>
    /// In this context: "function" = Methdods + Constructors.
    /// </summary>
    internal static class RemoteFunctionsInvokeHelper
    {
        public static ObjectOrRemoteAddress CreateRemoteParameter(object parameter)
        {
            if(parameter == null)
            {
                return ObjectOrRemoteAddress.Null;
            }
            else if (parameter.GetType().IsPrimitiveEtc() || parameter.GetType().IsPrimitiveEtcArray())
            {
                return ObjectOrRemoteAddress.FromObj(parameter);
            }
            else if (parameter is RemoteObject remoteArg)
            {
                return ObjectOrRemoteAddress.FromToken(remoteArg.RemoteToken, remoteArg.GetType().FullName);
            }
            else if (parameter is DynamicRemoteObject dro)
            {
                RemoteObject originRemoteObject = dro.__ro;
                return ObjectOrRemoteAddress.FromToken(originRemoteObject.RemoteToken, originRemoteObject.GetType().FullName);
            }
            else if (parameter is Type t)
            {
                return ObjectOrRemoteAddress.FromType(t);
            }
            else
            {
                throw new Exception(
                    $"{nameof(RemoteMethodInfo)}.{nameof(Invoke)} only works with primitive (int, " +
                    $"double, string,...) or remote (in {nameof(RemoteObject)}) parameters. " +
                    $"One of the parameter was of unsupported type {parameter.GetType()}");
            }
        }

        public static object Invoke(RemoteApp app, Type declaringType, string funcName, object obj, Type[] genericArgs, object[] parameters)
            => Invoke(app, declaringType, funcName, obj, genericArgs.Select(arg => arg.FullName).ToArray(), parameters);

        public static object Invoke(RemoteApp app, Type declaringType, string funcName, object obj, string[] genericArgsFullNames, object[] parameters)
        {
            // invokeAttr, binder and culture currently ignored
            // TODO: Actually validate parameters and expected parameters.

            object[] paramsNoEnums = parameters.ToArray();
            for (int i = 0; i < paramsNoEnums.Length; i++)
            {
                var val = paramsNoEnums[i];
                if (val != null && val.GetType().IsEnum)
                {
                    var enumClass = app.GetRemoteEnum(val.GetType().FullName);
                    // TODO: This will break on the first enum value which represents 2 or more flags
                    object enumVal = enumClass.GetValue(val.ToString());
                    // NOTE: Object stays in place in the remote app as long as we have it's reference
                    // in the paramsNoEnums array (so untill end of this method)
                    paramsNoEnums[i] = enumVal;
                }
            }

            ObjectOrRemoteAddress[] remoteParams = paramsNoEnums.Select(RemoteFunctionsInvokeHelper.CreateRemoteParameter).ToArray();

            bool hasResults;
            ObjectOrRemoteAddress oora;
            if (obj == null)
            {
                if (app == null)
                {
                    throw new InvalidOperationException($"Trying to invoke a static call (null target object) " +
                                                        $"on a {nameof(RemoteMethodInfo)} but it's associated " +
                                                        $"Declaring Type ({declaringType}) does not have a RemoteApp associated. " +
                                                        $"The type was either mis-constructed or it's not a {nameof(RemoteType)} object");
                }

                InvocationResults invokeRes = app.Communicator.InvokeStaticMethod(declaringType.FullName, funcName, genericArgsFullNames, remoteParams);
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
                // obj is NOT null. Make sure it's a RemoteObject.
                if (!(obj is RemoteObject ro))
                {
                    throw new NotImplementedException(
                        $"{nameof(RemoteMethodInfo)}.{nameof(Invoke)} only supports {nameof(RemoteObject)} targets at the moment.");
                }
                (hasResults, oora) = ro.InvokeMethod(funcName, genericArgsFullNames, remoteParams);
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
                RemoteObject ro = app.GetRemoteObject(oora.RemoteAddress, oora.Type);
                return ro.Dynamify();
            }
        }
        
    }
}