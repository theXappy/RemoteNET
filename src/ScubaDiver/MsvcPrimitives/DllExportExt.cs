using NtApiDotNet;
using NtApiDotNet.Win32;
using ScubaDiver.Rtti;
using System;
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


        Lazy<int?> lazyNumArgs = new Lazy<int?>(() =>
        {
            SerializedType sig;
            if (input.Name.FirstOrDefault() != '?')
                return null;
            try
            {
                var parser = new MsMangledNameParser(input.Name);
                (_, sig, _) = parser.Parse();
            }
            catch (Exception ex)
            {
                //Logger.Debug($"Failed to demangle name of function, Raw: {input.Name}, Exception: " + ex.Message);
                return null;
            }

            Argument_v1[] args = (sig as SerializedSignature)?.Arguments;
            if (args == null)
            {
                // Failed to parse?!?
                //Logger.Debug($"Failed to parse arguments of function, Raw: {input.Name}");
                return null;
            }

            return args.Length;
        });

        output = new UndecoratedExport(basicUndecoratedName, lazyNumArgs, input, module);
        return true;
    }
}