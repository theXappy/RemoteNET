using System;
using System.Collections.Generic;
using NtApiDotNet.Win32;
using ScubaDiver.API.Interactions.Dumps;
using ScubaDiver;
using ScubaDiver.Rtti;
using System.Linq;
using Windows.Win32.Foundation;

public static class VftableParser
{
    /// <summary>
    /// Tries to parse the different functions in some type's vftable.
    /// Note that inheritance might cause some of the methods in the table to have names of base classes.
    /// For example, a vftable of a ChildClass might look like his:
    /// 1) ~ChildClass (dtor)
    /// 2) ChildClass::Add      --{ Overridden method 
    /// 3) ParentClass::Remove  --{ Non-overridden method
    ///
    /// Assuming all function (3 in the example above) are exported, we'd find their names in <see cref="mangledExports"/>
    /// and return them together with their names.
    /// </summary>
    public static List<ManagedTypeDump.TypeMethod> AnalyzeVftable(HANDLE process, ModuleInfo module, IReadOnlyList<DllExport> exportsList, UndecoratedSymbol vftable)
    {
        List<ManagedTypeDump.TypeMethod> virtualMethods = new List<ManagedTypeDump.TypeMethod>();

        using var scanner = new RttiScanner(
            process,
            module.BaseAddress,
            module.Size);

        Dictionary<nuint, DllExport> exports = exportsList
                                                .DistinctBy(exp => exp.Address)
                                                .ToDictionary(exp => (nuint)exp.Address);

        bool nextVftableFound = false;
        // Assuming at most 99 functions in the vftable.
        for (int i = 0; i < 100; i++)
        {
            // Check if this address is some other type's vftable address.
            // (Not checking the first one, since it OUR vftable)
            nuint nextEntryAddress = (nuint)(vftable.Address + (i * IntPtr.Size));
            if (i != 0 && IsVftableAddress(nextEntryAddress))
            {
                nextVftableFound = true;
                break;
            }

            // Read next vftable entry
            bool readNext = scanner.TryRead(nextEntryAddress, out nuint entryContent);
            if (!readNext)
                break;

            if (!exports.TryGetValue(entryContent, out DllExport exportInfo))
                continue;

            if (!exportInfo.TryUndecorate(module, out UndecoratedSymbol undecSymbol))
            {
                Logger.Debug($"[AnalyzeVftable] Failed to undecorate. Name: {exportInfo.Name}");
                continue;
            }

            if (undecSymbol is not UndecoratedFunction undecFunc) 
                continue;

            // Converting to type method (parsing parameters)
            ManagedTypeDump.TypeMethod m = ConvertToTypeMethod(undecFunc);
            if (m == null) 
                continue;

            // Found a new virtual method for our type!
            virtualMethods.Add(m);
        }

        if (nextVftableFound)
        {
            return virtualMethods;
        }

        Logger.Debug($"[AnalyzeVftable] Next vftable not found starting at {vftable.UndecoratedName}");
        return new List<ManagedTypeDump.TypeMethod>();

        bool IsVftableAddress(nuint addr)
        {
            if (!exports.TryGetValue(addr, out DllExport exportInfo))
                return false;
            if (!exportInfo.TryUndecorate(module, out UndecoratedSymbol undecoratedExport))
                return false;
            return undecoratedExport.UndecoratedName.EndsWith("`vftable'");
        }
    }

    // TODO: Move somewhere else
    public static ManagedTypeDump.TypeMethod ConvertToTypeMethod(UndecoratedFunction undecFunc)
    {
        List<ManagedTypeDump.TypeMethod.MethodParameter> parameters;
        string[] argTypes = undecFunc.ArgTypes;
        if (argTypes != null)
        {
            parameters = argTypes.Select((argType, i) =>
                new ManagedTypeDump.TypeMethod.MethodParameter()
                {
                    FullTypeName = argType,
                    Name = $"a{i}"
                }).ToList();
        }
        else
        {
            Logger.Debug(
                $"[{nameof(ConvertToTypeMethod)}] Failed to parse function's argumenst. Undecorated name: {undecFunc.UndecoratedFullName}");
            return null;
        }

        ManagedTypeDump.TypeMethod method = new()
        {
            Name = undecFunc.UndecoratedName,
            UndecoratedFullName = undecFunc.UndecoratedFullName,
            DecoratedName = undecFunc.DecoratedName,
            Parameters = parameters,
            ReturnTypeName = undecFunc.RetType,
            Visibility = "Public" // Because it's exported
        };
        return method;
    }
}