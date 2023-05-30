using ScubaDiver.API.Interactions.Dumps;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using ScubaDiver.API.Utils;
using ModuleInfo = Microsoft.Diagnostics.Runtime.ModuleInfo;
using TypeInfo = System.Reflection.TypeInfo;
using System.Net.Sockets;
using System.Reflection;
using NtApiDotNet.Win32;
using System.IO;
using NtApiDotNet;
using System.Drawing;
using ScubaDiver.Rtti;
using Newtonsoft.Json.Linq;

namespace ScubaDiver
{
    public class MsvcDiver : DiverBase
    {
        private Dictionary<ModuleInfo, TypeInfo[]> scannedModules = new Dictionary<ModuleInfo, TypeInfo[]>();

        public MsvcDiver(IRequestsListener listener) : base(listener)
        {
        }

        public override void Start()
        {
            Logger.Debug("[MsvcDiver] Is logging debugs in release? " + Logger.DebugInRelease.Value);

            // Load or Hijack Newtonsoft.Json
            var nsJson = InitNewtonsoftJson();
            Logger.Debug("[MsvcDiver] Newtonsoft.Json's module path: " + nsJson.Location);

            base.Start();
        }

        protected override string MakeInjectDllResponse(ScubaDiverMessage req)
        {
            string dllPath = req.QueryString.Get("dll_path");
            try
            {
                var handle = Windows.Win32.Kernel32.LoadLibrary(dllPath);

                if(handle.IsInvalid)
                    return "{\"status\":\"dll load failed\"}";

                // Must take a new snapshot to see our new module
                RefreshRuntime();
                return "{\"status\":\"dll loaded\"}";
            }
            catch (Exception ex)
            {
                return QuickError(ex.Message, ex.StackTrace);
            }
        }


        protected override string MakeDomainsResponse(ScubaDiverMessage req)
        {
            RefreshRuntime();

            List<DomainsDump.AvailableDomain> available = new();
            var modules = _trickster.ModulesParsed
                .Select(m => m.Name)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .ToList();
            var dom = new DomainsDump.AvailableDomain()
            {
                Name = "dummy_domain",
                AvailableModules = modules
            };
            available.Add(dom);

            DomainsDump dd = new()
            {
                Current = "dummy_domain",
                AvailableDomains = available
            };

            return JsonConvert.SerializeObject(dd);
        }

        private Trickster _trickster = null;

        private void RefreshRuntime()
        {
            _trickster ??= new Trickster(Process.GetCurrentProcess());
            _trickster.ScanTypes();
        }

        protected override string MakeTypesResponse(ScubaDiverMessage req)
        {
            if (_trickster == null || !_trickster.ScannedTypes.Any())
            {
                RefreshRuntime();
            }

            string assembly = req.QueryString.Get("assembly");
            List<ScubaDiver.Rtti.ModuleInfo> matchingAssemblies = _trickster.ScannedTypes.Keys.Where(assm => assm.Name == assembly).ToList();
            if (matchingAssemblies.Count == 0)
            {
                // No exact matches, widen search to any assembly *containing* the query
                matchingAssemblies = _trickster.ScannedTypes.Keys.Where(module =>
                {
                    try
                    {
                        return module.Name?.Contains(assembly) == true;
                    }
                    catch { }

                    return false;
                }).ToList();
            }


            var assm = matchingAssemblies.Single();
            List<TypesDump.TypeIdentifiers> types = new List<TypesDump.TypeIdentifiers>();
            foreach (var type in _trickster.ScannedTypes[assm])
            {
                types.Add(new TypesDump.TypeIdentifiers()
                {
                    TypeName = $"{assm.Name}!{type.Name}"
                });
            }

            TypesDump dump = new()
            {
                AssemblyName = assembly,
                Types = types
            };

            return JsonConvert.SerializeObject(dump);
        }

        protected override string MakeTypeResponse(ScubaDiverMessage req)
        {
            string body = req.Body;

            if (string.IsNullOrEmpty(body))
            {
                return QuickError("Missing body");
            }

            TextReader textReader = new StringReader(body);
            var request = JsonConvert.DeserializeObject<TypeDumpRequest>(body);
            if (request == null)
            {
                return QuickError("Failed to deserialize body");
            }

            return MakeTypeResponse(request);
        }

