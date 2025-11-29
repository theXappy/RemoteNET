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
    public static List<UndecoratedFunction> AnalyzeVftable(
        HANDLE process, 
        RichModuleInfo module, 
        MsvcModuleExports moduleExports, 
        TypeInfo type, 
        nuint vftableAddress, 
        MsvcTypesManager typesManager = null,
        bool verbose = false)
    {
        if (verbose)
            Logger.Debug("[VftableParser][AnalyzeVftable] Called");
        
        if (verbose)
            Logger.Debug($"[VftableParser][AnalyzeVftable] Parameters: process=0x{process.Value:x}, module={module.ModuleInfo.Name}, type={type.FullTypeName}, vftableAddress=0x{vftableAddress:x}, typesManager={(typesManager != null ? "provided" : "null")}");

        if (verbose)
            Logger.Debug("[VftableParser][AnalyzeVftable] Getting .TEXT sections from module");
        IReadOnlyList<ModuleSection> textSections = module.GetSections(".TEXT").ToList();
        if (verbose)
            Logger.Debug($"[VftableParser][AnalyzeVftable] Found {textSections.Count} .TEXT sections");

        List<UndecoratedFunction> virtualMethods = new List<UndecoratedFunction>();

        if (verbose)
            Logger.Debug("[VftableParser][AnalyzeVftable] Creating RttiScanner");
        using var scanner = new RttiScanner(
            process,
            module.ModuleInfo.BaseAddress,
            module.ModuleInfo.Size,
            module.Sections
            );
        if (verbose)
            Logger.Debug("[VftableParser][AnalyzeVftable] RttiScanner created successfully");

        bool nextVftableFound = false;
        if (verbose)
            Logger.Debug("[VftableParser][AnalyzeVftable] Starting vftable iteration (max 100 entries)");
        
        // Assuming at most 99 functions in the vftable.
        for (int i = 0; i < 100; i++)
        {
            // Check if this address is some other type's vftable address.
            // (Not checking the first one, since it's OUR vftable)
            nuint nextEntryAddress = (nuint)(vftableAddress + (nuint)(i * IntPtr.Size));
            
            if (verbose)
                Logger.Debug($"[VftableParser][AnalyzeVftable] Iteration {i}: nextEntryAddress = 0x{nextEntryAddress:x}");
            
            if (i != 0)
            {
                // Hybrid detection: Check both exports AND RTTI cache
                bool isVftableByExports = moduleExports.TryGetVftable(nextEntryAddress, out _);
                bool isVftableByCache = typesManager?.IsKnownVftableAddress(nextEntryAddress) ?? false;
                
                if (verbose)
                {
                    Logger.Debug($"[VftableParser][AnalyzeVftable] Iteration {i}: isVftableByExports={isVftableByExports}, isVftableByCache={isVftableByCache}");
                }
                
                // ✅ NEW: Check both exports AND cache
                if (isVftableByExports || isVftableByCache)
                {
                    string detectionMethod = isVftableByExports ? "exports" : "cache";
                    if (isVftableByExports && isVftableByCache)
                        detectionMethod = "both exports and cache";
                        
                    if (verbose)
                        Logger.Debug($"[VftableParser][AnalyzeVftable] Iteration {i}: Found another vftable at this address (detected via {detectionMethod}), stopping iteration");
                    nextVftableFound = true;
                    break;
                }
            }

            // Read next vftable entry
            if (verbose)
                Logger.Debug($"[VftableParser][AnalyzeVftable] Iteration {i}: Reading vftable entry at 0x{nextEntryAddress:x}");
            
            bool readNext = scanner.TryRead(nextEntryAddress, out nuint entryContent);
            if (!readNext)
            {
                if (verbose)
                    Logger.Debug($"[VftableParser][AnalyzeVftable] Iteration {i}: Failed to read vftable entry, stopping iteration");
                break;
            }

            if (verbose)
                Logger.Debug($"[VftableParser][AnalyzeVftable] Iteration {i}: Read entry content: 0x{entryContent:x}");

            if (verbose)
                Logger.Debug($"[VftableParser][AnalyzeVftable] Iteration {i}: Attempting to resolve function from module exports");
            
            if (!moduleExports.TryGetFunc(entryContent, out UndecoratedFunction undecFunc))
            {
                if (verbose)
                    Logger.Debug($"[VftableParser][AnalyzeVftable] Iteration {i}: Function not found in exports, checking if it points to .TEXT section");
                
                // Check for anon-exported method of our type. We should still add it to the list.
                if (PointsToTextSection(textSections, entryContent))
                {
                    if (verbose)
                        Logger.Debug($"[VftableParser][AnalyzeVftable] Iteration {i}: Entry points to .TEXT section, creating anonymous function");
                    
                    nuint subRelativeOffset = (nuint)(entryContent - module.ModuleInfo.BaseAddress);

                    string trimmedHex = subRelativeOffset.ToString("x16").TrimStart('0');
                    string subName = $"sub_{trimmedHex}";
                    
                    if (verbose)
                        Logger.Debug($"[VftableParser][AnalyzeVftable] Iteration {i}: Creating UndecoratedInternalFunction: {subName} at offset 0x{subRelativeOffset:x}");
                    
                    undecFunc = new UndecoratedInternalFunction(
                        moduleInfo: module.ModuleInfo,
                        decoratedName: subName,
                        undecoratedFullName: $"{type.NamespaceAndName}::{subName}",
                        undecoratedName: subName,
                        address: entryContent,
                        numArgs: 1, // TODO: This is 99% wrong
                        retType: "void*" // TODO: Also XX% wrong
                        );
                    
                    if (verbose)
                        Logger.Debug($"[VftableParser][AnalyzeVftable] Iteration {i}: Anonymous function created successfully");
                }
                else
                {
                    if (verbose)
                        Logger.Debug($"[VftableParser][AnalyzeVftable] Iteration {i}: Entry does not point to .TEXT section, skipping");
                    continue;
                }
            }
            else
            {
                if (verbose)
                    Logger.Debug($"[VftableParser][AnalyzeVftable] Iteration {i}: Function resolved from exports: {undecFunc.UndecoratedFullName} at 0x{undecFunc.Address:x}");
            }
            
            // Found a new virtual method for our type!
            virtualMethods.Add(undecFunc);
            if (verbose)
                Logger.Debug($"[VftableParser][AnalyzeVftable] Iteration {i}: Added function to virtual methods list. Total count: {virtualMethods.Count}");
        }

        if (verbose)
            Logger.Debug($"[VftableParser][AnalyzeVftable] Iteration completed. nextVftableFound={nextVftableFound}, virtualMethods.Count={virtualMethods.Count}");

        if (nextVftableFound)
        {
            if (verbose)
                Logger.Debug($"[VftableParser][AnalyzeVftable] Returning {virtualMethods.Count} virtual methods (stopped at next vftable)");
            return virtualMethods;
        }

        if (verbose)
            Logger.Debug("[VftableParser][AnalyzeVftable] Returning empty list (no next vftable found)");
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