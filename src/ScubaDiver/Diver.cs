using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Runtime;
using Newtonsoft.Json;
using ScubaDiver.API;
using ScubaDiver.Extensions;
using ScubaDiver.Utils;

namespace ScubaDiver
{
    public class Diver : IDisposable
    {
        // Runtime analysis and exploration fields
        private DataTarget _dt = null;
        private ClrRuntime _runtime = null;
        private Converter<object> _converter = new();

        // HTTP Responses fields
        private Dictionary<string, Func<HttpListenerRequest, string>> _responseBodyCreators;

        // Pinning objects fields
        private Dictionary<ulong, object> _pinnedObjects;

        // Singleton
        private static Diver _instance;

        public Diver()
        {
            _responseBodyCreators = new Dictionary<string, Func<HttpListenerRequest, string>>()
            {
                {"/die", MakeDieResponse},
                {"/domains", MakeDomainsResponse},
                {"/heap", MakeHeapResponse},
                {"/invoke", MakeInvokeResponse},
                {"/object", MakeObjectResponse},
                {"/unpin", MakeUnpinResponse},
                {"/types", MakeTypesResponse},
                {"/type", MakeTypeResponse},
            };
            _pinnedObjects = new();
        }

        private string MakeUnpinResponse(HttpListenerRequest arg)
        {
            string objAddrStr = arg.QueryString.Get("address");
            if (objAddrStr == null || !ulong.TryParse(objAddrStr, out var objAddr))
            {
                return "{\"error\":\"Missing parameter 'address'\"}";
            }

            // Check if we have this objects in our pinned pool
            if (_pinnedObjects.ContainsKey(objAddr))
            {
                // Found pinned object!
                UnpinObject(objAddr);
                return "{\"status\":\"OK\"}";
            }
            else
            {
                // Object not pinned, try get it the hard way
                return "{\"error\":\"Object at given address wasn't pinned\"}";
            }
        }

        private string MakeInvokeResponse(HttpListenerRequest arg)
        {
            Console.WriteLine("[Diver] Got /Invoke request!");
            string body = null;
            using (StreamReader sr = new(arg.InputStream))
            {
                body = sr.ReadToEnd();
            }

            if (string.IsNullOrEmpty(body))
            {
                return "{\"error\":\"Missing body\"}";
            }

            TextReader textReader = new StringReader(body);
            JsonReader jr = new JsonTextReader(textReader);
            JsonSerializer js = new JsonSerializer();
            var request = js.Deserialize<InvocationRequest>(jr);
            if (request == null)
            {
                return "{\"error\":\"Failed to deserialize body\"}";
            }

            ClrObject clrObj = _runtime.Heap.GetObject(request.ObjAddress);
            if (clrObj.Type == null)
            {
                return "{\"error\":\"'address' points at an invalid address\"}";
            }

            List<object> paramsList = new();
            if (request.Parameters.Any())
            {
                Console.WriteLine($"[Diver] Invoking with parameters. Count: {request.Parameters.Count}");
                foreach (var param in request.Parameters)
                {
                    Type paramType = ResolveType(param.Type);
                    if (paramType == typeof(string))
                    {
                        // String are not encoded - they are themselves.
                        paramsList.Add(param.EncodedValue);
                        continue;
                    }
                    if (paramType.IsPrimitive)
                    {
                        // Call 'Parse' static method of the relevant type
                        var parserMethod = paramType.GetMethodRecursive("Parse", new[] { typeof(string) });
                        object parsedParam = parserMethod.Invoke(null, new object[1] { param.EncodedValue });
                        paramsList.Add(parsedParam);
                        continue;
                    }
                    else
                    {
                        throw new NotImplementedException(
                            $"Don't know how to parse this parameter into an object of type `{paramType.FullName}`");
                    }
                }
            }
            else
            {
                // No parameters.
                Console.WriteLine("[Diver] Invoking without parameters");
            }

            Type realType = clrObj.Type.GetRealType();
            Console.WriteLine($"[Diver] Resolved target object type: {realType.FullName}");
            var method = realType.GetMethodRecursive(request.MethodName, paramsList.Select(p => p.GetType()).ToArray());
            if (method == null)
            {
                Console.WriteLine($"[Diver] Failed to Resolved method :/");
                return "{\"error\":\"Couldn't find method in type.\"}";
            }
            Console.WriteLine($"[Diver] Resolved method: {method.Name}, Containing Type: {method.DeclaringType}");

            Console.WriteLine($"[Diver] Trying to get object at address {clrObj.Address}");
            object instance;
            if (!_pinnedObjects.TryGetValue(request.ObjAddress, out instance))
            {
                Console.WriteLine($"[Diver] Failed to get pinned object :/");
                return "{\"error\":\"Couldn't find pinned object with given address.\"}";
            }
            Console.WriteLine($"[Diver] Results of object retrieval: {instance}");

            object results = null;
            try
            {
                results = method.Invoke(instance, paramsList.ToArray());
            }
            catch (Exception e)
            {
                return $"{{\"error\":\"Invocation caused exception: {e}\"}}";
            }

            InvocationResults invocResults;
            if (method.ReturnType == typeof(void))
            {
                // Not expecting results.
                invocResults = new() { VoidReturnType = true };
                return $"{{results:\"Done.\"}}";
            }
            else
            {
                // Need to return the results. If it's primitive we'll encode it
                // If it's non-primitive we pin it and send the address.
                ObjectOrRemoteAddress returnValue;
                if (results.GetType().IsPrimitiveEtc())
                {
                    returnValue = ObjectOrRemoteAddress.FromObj(results);
                }
                else
                {
                    // Pinning results
                    ulong resultsAddress = PinObject(results);
                    Type resultsType = results.GetType();
                    returnValue = ObjectOrRemoteAddress.FromToken(resultsAddress, resultsType.Name);
                }


                invocResults = new()
                {
                    VoidReturnType = false,
                    ReturnedObjectOrAddress = returnValue
                };
            }
            return JsonConvert.SerializeObject(invocResults);
        }