        public string MakeTypeResponse(TypeDumpRequest dumpRequest)
        {
            string rawTypeFilter = dumpRequest.TypeFullName;
            if (string.IsNullOrEmpty(rawTypeFilter))
            {
                return QuickError("Missing parameter 'TypeFullName'");
            }

            ParseFullTypeName(rawTypeFilter, out var rawAssemblyFilter, out rawTypeFilter);
            string assembly = dumpRequest.Assembly;
            if (!string.IsNullOrEmpty(assembly))
            {
                rawAssemblyFilter = assembly;
            }

            ManagedTypeDump dump = GetManagedTypeDump(rawAssemblyFilter, rawTypeFilter);
            if (dump != null)
                return JsonConvert.SerializeObject(dump);

            return QuickError("Failed to find type in searched assemblies");
        }

        private ManagedTypeDump GetManagedTypeDump(string rawAssemblyFilter, string rawTypeFilter)
        {
            IEnumerable<Rtti.TypeInfo> typeInfos = GetTypeInfos(rawAssemblyFilter, rawTypeFilter);
            foreach (ScubaDiver.Rtti.TypeInfo typeInfo in typeInfos)
            {
                string moduleName = typeInfo.ModuleName;
                string className = typeInfo.Name.Substring(typeInfo.Name.LastIndexOf("::") + 2);
                string membersPrefix = $"{typeInfo.Name}::";
                string ctorName = $"{typeInfo.Name}::{className}"; // Constructing NameSpace::ClassName::ClassName

                List<DllExport> exports = GetExports(moduleName);

                List<ManagedTypeDump.TypeMethod> methods = new();
                List<ManagedTypeDump.TypeMethod> constructors = new();
                foreach (DllExport dllExport in exports)
                {
                    string undecorated = dllExport.UndecorateName();
                    if (!undecorated.StartsWith(membersPrefix))
                        continue;

                    ManagedTypeDump.TypeMethod method = new()
                    {
                        Name = dllExport.Name,
                        Visibility = "Public" // Because it's exported
                    };

                    if (undecorated == ctorName)
                        constructors.Add(method);
                    else
                        methods.Add(method);
                }

                ManagedTypeDump recusiveManagedTypeDump = new ManagedTypeDump()
                {
                    Assembly = moduleName,
                    Type = typeInfo.Name,
                    Methods = methods,
                    Constructors = constructors
                };
                return recusiveManagedTypeDump;
            }
            return null;
        }

        private IEnumerable<ScubaDiver.Rtti.TypeInfo> GetTypeInfos(string rawAssemblyFilter, string rawTypeFilter)
        {
            Predicate<string> assmFilter = Filter.CreatePredicate(rawAssemblyFilter);
            Predicate<string> typeFilter = Filter.CreatePredicate(rawTypeFilter);

            foreach (var kvp in _trickster.ScannedTypes)
            {
                var module = kvp.Key;
                if (!assmFilter(module.Name))
                    continue;

                ScubaDiver.Rtti.TypeInfo[] typeInfos = kvp.Value;
                foreach (ScubaDiver.Rtti.TypeInfo typeInfo in typeInfos)
                {
                    if (!typeFilter(typeInfo.Name))
                        continue;
                    yield return typeInfo;
                }
            }
        }

        private Dictionary<string, List<DllExport>> _cache = new Dictionary<string, List<DllExport>>();
        private List<DllExport> GetExports(string moduleName)
        {
            if (!_cache.ContainsKey(moduleName))
            {
                var lib = SafeLoadLibraryHandle.GetModuleHandle(moduleName);
                _cache[moduleName] = lib.Exports.ToList();
            }
            return _cache[moduleName];
        }


