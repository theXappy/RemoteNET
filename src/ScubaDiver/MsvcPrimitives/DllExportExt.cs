using NtApiDotNet.Win32;
using ScubaDiver.Rtti;
using System;
using System.Collections.Generic;
using System.Linq;
using ScubaDiver.Demangle.Demangle;
using ScubaDiver.Demangle.Demangle.Core.Serialization;
using Microsoft.Extensions.Primitives;

namespace ScubaDiver;

public static class DllExportExt
{
    public static string UndecorateName(this DllExport export)
    {
        return RttiScanner.UnDecorateSymbolNameWrapper(export.Name);
    }

    public static bool TryUndecorate(this DllExport input, ModuleInfo module, out UndecoratedSymbol output)
    {
        output = null;
        string undecoratedFullName;
        try
        {
            undecoratedFullName = UndecorateName(input);
        }
        catch
        {
            return false;
        }
        string className = "";
        string undecoratedName = undecoratedFullName;
        if (undecoratedFullName.Contains("::"))
        {
            className = undecoratedFullName[..(undecoratedFullName.LastIndexOf("::"))];
            undecoratedName = undecoratedFullName[(undecoratedFullName.LastIndexOf("::") + 2)..];
        }
        else if (input.Name == undecoratedFullName)
        {
            // No namespace (no '::' separator) and the name wasn't demangled, it remained the same.
            // So this is not a decorated symbol in the first place...
            return false;
        }

        bool isFunc = MsMangledNameParser.IsFunction(input.Name);

        if (isFunc)
        {
            Lazy<DemangledSignature> lazyArgs = new Lazy<DemangledSignature>(() =>
            {
                SerializedType sig;
                if (input.Name.FirstOrDefault() != '?')
                    return DemangledSignature.Empty;
                try
                {
                    var parser = new MsMangledNameParser(input.Name);
                    (_, sig, _) = parser.Parse();
                }
                catch (Exception)
                {
                    //Logger.Debug($"Failed to demangle name of function, Raw: {input.Name}, Exception: " + ex.Message);
                    return DemangledSignature.Empty;
                }

                if (sig is not SerializedSignature serSig)
                {
                    // Failed to parse?!?
                    //Logger.Debug($"Failed to parse arguments of function, Raw: {input.Name}");
                    return DemangledSignature.Empty;
                }


                List<RestarizedParameter> restParameters;
                RestarizedParameter resRetType;
                bool isRetNonRefStruct;
                try
                {
                    restParameters = TypesRestarizer.RestarizeParameters(serSig);
                    resRetType = TypesRestarizer.RestarizeArgument(serSig.ReturnValue);

                    // Non-ref return values are stringified to "arg(some_struct_name)"
                    // Logic below looks for this pattern.
                    string retValueToString = serSig.ReturnValue.ToString();
                    // Removing "arg(" and ")";
                    retValueToString = retValueToString.Substring("arg(".Length, retValueToString.Length - 5);
                    isRetNonRefStruct = !retValueToString.Contains('(') && retValueToString != "void";
                }
                catch
                {
                    // Failed to parse?!?
                    Logger.Debug(
                        $"[TypesRestarizer.RestarizeParameters] Failed to parse arguments of function, Raw: {input.Name}");
                    return DemangledSignature.Empty;
                }


                string[] argTypes = restParameters.Select(param => param.FriendlyName).ToArray();
                string retType = resRetType.FriendlyName;

                return new DemangledSignature
                {
                    ArgTypes = argTypes,
                    RetType = retType,
                    IsRetNonRefStruct = isRetNonRefStruct,
                };
            });

            output = new UndecoratedExportedFunc(className, undecoratedName, undecoratedFullName, lazyArgs, input, module);
            return true;
        }
        else
        {
            // Not a function - assuming it's a field
            output = new UndecoratedExportedField((nuint)input.Address, undecoratedName, undecoratedFullName, input, module);
            return true;
        }
    }
}