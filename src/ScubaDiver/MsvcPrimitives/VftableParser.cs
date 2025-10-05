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
    /// 1) ChildClass::~ChildClass (dtor)
    /// 2) ChildClass::Add      --{ Overridden method 
    /// 3) ParentClass::Remove  --{ Non-overridden method
    ///
    /// Assuming all function (3 in the example above) are exported, we'd find their names in <see cref="exportsList"/>
    /// and return them together with their names.
    /// </summary>
    public static List<UndecoratedFunction> AnalyzeVftable(HANDLE process, RichModuleInfo module, MsvcModuleExports moduleExports, TypeInfo type, nuint vftableAddress)
    {
        Logger.Debug("[AnalyzeVftable] Called");

        IReadOnlyList<ModuleSection> textSections = module.GetSections(".TEXT").ToList();

        List<UndecoratedFunction> virtualMethods = new List<UndecoratedFunction>();

        using var scanner = new RttiScanner(
            process,
            module.ModuleInfo.BaseAddress,
            module.ModuleInfo.Size,
            module.Sections
            );

        bool nextVftableFound = false;
        // Assuming at most 99 functions in the vftable.
        for (int i = 0; i < 100; i++)
        {
            // Check if this address is some other type's vftable address.
            // (Not checking the first one, since it's OUR vftable)
            nuint nextEntryAddress = (nuint)(vftableAddress + (nuint)(i * IntPtr.Size));
            if (i != 0 && moduleExports.TryGetVftable(nextEntryAddress, out _))
            {
                nextVftableFound = true;
                break;
            }

            // Read next vftable entry
            bool readNext = scanner.TryRead(nextEntryAddress, out nuint entryContent);
            if (!readNext)
                break;

            if (!moduleExports.TryGetFunc(entryContent, out UndecoratedFunction undecFunc))
            {
                // Check for anon-exported method of our type. We should still add it to the list.
                if (PointsToTextSection(textSections, entryContent))
                {
                    nuint subRelativeOffset = (nuint)(entryContent - module.ModuleInfo.BaseAddress);

                    string trimmedHex = subRelativeOffset.ToString("x16").TrimStart('0');
                    string subName = $"sub_{trimmedHex}";
                    undecFunc = new UndecoratedInternalFunction(
                        moduleInfo: module.ModuleInfo,
                        decoratedName: subName,
                        undecoratedFullName: $"{type.NamespaceAndName}::{subName}",
                        undecoratedName: subName,
                        address: entryContent,
                        numArgs: 1, // TODO: This is 99% wrong
                        retType: "void*" // TODO: Also XX% wrong
                        );
                }
                else
                {
                    continue;
                }
            }
            
            // Found a new virtual method for our type!
            virtualMethods.Add(undecFunc);
        }

        if (nextVftableFound)
        {
            return virtualMethods;
        }

        return new();
    }

    private static bool PointsToTextSection(IReadOnlyList<ModuleSection> textSections, nuint entryContent)
    {
        foreach (ModuleSection section in textSections)
        {
            if (section.BaseAddress <= entryContent && entryContent < section.BaseAddress + section.Size)
            {
                return true;
            }
        }
        return false;
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

        ulong attributes = 0;
        if (undecFunc is UndecoratedExportedFunc exportFunc)
        {
            if (exportFunc.IsStatic)
            {
                attributes |= (int)System.Reflection.MethodAttributes.Static;
            }
        }

        TypeDump.TypeMethod method = new()
        {
            Name = undecFunc.UndecoratedName,
            UndecoratedFullName = undecFunc.UndecoratedFullName,
            DecoratedName = undecFunc.DecoratedName,
            Parameters = parameters,
            ReturnTypeName = undecFunc.RetType,
            Visibility = "Public", // Because it's exported
            Attributes = attributes // Not sure about this one
        };
        return method;
    }
}