        protected override string MakeHeapResponse(ScubaDiverMessage arg)
        {
            if (_trickster == null || !_trickster.ScannedTypes.Any())
            {
                RefreshRuntime();
            }

            if (_trickster.Regions == null || !_trickster.Regions.Any())
            {
                Console.WriteLine("[MsvcDiver] Calling Read Regions in trickster.");
                _trickster.ReadRegions();
                Console.WriteLine("[MsvcDiver] Calling Read Regions in trickster -- done!");
            }

            string rawFilter = arg.QueryString.Get("type_filter");
            ParseFullTypeName(rawFilter, out var rawAssemblyFilter, out var rawTypeFilter);

            Predicate<string> assmFilter = Filter.CreatePredicate(rawAssemblyFilter);
            Predicate<string> typeFilter = Filter.CreatePredicate(rawTypeFilter);

            HeapDump hd = new HeapDump()
            {
                Objects = new List<HeapDump.HeapObject>()
            };
            foreach (var moduleTypesKvp in _trickster.ScannedTypes)
            {
                var module = moduleTypesKvp.Key;
                if (!assmFilter(module.Name))
                    continue;

                IEnumerable<Rtti.TypeInfo> typeInfos = moduleTypesKvp.Value;
                if (!string.IsNullOrEmpty(rawTypeFilter) && rawTypeFilter != "*")
                    typeInfos = typeInfos.Where(ti => typeFilter(ti.Name));


                Logger.Debug($"[{DateTime.Now}] Starting Trickster Scan for {typeInfos.Count()} types");
                Dictionary<Rtti.TypeInfo, IReadOnlyCollection<ulong>> addresses = TricksterUI.Scan(_trickster, typeInfos);
                Logger.Debug($"[{DateTime.Now}] Trickster Scan finished with {addresses.SelectMany(kvp => kvp.Value).Count()} results");
                foreach (var typeInstancesKvp in addresses)
                {
                    Rtti.TypeInfo typeInfo = typeInstancesKvp.Key;
                    foreach (nuint addr in typeInstancesKvp.Value)
                    {
                        HeapDump.HeapObject ho = new HeapDump.HeapObject()
                        {
                            Address = addr,
                            MethodTable = typeInfo.Address,
                            Type = $"{module.Name}!{typeInfo.Name}"
                        };
                        hd.Objects.Add(ho);
                    }
                }
            }

            return JsonConvert.SerializeObject(hd);

        }

        private static void ParseFullTypeName(string rawFilter, out string rawAssemblyFilter, out string rawTypeFilter)
        {
            rawAssemblyFilter = "*";
            rawTypeFilter = rawFilter;
            if (rawFilter.Contains('!'))
            {
                var splitted = rawFilter.Split('!');
                rawAssemblyFilter = splitted[0];
                rawTypeFilter = splitted[1];
            }
        }

        protected override string MakeObjectResponse(ScubaDiverMessage arg)
        {
            string objAddrStr = arg.QueryString.Get("address");
            string typeName = arg.QueryString.Get("type_name");
            bool pinningRequested = arg.QueryString.Get("pinRequest").ToUpper() == "TRUE";
            bool hashCodeFallback = arg.QueryString.Get("hashcode_fallback").ToUpper() == "TRUE";
            string hashCodeStr = arg.QueryString.Get("hashcode");
            int userHashcode = 0;
            if (objAddrStr == null)
            {
                return QuickError("Missing parameter 'address'");
            }
            if (!ulong.TryParse(objAddrStr, out var objAddr))
            {
                return QuickError("Parameter 'address' could not be parsed as ulong");
            }
            if (hashCodeFallback)
            {
                if (!int.TryParse(hashCodeStr, out userHashcode))
                {
                    return QuickError("Parameter 'hashcode_fallback' was 'true' but the hashcode argument was missing or not an int");
                }
            }
            
            ParseFullTypeName(typeName, out var rawAssemblyFilter, out var rawTypeFilter);

            try
            {
                Rtti.TypeInfo typeInfo = GetTypeInfos(rawAssemblyFilter, rawTypeFilter).Single();

                // TODO: Actual Pin
                ulong pinAddr = 0x0;

                ObjectDump od = new ObjectDump()
                {
                    Type = $"{typeInfo.ModuleName}!{typeInfo.Name}",
                    RetrivalAddress = objAddr,
                    PinnedAddress = pinAddr,
                    HashCode = 0x0bad0bad
                };
                return JsonConvert.SerializeObject(od);
            }
            catch (Exception e)
            {
                return QuickError("Failed Getting the object for the user. Error: " + e.Message);
            }
        }

        protected override string MakeCreateObjectResponse(ScubaDiverMessage arg)
        {
            return QuickError("Not Implemented");
        }

        protected override string MakeInvokeResponse(ScubaDiverMessage arg)
        {
            return QuickError("Not Implemented");
        }

        protected override string MakeGetFieldResponse(ScubaDiverMessage arg)
        {
            return QuickError("Not Implemented");
        }

        protected override string MakeSetFieldResponse(ScubaDiverMessage arg)
        {
            return QuickError("Not Implemented");
        }

        protected override string MakeArrayItemResponse(ScubaDiverMessage arg)
        {
            return QuickError("Not Implemented");
        }

        protected override string MakeUnpinResponse(ScubaDiverMessage arg)
        {
            return QuickError("Not Implemented");
        }

        public override void Dispose()
        {
        }

    }

    public static class DllExportExt
    {
        public static string UndecorateName(this DllExport export)
        {
            return RttiScanner.UnDecorateSymbolNameWrapper(export.Name);
        }
    }
}
