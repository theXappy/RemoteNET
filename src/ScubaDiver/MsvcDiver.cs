using ScubaDiver.API.Interactions.Dumps;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using ScubaDiver.API.Utils;
using TheLeftExit.Trickster.Memory;
using ModuleInfo = Microsoft.Diagnostics.Runtime.ModuleInfo;
using TypeInfo = System.Reflection.TypeInfo;
using Microsoft.Diagnostics.Runtime;
using System.IO;

namespace ScubaDiver
{
    public class MsvcDiver : DiverBase
    {
        private Dictionary<ModuleInfo, TypeInfo[]> scannedModules = new Dictionary<ModuleInfo, TypeInfo[]>();

        public override void Start(ushort listenPort)
        {
            Logger.Debug("[MsvcDiver] Is logging debugs in release? " + Logger.DebugInRelease.Value);

            // Load or Hijack Newtonsoft.Json
            var nsJson = InitNewtonsoftJson();
            Logger.Debug("[MsvcDiver] Newtonsoft.Json's module path: " + nsJson.Location);

            // Start session
            HttpListener listener = new();
            string listeningUrl = $"http://127.0.0.1:{listenPort}/";
            listener.Prefixes.Add(listeningUrl);
            // Set timeout
            var manager = listener.TimeoutManager;
            manager.IdleConnection = TimeSpan.FromSeconds(5);
            listener.Start();
            Logger.Debug($"[MsvcDiver] Listening on {listeningUrl}...");

            Dispatcher(listener);

            Logger.Debug("[MsvcDiver] Closing listener");
            listener.Stop();
            listener.Close();

            Logger.Debug("[MsvcDiver] Unpinning objects");
            // TODO:
            Logger.Debug("[MsvcDiver] Unpinning finished");

            Logger.Debug("[MsvcDiver] Dispatcher returned, Start is complete.");
        }

        protected override void DispatcherCleanUp()
        {
            // Nothing here for now
        }

        protected override string MakeInjectResponse(HttpListenerRequest req)
        {
            return QuickError("Not Implemented");
        }

        protected override string MakeDomainsResponse(HttpListenerRequest req)
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

        protected override string MakeTypesResponse(HttpListenerRequest req)
        {
            RefreshTrickster();

            string assembly = req.QueryString.Get("assembly");
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

        protected override string MakeTypeResponse(HttpListenerRequest req)
        {
            return QuickError("Not Implemented");
        }

        protected override string MakeHeapResponse(HttpListenerRequest arg)
        {
            return QuickError("Not Implemented");
        }

        protected override string MakeObjectResponse(HttpListenerRequest arg)
        {
            return QuickError("Not Implemented");
        }

        protected override string MakeCreateObjectResponse(HttpListenerRequest arg)
        {
            return QuickError("Not Implemented");
        }

        protected override string MakeInvokeResponse(HttpListenerRequest arg)
        {
            return QuickError("Not Implemented");
        }

        protected override string MakeGetFieldResponse(HttpListenerRequest arg)
        {
            return QuickError("Not Implemented");
        }

        protected override string MakeSetFieldResponse(HttpListenerRequest arg)
        {
            return QuickError("Not Implemented");
        }

        protected override string MakeArrayItemResponse(HttpListenerRequest arg)
        {
            return QuickError("Not Implemented");
        }

        protected override string MakeUnpinResponse(HttpListenerRequest arg)
        {
            return QuickError("Not Implemented");
        }

        public override void Dispose()
        {
        }
    }
}
