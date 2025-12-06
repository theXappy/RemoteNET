using NtApiDotNet.Win32;
using ScubaDiver.API.Utils;
using ScubaDiver.Rtti;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace ScubaDiver
{
    public class MsvcModuleFilter
    {
        /// <summary>
        /// Only accept modules with names that pass this predicate.
        /// </summary>
        public Predicate<string> NamePredicate { get; set; }
        /// <summary>
        /// Only accept modules that are imported INTO the module named in this property.
        /// </summary>
        public string ImportingModule { get; set; }

        public MsvcModuleFilter()
        {
            NamePredicate = (s) => true;
            ImportingModule = null;
        }
    }

    public interface ISymbolBackedMember
    {
        public UndecoratedSymbol Symbol { get; }
    }

    public class VftableInfo : FieldInfo, ISymbolBackedMember
    {
        private MsvcType _type;
        private nuint _xoredAddress;
        private string _name;
        
        public UndecoratedExportedField ExportedField { get; set; }
        UndecoratedSymbol ISymbolBackedMember.Symbol => ExportedField;

        // Constructor for exported vftables
        public VftableInfo(MsvcType msvcType, UndecoratedExportedField symbol)
        {
            _type = msvcType;
            ExportedField = symbol;
            _xoredAddress = symbol.Address;
            _name = symbol.UndecoratedName;
        }
        
        // Constructor for RTTI vftables (not exported)
        public VftableInfo(MsvcType msvcType, nuint xoredVftableAddress, string name = null)
        {
            _type = msvcType;
            ExportedField = null;
            _xoredAddress = xoredVftableAddress;
            _name = name ?? $"`vftable' (at 0x{xoredVftableAddress ^ FirstClassTypeInfo.XorMask:x})";
        }

        public override string Name => _name;
        public ulong Address => _xoredAddress ^ FirstClassTypeInfo.XorMask;
        public override Type DeclaringType => _type;
        public override object GetValue(object obj) => Address;

        public override Type FieldType => throw new NotImplementedException();
        public override FieldAttributes Attributes => throw new NotImplementedException();
        public override RuntimeFieldHandle FieldHandle => throw new NotImplementedException();
        public override Type ReflectedType => throw new NotImplementedException();
        public override void SetValue(object obj, object value, BindingFlags invokeAttr, Binder binder, CultureInfo culture) => throw new NotImplementedException();
        public override object[] GetCustomAttributes(bool inherit) => throw new NotImplementedException();
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => throw new NotImplementedException();
        public override bool IsDefined(Type attributeType, bool inherit) => throw new NotImplementedException();
        public override string ToString() => Name;
    }

    public class MsvcMethod : MethodInfo, ISymbolBackedMember
    {
        public UndecoratedFunction UndecoratedFunc { get; set; }
        UndecoratedSymbol ISymbolBackedMember.Symbol => UndecoratedFunc;

        private MsvcType _type;

        public MsvcMethod(MsvcType type, UndecoratedFunction exportedFuncInfo)
        {
            _type = type;
            UndecoratedFunc = exportedFuncInfo;
        }

        public override ParameterInfo[] GetParameters() => throw new NotImplementedException();


        public override MsvcType DeclaringType => _type;
        public override string Name => UndecoratedFunc.UndecoratedName;
        public override Type ReturnType => throw new NotImplementedException();
        public override MethodAttributes Attributes
        {
            get
            {
                if (this.UndecoratedFunc is UndecoratedExportedFunc expFunc)
                {
                    if (expFunc.IsStatic)
                        return MethodAttributes.Static;
                }
                return 0;
            }
        }
        public override RuntimeMethodHandle MethodHandle => throw new NotImplementedException();
        public override Type ReflectedType => throw new NotImplementedException();
        public override ICustomAttributeProvider ReturnTypeCustomAttributes => throw new NotImplementedException();
        public override MethodInfo GetBaseDefinition() => throw new NotImplementedException();
        public override object[] GetCustomAttributes(bool inherit) => throw new NotImplementedException();
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => throw new NotImplementedException();
        public override MethodImplAttributes GetMethodImplementationFlags() => throw new NotImplementedException();
        public override object Invoke(object obj, BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture) => throw new NotImplementedException();
        public override bool IsDefined(Type attributeType, bool inherit) => throw new NotImplementedException();
        public override string ToString() => throw new NotImplementedException();
    }

    public class MsvcModule : Module
    {
        public ModuleInfo ModuleInfo { get; set; }
        public override string Name => ModuleInfo.Name;
        public nuint BaseAddress => ModuleInfo.BaseAddress;

        public MsvcModule(ModuleInfo moduleInfo)
        {
            ModuleInfo = moduleInfo;
        }
    }

    public class MsvcTypeStub
    {
        public Rtti.TypeInfo TypeInfo { get; set; }

        private Lazy<MsvcType> _upgrader;

        public MsvcTypeStub(Rtti.TypeInfo typeInfo, Lazy<MsvcType> upgrader)
        {
            TypeInfo = typeInfo;
            _upgrader = upgrader;
        }

        public MsvcType Upgrade() => _upgrader.Value;
    }

    public class MsvcType : Type
    {
        private MsvcModule _module;
        private MsvcMethod[] _methods;
        private VftableInfo[] _vftables;
        private List<MsvcMethod> _customMethods = new List<MsvcMethod>();
        public Rtti.TypeInfo TypeInfo { get; set; }

        public MsvcType(MsvcModule module, Rtti.TypeInfo typeInfo)
        {
            _module = module;
            TypeInfo = typeInfo;
        }

        public void SetMethods(MsvcMethod[] methods)
        {
            _methods = methods;
        }
        public void SetVftables(VftableInfo[] vftables)
        {
            _vftables = vftables;
        }

        public void AddCustomMethod(MsvcMethod method)
        {
            _customMethods.Add(method);
        }

        public override MsvcModule Module => _module;

        public override string Name => TypeInfo.Name;
        public override string Namespace => TypeInfo.Namespace;
        public override string FullName => TypeInfo.FullTypeName;


        public override MsvcMethod[] GetMethods(BindingFlags bindingAttr)
        {
            if (_methods == null)
                return _customMethods.ToArray();
            return _methods.Concat(_customMethods).ToArray();
        }
        public new MsvcMethod[] GetMethods() => GetMethods(0);
        public VftableInfo[] GetVftables() => _vftables;
        public override MemberInfo[] GetMembers(BindingFlags bindingAttr) => [.. GetVftables(), .. GetMethods()];
        public override string ToString() => FullName;

        // TODO: we CAN add the constructors here, right now they're just shoved into the methods list
        public override ConstructorInfo[] GetConstructors(BindingFlags bindingAttr) => Array.Empty<ConstructorInfo>();
        public override FieldInfo GetField(string name, BindingFlags bindingAttr) => throw new NotImplementedException();
        public override FieldInfo[] GetFields(BindingFlags bindingAttr) => throw new NotImplementedException();
        public override PropertyInfo[] GetProperties(BindingFlags bindingAttr) => throw new NotImplementedException();



        // Useless properties
        public override Assembly Assembly => null;
        public override string AssemblyQualifiedName => throw new NotImplementedException();
        public override Type BaseType => throw new NotImplementedException();
        public override Guid GUID => throw new NotImplementedException();
        public override Type UnderlyingSystemType => throw new NotImplementedException();
        protected override bool HasElementTypeImpl() => throw new NotImplementedException();
        protected override bool IsArrayImpl() => throw new NotImplementedException();
        protected override bool IsByRefImpl() => throw new NotImplementedException();
        public override bool IsDefined(Type attributeType, bool inherit) => throw new NotImplementedException();
        protected override bool IsCOMObjectImpl() => throw new NotImplementedException();
        protected override bool IsPointerImpl() => throw new NotImplementedException();
        protected override bool IsPrimitiveImpl() => throw new NotImplementedException();

        //Useless methods
        public override object[] GetCustomAttributes(bool inherit) => throw new NotImplementedException();
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => throw new NotImplementedException();
        public override Type GetElementType() => throw new NotImplementedException();
        public override EventInfo GetEvent(string name, BindingFlags bindingAttr) => throw new NotImplementedException();
        public override EventInfo[] GetEvents(BindingFlags bindingAttr) => throw new NotImplementedException();

        [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)]
        public override Type GetInterface(string name, bool ignoreCase) => throw new NotImplementedException();
        public override Type[] GetInterfaces() => throw new NotImplementedException();
        public override Type GetNestedType(string name, BindingFlags bindingAttr) => throw new NotImplementedException();
        public override Type[] GetNestedTypes(BindingFlags bindingAttr) => throw new NotImplementedException();
        public override object InvokeMember(string name,
            BindingFlags invokeAttr,
            Binder binder,
            object target,
            object[] args,
            ParameterModifier[] modifiers,
            CultureInfo culture,
            string[] namedParameters)
            => throw new NotImplementedException();
        protected override TypeAttributes GetAttributeFlagsImpl() => throw new NotImplementedException();
        protected override ConstructorInfo GetConstructorImpl(BindingFlags bindingAttr, Binder binder, CallingConventions callConvention, Type[] types, ParameterModifier[] modifiers) => throw new NotImplementedException();
        protected override MethodInfo GetMethodImpl(string name, BindingFlags bindingAttr, Binder binder, CallingConventions callConvention, Type[] types, ParameterModifier[] modifiers) => throw new NotImplementedException();
        protected override PropertyInfo GetPropertyImpl(string name, BindingFlags bindingAttr, Binder binder, Type returnType, Type[] types, ParameterModifier[] modifiers) => throw new NotImplementedException();
    }

    public class MsvcTypesManager
    {
        private TricksterWrapper _tricksterWrapper = null;
        private MemoryScanner _memoryScanner = null;
        private IReadOnlyExportsMaster _exportsMaster = null;

        // Types cache.
        //  <Module name to: <Type name to: Type>>
        Dictionary<string, Dictionary<string, MsvcTypeStub>> _typesCache = new();

        // Modules cache.
        //  <Module name to: MsvcModule>
        Dictionary<string, MsvcModule> _modulesCache = new();

        // Vftables cache.
        //  <vftable address : Type>
        Dictionary<nuint, MsvcTypeStub> _vftablesCache = new();

        // All known vftable addresses from RTTI-discovered types
        // Populated during GetTypes() to enable boundary detection in VftableParser
        private HashSet<nuint> _allKnownXoredVftableAddresses = new HashSet<nuint>();

        private Dictionary<ModuleInfo, MsvcModuleExports> _exportsCache;

        public MsvcTypesManager()
        {
            _tricksterWrapper = new TricksterWrapper();
            _memoryScanner = new MemoryScanner();
            _exportsMaster = _tricksterWrapper.ExportsMaster;
            _exportsCache = new();
        }

        public List<UndecoratedModule> GetUndecoratedModules(MsvcModuleFilter filter = null)
        {
            if (_tricksterWrapper.RefreshRequired())
                _tricksterWrapper.Refresh();

            filter ??= new MsvcModuleFilter();
            var results = _tricksterWrapper.GetUndecoratedModules(filter.NamePredicate);
            if (filter.ImportingModule != null)
            {
                IReadOnlyList<DllImport> imports = _tricksterWrapper.ExportsMaster.GetImports(filter.ImportingModule);
                if (imports == null)
                {
                    // TODO: Something else where no modules could be found in the imports table??
                    results = new List<UndecoratedModule>();
                }
                else
                {
                    results = results.Where(module => IsImportedInto(module.ModuleInfo.Name, imports)).ToList();
                }
            }
            return results;

            bool IsImportedInto(string moduleName, IReadOnlyList<DllImport> imports)
            {
                return imports.Any(imp => imp.DllName == moduleName);
            }
        }
        public List<UndecoratedModule> GetUndecoratedModules(Predicate<string> moduleNameFilter) => GetUndecoratedModules(new MsvcModuleFilter() { NamePredicate = moduleNameFilter });

        internal void RefreshIfNeeded()
        {
            if (_tricksterWrapper.RefreshRequired())
            {
                _tricksterWrapper.Refresh();
                
                // Clear vftable cache on refresh since runtime may have changed
                lock (_getTypesLock)
                {
                    _allKnownXoredVftableAddresses.Clear();
                }
            }
        }
        
        /// <summary>
        /// Ensures the vftable address cache is populated with all known vftables from RTTI.
        /// This method is idempotent - calling it multiple times is safe and efficient (no-op if already populated).
        /// </summary>
        /// <param name="verbose">Enable verbose logging for debugging</param>
        private void EnsureVftableCachePopulated()
        {
            lock (_getTypesLock)
            {
                // If cache is already populated, no work needed
                if (_allKnownXoredVftableAddresses.Count > 0)
                    return;
                
                // Get all modules (with refresh if needed)
                List<UndecoratedModule> modules = GetUndecoratedModules();
                // Populate cache from all FirstClassTypeInfo instances across all modules
                foreach (UndecoratedModule undecoratedModule in modules)
                {   
                    foreach (Rtti.TypeInfo type in undecoratedModule.Types)
                    {
                        if (type is not FirstClassTypeInfo firstClass)
                            continue;

                        _allKnownXoredVftableAddresses.Add(firstClass.XoredVftableAddress);

                        // Also add secondary vftables (multiple inheritance)
                        foreach (nuint xoredSecondaryVftable in firstClass.XoredSecondaryVftableAddresses ?? Enumerable.Empty<nuint>())
                        {
                            _allKnownXoredVftableAddresses.Add(xoredSecondaryVftable);
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Checks if the given address is a known vftable address from RTTI-discovered types.
        /// Automatically populates the cache on first call if needed.
        /// </summary>
        /// <param name="xoredVftableAddress">The address to check</param>
        /// <param name="verbose">Enable verbose logging for debugging</param>
        /// <returns>True if this address is a known vftable</returns>
        public bool IsKnownVftableAddress(nuint xoredVftableAddress)
        {
            // Ensure cache is populated before querying
            EnsureVftableCachePopulated();
            
            lock (_getTypesLock)
            {
                return _allKnownXoredVftableAddresses.Contains(xoredVftableAddress);
            }
        }

        private object _getTypesLock = new object();
        public IReadOnlyList<MsvcTypeStub> GetTypes(MsvcModuleFilter moduleFilter, Predicate<string> typeFilter)
        {
            List<MsvcTypeStub> output = new List<MsvcTypeStub>();
            lock (_getTypesLock)
            {
                RefreshIfNeeded();
                
                // PHASE 1: Ensure vftable cache is populated before creating any stubs
                // This ensures the cache is complete before any AnalyzeVftable calls
                EnsureVftableCachePopulated();
                
                // PHASE 2: Now create stubs (which may lazy-call CreateType → AnalyzeVftable)
                // At this point, the cache has ALL vftables from all modules
                moduleFilter ??= new MsvcModuleFilter();
                typeFilter ??= (s) => true;
                List<UndecoratedModule> modules = GetUndecoratedModules(moduleFilter);
                
                foreach (UndecoratedModule undecoratedModule in modules)
                {
                    foreach (Rtti.TypeInfo type in undecoratedModule.Types)
                    {
                        if (!typeFilter(type.NamespaceAndName))
                            continue;

                        MsvcTypeStub t = GetOrCreateTypeStub(undecoratedModule.RichModule, type);
                        output.Add(t);
                    }
                }
            }
            return output;
        }

        public IReadOnlyList<MsvcTypeStub> GetTypes(Predicate<string> moduleNameFilter, Predicate<string> typeFilter) 
                    => GetTypes(new MsvcModuleFilter() { NamePredicate = moduleNameFilter }, typeFilter).ToList();


        public MsvcTypeStub GetType(MsvcModuleFilter moduleFilter, Predicate<string> typeFilter)
        {
            IEnumerable<MsvcTypeStub> lazyMatches = GetTypes(moduleFilter, typeFilter);
            // Assert we only have one match without throwing an exception or parsing needlessly the third, forth,...
            MsvcTypeStub[] firstMatches = lazyMatches.Take(2).ToArray();
            if (firstMatches.Length == 1)
                return firstMatches[0];
            return null;
        }

        public MsvcTypeStub GetType(Predicate<string> moduleNameFilter, Predicate<string> typeFilter) => GetType(new MsvcModuleFilter() { NamePredicate = moduleNameFilter }, typeFilter);
        public MsvcTypeStub GetType(string moduleName, string typeName) => GetType((s) => s == moduleName, (s) => s == typeName);

        public MsvcTypeStub GetType(nuint vftable)
        {
            RefreshIfNeeded();
            if (_vftablesCache.TryGetValue(vftable, out MsvcTypeStub cachedType))
                return cachedType;

            // Find the module and type of the vftable
            ModuleInfo? module = GetContainingModule(vftable);
            if (module == null)
                return null;

            // Find the type of the vftable
            MsvcModuleExports moduleExports = GetOrCreateModuleExports(module.Value);
            if (!moduleExports.TryGetVftable(vftable, out var vftableSymbol))
                return null;

            // Extract name of type
            string typeName = vftableSymbol.UndecoratedFullName;
            if (!typeName.Contains("::`vftable'"))
                return null;
            typeName = typeName.Substring(0, typeName.Length - "::`vftable'".Length);

            // Use regular search
            MsvcTypeStub res = GetType(module.Value.Name, typeName);

            // Cache and return
            if (res != null)
                _vftablesCache[vftable] = res;
            return res;

            ModuleInfo? GetContainingModule(nuint address)
            {
                foreach (var module in _tricksterWrapper.GetModules())
                {
                    if (module.BaseAddress <= address && address < module.BaseAddress + module.Size)
                        return module;
                }
                return null;
            }
        }

        private MsvcTypeStub GetOrCreateTypeStub(RichModuleInfo module, Rtti.TypeInfo rawType)
        {
            // Search cache
            if (!_typesCache.TryGetValue(module.ModuleInfo.Name, out var moduleTypes))
            {
                moduleTypes = new Dictionary<string, MsvcTypeStub>();
                _typesCache[module.ModuleInfo.Name] = moduleTypes;
            }
            if (moduleTypes.TryGetValue(rawType.NamespaceAndName, out MsvcTypeStub cachedType))
                return cachedType; // Cache hit!

            // No hit, creating new type.
            // First, get or create MsvcModule
            MsvcTypeStub newType = CreateTypeStub(module, rawType);

            // Cache and return
            moduleTypes[rawType.NamespaceAndName] = newType;
            return newType;
        }

        private MsvcTypeStub CreateTypeStub(RichModuleInfo module, Rtti.TypeInfo type)
        {
            Func<MsvcType> upgrader = () =>
            {
                Logger.Debug($"[MsvcTypesManager] Upgrading type {type.FullTypeName}");
                return CreateType(module, type);
            };
            MsvcTypeStub newType = new MsvcTypeStub(type, new Lazy<MsvcType>(upgrader));
            return newType;
        }

        private MsvcType CreateType(RichModuleInfo module, Rtti.TypeInfo type)
        {            
            if (!_modulesCache.TryGetValue(module.ModuleInfo.Name, out MsvcModule msvcModule))
            {
                msvcModule = new MsvcModule(module.ModuleInfo);
                _modulesCache[module.ModuleInfo.Name] = msvcModule;
            }

            // Create hollow type
            MsvcType finalType = new MsvcType(msvcModule, type);

            // Get all exported members of the requested type
            List<UndecoratedSymbol> rawMembers = _exportsMaster.GetExportedTypeMembers(module.ModuleInfo, type.NamespaceAndName).ToList();
            
            List<UndecoratedFunction> exportedFuncs = rawMembers.OfType<UndecoratedFunction>().ToList();

            // Collect all vftable addresses and create VftableInfo objects from two sources:
            // 1. Exported vftables (with UndecoratedExportedField wrappers) - PRIORITIZED
            // 2. TypeInfo's vftable (if it's a FirstClassTypeInfo) - Only if not exported
            
            List<nuint> allXoredVftableAddresses = new List<nuint>();
            List<VftableInfo> allVftableInfos = new List<VftableInfo>();
            
            // Source 1: Find exported vftables (PRIORITIZED - added first)
            UndecoratedExportedField[] exportedVftables = rawMembers.OfType<UndecoratedExportedField>()
                                            .Where(member => member.UndecoratedName.EndsWith("`vftable'"))
                                            .ToArray();
            
            foreach (UndecoratedExportedField exportedVftable in exportedVftables)
            {
                allXoredVftableAddresses.Add(exportedVftable.XoredAddress);
                allVftableInfos.Add(new VftableInfo(finalType, exportedVftable));
            }
            
            // Source 2: Add RTTI vftable addresses (primary + secondary) ONLY if not already exported
            if (type is FirstClassTypeInfo firstClass)
            {
                // Add primary vftable ONLY if not already in the list (i.e., not exported)
                if (!allXoredVftableAddresses.Contains(firstClass.XoredVftableAddress))
                {
                    allXoredVftableAddresses.Add(firstClass.XoredVftableAddress);
                    allVftableInfos.Add(new VftableInfo(finalType, firstClass.XoredVftableAddress, $"`vftable' (primary, non-exported)"));
                }
                
                // Add secondary vftables ONLY if not already in the list (i.e., not exported)
                if (firstClass.SecondaryVftableAddresses != null)
                {
                    int secondaryIndex = 0;
                    foreach (nuint xoredSecondaryVftable in firstClass.XoredSecondaryVftableAddresses)
                    {
                        if (!allXoredVftableAddresses.Contains(xoredSecondaryVftable))
                        {
                            allXoredVftableAddresses.Add(xoredSecondaryVftable);
                            allVftableInfos.Add(new VftableInfo(finalType, xoredSecondaryVftable, $"`vftable' (secondary #{secondaryIndex}, non-exported)"));
                            secondaryIndex++;
                        }
                        else
                        {
                            // Secondary vftable is exported, increment counter anyway for consistent numbering
                            secondaryIndex++;
                        }
                    }
                }
            }

            // Set all vftables (exported ones first, then non-exported RTTI ones)
            finalType.SetVftables(allVftableInfos.ToArray());

            // Find all virtual methods (from all vftables)
            List<UndecoratedFunction> virtualFuncs = new List<UndecoratedFunction>();
            MsvcModuleExports moduleExports = GetOrCreateModuleExports(module.ModuleInfo);
            for (int i = 0; i < allXoredVftableAddresses.Count; i++)
            {
                nuint xoredVftableAddress = allXoredVftableAddresses[i];                
                try
                {                    
                    // ✅ Pass 'this' to VftableParser to enable RTTI-based vftable detection
                    List<UndecoratedFunction> methodsFromThisVftable = VftableParser.AnalyzeVftable(
                        _tricksterWrapper.GetProcessHandle(),
                        module,
                        moduleExports,
                        type,
                        xoredVftableAddress,
                        typesManager: this);
                    
                    if (methodsFromThisVftable.Count > 0)
                    {
                        for (int j = 0; j < methodsFromThisVftable.Count; j++)
                        {
                            var method = methodsFromThisVftable[j];
                        }
                    }
                    
                    virtualFuncs.AddRange(methodsFromThisVftable);
                }
                catch (Exception ex)
                {
                    Logger.Debug($"[MsvcTypesManager][CreateType] EXCEPTION while parsing vftable 0x{xoredVftableAddress:x}");
                    Logger.Debug($"[MsvcTypesManager][CreateType] Exception type: {ex.GetType().Name}");
                    Logger.Debug($"[MsvcTypesManager][CreateType] Exception message: {ex.Message}");
                    Logger.Debug($"[MsvcTypesManager][CreateType] Exception stack trace: {ex.StackTrace}");
                    if (ex.InnerException != null)
                    {
                        Logger.Debug($"[MsvcTypesManager][CreateType] Inner exception: {ex.InnerException.GetType().Name}");
                        Logger.Debug($"[MsvcTypesManager][CreateType] Inner exception message: {ex.InnerException.Message}");
                    }
                    Logger.Debug($"[MsvcTypesManager][CreateType] Continuing to next vftable...");
                }
            }

            // Remove duplicates - the methods which are both virtual and exported
            int beforeDistinct = virtualFuncs.Count;
            virtualFuncs = virtualFuncs.Distinct().ToList();
            
            int beforeExportedFilter = virtualFuncs.Count;
            virtualFuncs = virtualFuncs.Where(method => !exportedFuncs.Contains(method)).ToList();
            
            IEnumerable<UndecoratedFunction> allFuncs = exportedFuncs.Concat(virtualFuncs);
            MsvcMethod[] msvcMethods = allFuncs.Select(func => new MsvcMethod(finalType, func)).ToArray();
            
            finalType.SetMethods(msvcMethods);
            return finalType;
        }


        private MsvcModuleExports GetOrCreateModuleExports(ModuleInfo module)
        {            
            if (!_exportsCache.TryGetValue(module, out MsvcModuleExports exports))
            {
                IReadOnlyList<UndecoratedSymbol> rawExports = _exportsMaster.GetUndecoratedExports(module);
                exports = new MsvcModuleExports(rawExports);
                _exportsCache[module] = exports;
            }
            
            return exports;
        }

        //TODO: Move me
        public Dictionary<FirstClassTypeInfo, IReadOnlyCollection<ulong>> Scan(IEnumerable<MsvcTypeStub> types)
        {
            IEnumerable<FirstClassTypeInfo> allClassesToScanFor = types.Select(t => t.TypeInfo).OfType<FirstClassTypeInfo>();
            var rawMatches = _memoryScanner.Scan(allClassesToScanFor);

            // Filtering out the matches which are just exports (not instances)
            return rawMatches.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyCollection<ulong>)kvp.Value.Where(IsNotExport).ToList());

            bool IsNotExport(ulong addr)
            {
                bool res = (_exportsMaster as ExportsMaster)?.QueryExportByAddress((nuint)addr) == null;
                if (res)
                    1.ToString();
                return res;
            }
        }

        /// <summary>
        /// Registers a custom function on a type
        /// </summary>
        public bool RegisterCustomFunction(
            string parentTypeFullName,
            string parentAssembly,
            string functionName,
            string moduleName,
            ulong offset,
            string returnTypeFullName,
            string[] argTypeFullNames)
        {
            try
            {
                // Get the parent type
                Predicate<string> moduleFilter = Filter.CreatePredicate(parentAssembly);
                Predicate<string> typeFilter = Filter.CreatePredicate(parentTypeFullName);
                MsvcTypeStub typeStub = GetType(moduleFilter, typeFilter);
                if (typeStub == null)
                    return false;

                MsvcType parentType = typeStub.Upgrade();
                if (parentType == null)
                    return false;

                // Find the module base address
                List<UndecoratedModule> modules = GetUndecoratedModules(Filter.CreatePredicate(moduleName));
                if (modules.Count == 0)
                {
                    modules = GetUndecoratedModules(Filter.CreatePredicate(moduleName + ".dll"));
                    if (modules.Count == 0)
                        return false;
                }

                // Since we've verified the list is not empty, we can safely access the first element
                UndecoratedModule targetModule = modules[0];

                // Create a custom undecorated function
                CustomUndecoratedFunction customFunc = new CustomUndecoratedFunction(
                    targetModule.ModuleInfo,
                    offset,
                    functionName,
                    returnTypeFullName,
                    argTypeFullNames);

                // Create an MsvcMethod from the custom function and add it to the type
                MsvcMethod customMethod = new MsvcMethod(parentType, customFunc);
                parentType.AddCustomMethod(customMethod);

                return true;
            }
            catch (Exception ex)
            {
                // Log the exception for debugging purposes
                Logger.Debug($"[MsvcTypesManager][RegisterCustomFunction] Failed to register custom function. Exception: {ex}");
                return false;
            }
        }
    }

    public class MsvcModuleExports
    {
        Dictionary<nuint, UndecoratedExportedField> _exportedFields = new Dictionary<nuint, UndecoratedExportedField>();
        Dictionary<nuint, UndecoratedFunction> _exportedFunctions = new();

        public MsvcModuleExports(IReadOnlyList<UndecoratedSymbol> exportsList)
        {
            _exportedFields = new Dictionary<nuint, UndecoratedExportedField>();
            _exportedFunctions = new();

            foreach (UndecoratedSymbol exports in exportsList)
            {
                if (exports is UndecoratedExportedField exportedField)
                {
                    _exportedFields[exportedField.XoredAddress /* ! */] = exportedField;
                }
                else if (exports is UndecoratedFunction exportedFunction)
                {
                    _exportedFunctions[exportedFunction.Address /* ! */] = exportedFunction;
                }
            }
        }

        public bool TryGetVftable(nuint addr, out UndecoratedExportedField undecoratedExport)
        {   
            nuint xoredAddr = addr ^ UndecoratedExportedField.XorMask;            
            if (!_exportedFields.TryGetValue(xoredAddr, out undecoratedExport))
            {
                return false;
            }
            bool containsVftable = undecoratedExport.UndecoratedName.Contains("`vftable'");
            return containsVftable;
        }

        public bool TryGetFunc(nuint address, out UndecoratedFunction undecFunc)
        {
            return _exportedFunctions.TryGetValue(address, out undecFunc);
        }
    }
}