        private string MakeObjectResponse(HttpListenerRequest arg)
        {
            string objAddrStr = arg.QueryString.Get("address");
            bool pinningRequested = arg.QueryString.Get("pinRequest") == "True";
            Console.WriteLine($"[Diver][Debug](MakeObjectResponse) objAddrStr=\"{objAddrStr}\", pinningRequested={pinningRequested}");
            if (objAddrStr == null)
            {
                return "{\"error\":\"Missing parameter 'address'\"}";
            }
            if (!ulong.TryParse(objAddrStr, out var objAddr))
            {
                return "{\"error\":\"Parameter 'address' could not be parsed as ulong\"}";
            }

            // Check if we have this objects in our pinned pool
            object instance = null;
            bool alreadyPinned = false;
            Type dumpedObjType;
            if (_pinnedObjects.TryGetValue(objAddr, out instance))
            {
                // Found pinned object!
                dumpedObjType = instance.GetType();
                alreadyPinned = true;
            }
            else
            {
                // Object not pinned, try get it the hard way
                ClrObject clrObj = _runtime.Heap.GetObject(objAddr);
                if (clrObj.Type == null)
                {
                    return "{\"error\":\"'address' points at an invalid address\"}";
                }

                // Make sure it's still in place
                RefreshRuntime();
                clrObj = _runtime.Heap.GetObject(objAddr);
                if (clrObj.Type == null)
                {
                    return
                        "{\"error\":\"Object moved since last refresh. 'address' now points at an invalid address.\"}";
                }

                dumpedObjType = clrObj.Type.GetRealType();

                // Using a cool trick to get reference for the object
                instance = _converter.ConvertFromIntPtr(clrObj.Address);
            }

            if (pinningRequested & !alreadyPinned)
            {
                PinObject(instance, objAddr);
            }

            if (dumpedObjType.IsPrimitiveEtc() || instance is IEnumerable)
            {
                var od = new ObjectDump()
                {
                    Address = objAddr,
                    PrimitiveValue = PrimitivesEncoder.Encode(instance)
                };
                return JsonConvert.SerializeObject(od);
            }
            else
            {
                List<MemberDump> fields = new();
                foreach (var fieldInfo in dumpedObjType.GetFields((BindingFlags)0xffff))
                {
                    try
                    {
                        var fieldValue = fieldInfo.GetValue(instance);
                        bool hasEncValue = false;
                        string encValue = null;
                        if (fieldValue.GetType().IsPrimitiveEtc() || fieldValue is IEnumerable)
                        {
                            hasEncValue = true;
                            encValue = PrimitivesEncoder.Encode(fieldValue);
                        }
                        fields.Add(new()
                        {
                            Name = fieldInfo.Name,
                            HasEncodedValue = hasEncValue,
                            EncodedValue = encValue
                        });
                    }
                    catch (Exception e)
                    {
                        fields.Add(new()
                        {
                            Name = fieldInfo.Name,
                            HasEncodedValue = false,
                            RetrivalError = $"Failed to read. Exception: {e}"
                        });
                    }
                }

                List<MemberDump> props = new();
                foreach (var propInfo in dumpedObjType.GetProperties((BindingFlags)0xffff))
                {
                    if (propInfo.GetMethod == null)
                    {
                        // No getter, skipping
                        continue;
                    }

                    try
                    {
                        var propValue = propInfo.GetValue(instance);
                        bool hasEncValue = false;
                        string encValue = null;
                        if (propValue.GetType().IsPrimitiveEtc() || propValue is IEnumerable)
                        {
                            hasEncValue = true;
                            encValue = PrimitivesEncoder.Encode(propValue);
                        }
                        props.Add(new()
                        {
                            Name = propInfo.Name,
                            HasEncodedValue = hasEncValue,
                            EncodedValue = encValue
                        });
                    }
                    catch (Exception e)
                    {
                        props.Add(new()
                        {
                            Name = propInfo.Name,
                            HasEncodedValue = false,
                            RetrivalError = $"Failed to read. Exception: {e}"
                        });
                    }
                }

                var od = new ObjectDump()
                {
                    Address = objAddr,
                    Type = dumpedObjType.ToString(),
                    Fields = fields,
                    Properties = props
                };
                return JsonConvert.SerializeObject(od);
            }
        }

