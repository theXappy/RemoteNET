using System;
using System.Collections.Generic;
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
    /// Assuming all function (3 in the example above) are exported, we'd find their names in <see cref="exportsList"/>
    /// and return them together with their names.
    /// </summary>
    public static List<UndecoratedFunction> AnalyzeVftable(HANDLE process, ModuleInfo module, IReadOnlyList<UndecoratedSymbol> exportsList, UndecoratedSymbol vftable)
    {
        List<UndecoratedFunction> virtualMethods = new List<UndecoratedFunction>();

        using var scanner = new RttiScanner(
            process,
            module.BaseAddress,
            module.Size);

        Dictionary<nuint, UndecoratedSymbol> exportsDict = exportsList
                                                .DistinctBy(exp => exp.Address)
                                                .ToDictionary(exp => (nuint)exp.Address);

        bool nextVftableFound = false;
        // Assuming at most 99 functions in the vftable.
        for (int i = 0; i < 100; i++)
        {
            // Check if this address is some other type's vftable address.
            // (Not checking the first one, since it's OUR vftable)
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

            if (!exportsDict.TryGetValue(entryContent, out UndecoratedSymbol undecSymbol))
                continue;

            if (undecSymbol is not UndecoratedFunction undecFunc) 
                continue;

            // Found a new virtual method for our type!
            virtualMethods.Add(undecFunc);
        }

        if (nextVftableFound)
        {
            return virtualMethods;
        }

        Logger.Debug($"[AnalyzeVftable] Next vftable not found starting at {vftable.UndecoratedName}");
        return new();

        bool IsVftableAddress(nuint addr)
        {
            if (!exportsDict.TryGetValue(addr, out UndecoratedSymbol undecoratedExport))
                return false;
            return undecoratedExport.UndecoratedName.EndsWith("`vftable'");
        }
    }

    // TODO: Move somewhere else
    public static TypeDump.TypeMethod ConvertToTypeMethod(UndecoratedFunction undecFunc)
    {
        List<TypeDump.TypeMethod.MethodParameter> parameters;
        string[] argTypes = undecFunc.ArgTypes;
        if (argTypes != null)
        {
            parameters = argTypes.Select((argType, i) =>
                new TypeDump.TypeMethod.MethodParameter()
                {
                    FullTypeName = argType,
                    Name = $"a{i}"
                }).ToList();
        }
        else
        {
            Logger.Debug(
                $"[{nameof(ConvertToTypeMethod)}] Failed to parse function's arguments. Undecorated name: {undecFunc.UndecoratedFullName} \r\nDecorated: {undecFunc.DecoratedName}");
            return null;
        }

        TypeDump.TypeMethod method = new()
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