using NtApiDotNet.Win32;
using ScubaDiver.Rtti;

namespace ScubaDiver;

public static class DllExportExt
{
    public static string UndecorateName(this DllExport export)
    {
        return RttiScanner.UnDecorateSymbolNameWrapper(export.Name);
    }

    public static bool TryUndecorate(this DllExport input, out UndecoratedFunction output)
    {
        output = null;
        string undecoratedName;
        try
        {
            undecoratedName = input.UndecorateName();
        }
        catch
        {
            return false;
        }

        output = new UndecoratedExport(undecoratedName, input);
        return true;
    }
}