        private bool UnpinObject(ulong objAddress)
        {
            bool removed = _pinnedObjects.Remove(objAddress);
            return removed;
        }
        private ulong PinObject(object instance, ulong? objAddress = null)
        {
            // Need to pin the object (and it's not already pinned)
            GCHandle handle = GCHandle.Alloc(instance);
            objAddress ??= (ulong)GCHandle.ToIntPtr(handle);
            _pinnedObjects[objAddress.Value] = instance;
            return objAddress.Value;
        }

        void RefreshRuntime()
        {
            _runtime?.Dispose();
            _runtime = null;
            _dt?.Dispose();
            _dt = null;

            _dt = DataTarget.CreateSnapshotAndAttach(Process.GetCurrentProcess().Id);
            _runtime = _dt.ClrVersions.Single().CreateRuntime();
        }
        public void Dive()
        {
            // Start session
            RefreshRuntime();
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:9977/");
            listener.Start();
            Console.WriteLine("[Diver] Listening...");

            Dispatcher(listener);

            listener.Close();
            Console.WriteLine("[Diver] Dispatcher returned, Dive is complete.");
        }

        private void Dispatcher(HttpListener listener)
        {
            while (true)
            {
                var requestContext = listener.GetContext();
                HttpListenerRequest request = requestContext.Request;

                var response = requestContext.Response;
                string body;
                if (_responseBodyCreators.TryGetValue(request.Url.AbsolutePath, out var respBodyGenerator))
                {
                    body = respBodyGenerator(request);
                }
                else
                {
                    body = "{\"error\":\"Unknown Command\"}";
                }

                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(body);
                // Get a response stream and write the response to it.
                response.ContentLength64 = buffer.Length;
                response.ContentType = "application/json";
                System.IO.Stream output = response.OutputStream;
                output.Write(buffer, 0, buffer.Length);
                // You must close the output stream.
                output.Close();

                if (request.RawUrl == "/die")
                    break;
            }

            Console.WriteLine("[Diver] HTTP Loop ended. Closing HTTP listener");
        }

        private string MakeDieResponse(HttpListenerRequest req)
        {
            Console.WriteLine("[Diver] Die command received");
            return "{\"error\":\"Goodbye\"}";
        }

