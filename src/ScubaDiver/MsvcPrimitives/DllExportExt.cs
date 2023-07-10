using NtApiDotNet;
using NtApiDotNet.Win32;
using ScubaDiver.Rtti;
using System;
using System.Collections.Generic;
using System.Linq;
using ScubaDiver.Demangle.Demangle;
using ScubaDiver.Demangle.Demangle.Core.Serialization;

namespace ScubaDiver;

public static class DllExportExt
{
    public static string UndecorateName(this DllExport export)
    {
        return RttiScanner.UnDecorateSymbolNameWrapper(export.Name);
    }

    public static bool TryUndecorate(this DllExport input, ModuleInfo module, out UndecoratedFunction output)
    {
        output = null;
        string basicUndecoratedName;
        try
        {
            basicUndecoratedName = UndecorateName(input);
        }
        catch
        {
            return false;
        }


        Lazy<(string, string[])> lazyArgs = new Lazy<(string, string[])>(() =>
        {
            SerializedType sig;
            if (input.Name.FirstOrDefault() != '?')
                return (null, null);
            try
            {
                var parser = new MsMangledNameParser(input.Name);
                (_, sig, _) = parser.Parse();
            }
            catch (Exception ex)
            {
                //Logger.Debug($"Failed to demangle name of function, Raw: {input.Name}, Exception: " + ex.Message);
                return (null, null);
            }

            if (sig is not SerializedSignature serSig)
            {
                // Failed to parse?!?
                //Logger.Debug($"Failed to parse arguments of function, Raw: {input.Name}");
                return (null, null);
            }


            List<RestarizedParameter> restParameters;
            RestarizedParameter resRetType;
            try
            {
                restParameters = TypesRestarizer.RestarizeParameters(serSig);
                resRetType = TypesRestarizer.RestarizeArgument(serSig.ReturnValue);
            }
            catch
            {
                // Failed to parse?!?
                Logger.Debug($"[TypesRestarizer.RestarizeParameters] Failed to parse arguments of function, Raw: {input.Name}");
                return (null, null);
            }

            string[] argTypes = restParameters.Select(param => param.FriendlyName).ToArray();
            string retType = resRetType.FriendlyName;
            return (retType, argTypes);
        });

        string className = "";
        if (basicUndecoratedName.Contains("::"))
            className = basicUndecoratedName[..(basicUndecoratedName.LastIndexOf("::"))];
        output = new UndecoratedExport(className, basicUndecoratedName, lazyArgs, input, module);
        return true;
    }
}