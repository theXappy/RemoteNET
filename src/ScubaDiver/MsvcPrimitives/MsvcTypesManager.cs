﻿using NtApiDotNet.Win32;
using ScubaDiver.Rtti;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;

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
        public UndecoratedExportedField ExportedField { get; set; }
        UndecoratedSymbol ISymbolBackedMember.Symbol => ExportedField;

        public VftableInfo(MsvcType msvcType, UndecoratedExportedField symbol)
        {
            _type = msvcType;
            ExportedField = symbol;
        }

        public override string Name => ExportedField.UndecoratedName;
        public ulong Address => (ulong)ExportedField.Address;
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
        public override MethodAttributes Attributes => throw new NotImplementedException();
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

        public override MsvcModule Module => _module;

        public override string Name => TypeInfo.Name;
        public override string Namespace => TypeInfo.Namespace;
        public override string FullName => TypeInfo.FullTypeName;


        public override MsvcMethod[] GetMethods(BindingFlags bindingAttr) => _methods;
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
                _tricksterWrapper.Refresh();
        }

        private object _getTypesLock = new object();
        public IReadOnlyList<MsvcTypeStub> GetTypes(MsvcModuleFilter moduleFilter, Predicate<string> typeFilter)
        {
            List<MsvcTypeStub> output = new List<MsvcTypeStub>();
            lock (_getTypesLock)
            {
                RefreshIfNeeded();
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

            // Get all exported members of the requseted type
            List<UndecoratedSymbol> rawMembers = _exportsMaster.GetExportedTypeMembers(module.ModuleInfo, type.NamespaceAndName).ToList();
            List<UndecoratedFunction> exportedFuncs = rawMembers.OfType<UndecoratedFunction>().ToList();

            // Find the first vftable within all members
            // (TODO: Bug? How can I tell this is the "main" vftable?)
            UndecoratedExportedField[] vftables = rawMembers.OfType<UndecoratedExportedField>()
                                            .Where(member => member.UndecoratedName.EndsWith("`vftable'"))
                                            .ToArray();
            VftableInfo[] vftableInfos = vftables.Select(vftable => new VftableInfo(finalType, vftable)).ToArray();
            finalType.SetVftables(vftableInfos);

            // Find all virtual methods (from vftable)
            List<UndecoratedFunction> virtualFuncs = new List<UndecoratedFunction>();
            foreach (UndecoratedExportedField vftable in vftables)
            {
                MsvcModuleExports moduleExports = GetOrCreateModuleExports(module.ModuleInfo);
                virtualFuncs = VftableParser.AnalyzeVftable(_tricksterWrapper.GetProcessHandle(),
                    module,
                    moduleExports,
                    type,
                    vftable.Address);

                // Remove duplicates - the methods which are both virtual and exported.
                virtualFuncs = virtualFuncs.Where(method => !exportedFuncs.Contains(method)).ToList();
            }

            // Finalize methods
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
            return _memoryScanner.Scan(allClassesToScanFor);
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
                return false;
            return undecoratedExport.UndecoratedName.Contains("`vftable'");
        }

        public bool TryGetFunc(nuint address, out UndecoratedFunction undecFunc)
        {
            return _exportedFunctions.TryGetValue(address, out undecFunc);
        }
    }
}