        private string MakeTypesResponse(HttpListenerRequest req)
        {
            string assembly = req.QueryString.Get("assembly");

            // Try exact match assembly 
            var allAssembliesInApp = _runtime.AppDomains.SelectMany(appDom => appDom.Modules);
            List<ClrModule> matchingAssemblies = allAssembliesInApp.Where(module => Path.GetFileNameWithoutExtension(module.Name) == assembly).ToList();
            if (matchingAssemblies.Count == 0)
            {
                // No exact matches, widen search to any assembly *containing* the query
                matchingAssemblies = allAssembliesInApp.Where(module => Path.GetFileNameWithoutExtension(module.Name).Contains(assembly)).ToList();
            }

            if (!matchingAssemblies.Any())
            {
                // No matching assemblies found
                return "{\"error\":\"No assemblies found matching the query\"}";
            }
            else if (matchingAssemblies.Count > 1)
            {
                return $"{{\"error\":\"Too many assemblies found matching the query. Expected: 1, Got: {matchingAssemblies.Count}\"}}";
            }

            // Got here - we have a single matching assembly.
            ClrModule matchingAssembly = matchingAssemblies.Single();

            var typeNames = from tuple in matchingAssembly.OldSchoolEnumerateTypeDefToMethodTableMap()
                            let token = tuple.Token
                            let typeName = matchingAssembly.ResolveToken(token)?.Name ?? "Unknown"
                            select new TypesDump.TypeIdentifiers()
                            { MethodTable = tuple.MethodTable, Token = token, TypeName = typeName };


            TypesDump dump = new()
            {
                AssemblyName = assembly,
                Types = typeNames.ToList()
            };

            return JsonConvert.SerializeObject(dump);
        }

        public string MakeHeapResponse(HttpListenerRequest httpReq)
        {
            string filter = httpReq.QueryString.Get("type_filter");

            // Default filter - no filter. Just return everything.
            Predicate<string> matchesFilter = (typeName) => true;
            if (filter != null)
            {
                string noStartsFilter = filter.Trim('*');
                // User specified a filter. Looking for wild cards
                if (filter.StartsWith("*"))
                {
                    if (filter.EndsWith("*"))
                    {
                        // Filter of format "*phrase*", looking anywhere inside the type name
                        matchesFilter = (typeName) => typeName.Contains(noStartsFilter);
                    }
                    else
                    {
                        // Filter of format "*phrase", looking for specific suffix
                        matchesFilter = (typeName) => typeName.EndsWith(noStartsFilter);
                    }
                }
                else
                {
                    if (filter.EndsWith("*"))
                    {
                        // Filter of format "phrase*", looking for specific prefix
                        matchesFilter = (typeName) => typeName.StartsWith(noStartsFilter);
                    }
                    else
                    {
                        // Filter has no wildcards - looking for specific type
                        matchesFilter = (typeName) => typeName == filter;
                    }
                }
            }

            List<HeapDump.HeapObject> objects = new();
            foreach (ClrObject obj in _runtime.Heap.EnumerateObjects())
            {
                string objType = obj.Type?.Name ?? "Unknown";
                if (matchesFilter(objType))
                {
                    objects.Add(new()
                    {
                        Address = obj.Address,
                        Type = objType
                    });
                }
            }

            HeapDump hd = new() { Objects = objects };

            var resJson = JsonConvert.SerializeObject(hd);
            return resJson;
        }

        private string MakeDomainsResponse(HttpListenerRequest req)
        {
            // TODO: Allow moving between domains?
            List<DomainsDump.AvailableDomain> available = new();
            foreach (ClrAppDomain clrAppDomain in _runtime.AppDomains)
            {
                available.Add(new()
                {
                    Name = clrAppDomain.Name,
                    AvailableModules = clrAppDomain.Modules.Select(m => Path.GetFileNameWithoutExtension(m.Name)).ToList()
                });
            }

            DomainsDump dd = new()
            {
                Current = AppDomain.CurrentDomain.FriendlyName,
                AvailableDomains = available
            };

            return JsonConvert.SerializeObject(dd);
        }

