using ScubaDiver.API.Interactions.Dumps;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using ScubaDiver.API.Utils;
using TheLeftExit.Trickster.Memory;
using ModuleInfo = Microsoft.Diagnostics.Runtime.ModuleInfo;
using TypeInfo = System.Reflection.TypeInfo;
using System.Net.Sockets;

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

            int lol = (new Random()).Next();
            if (lol == 0)
            {
                Console.WriteLine("Ports Test!");
                for (int port = 0; port <= 65535; port++)
                {
                    try
                    {
                        Console.WriteLine($"trying port {port} at 0.0.0.0");
                        IPAddress addr = IPAddress.Any;
                        var listener2 = new TcpListener(addr, port);
                        listener2.Start();
                        Console.WriteLine($"Successfully opened port {port} at 0.0.0.0");
                        listener2.Stop();

                        Console.WriteLine($"trying port {port} at 127.0.0.1");
                        addr = IPAddress.Parse("127.0.0.1");
                        listener2 = new TcpListener(addr, port);
                        listener2.Start();
                        Console.WriteLine($"Successfully opened port {port} at 127.0.0.1");
                        listener2.Stop();
                    }
                    catch
                    {
                        // Uncomment the following line to print the error message for failed ports
                        // Debug.WriteLine($"Failed to open port {port}: {e.Message}");
                    }
                }
                Console.WriteLine("Ports Test -- done!");
            }
        }

        protected override string MakeInjectResponse(ScubaDiverMessage req)
        {
            return QuickError("Not Implemented");
        }

        protected override string MakeDomainsResponse(ScubaDiverMessage req)
        {
            RefreshTrickster();

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

        private void RefreshTrickster()
        {
            _trickster ??= new Trickster(Process.GetCurrentProcess());
            _trickster.ScanTypes();
        }

        protected override string MakeTypesResponse(ScubaDiverMessage req)
        {
            RefreshTrickster();

            string assembly = req.QueryString["assembly"];
            List<TheLeftExit.Trickster.Memory.ModuleInfo> matchingAssemblies = _trickster.ScannedTypes.Keys.Where(assm => assm.Name == assembly).ToList();
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


            List<TypesDump.TypeIdentifiers> types = new List<TypesDump.TypeIdentifiers>();
            foreach (var type in _trickster.ScannedTypes[matchingAssemblies.Single()])
            {
                types.Add(new TypesDump.TypeIdentifiers()
                {
                    TypeName = type.Name
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
            return QuickError("Not Implemented");
        }

        protected override string MakeHeapResponse(ScubaDiverMessage arg)
        {
            return QuickError("Not Implemented");
        }

        protected override string MakeObjectResponse(ScubaDiverMessage arg)
        {
            return QuickError("Not Implemented");
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
}