        private Type ResolveType(string name, string assembly = null)
        {
            Console.WriteLine($"[Diver] Trying to resolve type. [Assembly:{assembly}, Type:{name}]");
            IList<ClrModule> assembliesToSearch = _runtime.AppDomains.First().Modules;
            if (assembly != null)
                assembliesToSearch = assembliesToSearch.Where(mod => Path.GetFileNameWithoutExtension(mod.Name) == assembly).ToList();
            if (!assembliesToSearch.Any())
            {
                // No such assembly
                Console.WriteLine($"[Diver] No such assembly \"{assembly}\"");
                return null;
            }

            foreach (ClrModule module in assembliesToSearch)
            {
                ClrType clrTypeInfo = module.GetTypeByName(name);
                if (clrTypeInfo == null)
                {
                    Console.WriteLine(
                        $"Mod Candidate for type resolution! {Path.GetFileNameWithoutExtension(module.Name)}");
                    var x = module.OldSchoolEnumerateTypeDefToMethodTableMap();
                    var typeNames = (from tuple in x
                        let token = tuple.Token
                        let resolvedType = module.ResolveToken(token) ?? null
                        where resolvedType?.Name == name
                        select new {MethodTable = tuple.MethodTable, Token = token, ClrType = resolvedType}).ToList();
                    if (typeNames.Any())
                    {
                        clrTypeInfo = typeNames.First().ClrType;
                    }
                }

                if (clrTypeInfo == null)
                {
                    continue;
                }

                // Found it
                Type typeObj = clrTypeInfo.GetRealType();
                return typeObj;
            }


            return null;
        }

        public string MakeTypeResponse(HttpListenerRequest req)
        {
            string type = req.QueryString.Get("name");
            if (type == null)
            {
                return "{\"error\":\"Missing parameter 'name'\"}";
            }

            string assembly = req.QueryString.Get("assembly");
            Console.WriteLine($"[Diver] Type query for: [Assembly:{assembly}, Type:{type}]");
            Type resolvedType = ResolveType(type, assembly);

            TypeDump ParseType(Type typeObj)
            {
                if (typeObj == null) return null;

                var methods = typeObj.GetMethods((BindingFlags)0xffff).Select(mi => new TypeDump.TypeMethod(mi))
                    .ToList();
                var fields = typeObj.GetFields((BindingFlags)0xffff).Select(fi => new TypeDump.TypeField(fi))
                    .ToList();
                var props = typeObj.GetProperties((BindingFlags)0xffff).Select(pi => new TypeDump.TypeProperty(pi))
                    .ToList();

                TypeDump td = new()
                {
                    Type = typeObj.FullName,
                    Assembly = typeObj.Assembly.GetName().Name,
                    Methods = methods,
                    Fields = fields,
                    Properties = props
                };
                if (typeObj != typeof(object))
                {
                    // Has parent. Parse it as well
                    td.ParentDump = ParseType(typeObj.BaseType);
                }

                return td;
            }

            if (resolvedType != null)
            {
                TypeDump recusiveTypeDump = ParseType(resolvedType);
                return JsonConvert.SerializeObject(recusiveTypeDump);
            }

            return "{\"error\":\"Failed to find type in searched assemblies\"}";
        }

        public static int EntryPoint(string pwzArgument)
        {
            // Bootstrap needs to call a C# function with exactly this signature.
            // So we use it to just create a diver, and run the Dive func (blocking)

            // Diver needs some assemblies which might not be loaded in the target process
            // so starting off with registering an assembly resolver to the Diver's dll's directory
            string folderPath = System.IO.Path.GetDirectoryName(typeof(Diver).Assembly.Location);
            AppDomain.CurrentDomain.AssemblyResolve += (object sender, ResolveEventArgs args) =>
            {
                string assemblyPath = Path.Combine(folderPath, new AssemblyName(args.Name).Name + ".dll");
                if (!File.Exists(assemblyPath)) return null;
                Assembly assembly = Assembly.LoadFrom(assemblyPath);
                return assembly;
            };
            Console.WriteLine("[Diver] Loaded + hooked assemblies resolver.");

            try
            {
                _instance = new Diver();
                _instance.Dive();

                // Diver killed
                Console.WriteLine("[Diver] Diver finished gracefully, Entry point returning");
                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Console.WriteLine("[Diver] Exiting entry point in 60 secs...");
                Thread.Sleep(TimeSpan.FromSeconds(60));
                return 1;
            }
        }

        public void Dispose()
        {
            _runtime?.Dispose();
            _dt?.Dispose();
        }
    }
}