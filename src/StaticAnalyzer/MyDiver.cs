using ScubaDiver;
using ScubaDiver.API;
using System.Net;
using System;
using ScubaDiver.API.Interactions.Callbacks;
using ScubaDiver.Hooking;

namespace StaticAnalyzer
{
    public class MyDiver : DiverBase
    {
        public MyDiver(IRequestsListener listener) : base(listener) { }

        protected override string MakeInjectDllResponse(ScubaDiverMessage req)
        {
            return "{\"status\":\"Not implemented\"}";
        }

        protected override void RefreshRuntime()
        {
            // Not implemented
        }

        protected override Action HookFunction(FunctionHookRequest req, HarmonyWrapper.HookCallback patchCallback)
        {
            return () => { }; // Empty action
        }

        protected override ObjectOrRemoteAddress InvokeControllerCallback(IPEndPoint callbacksEndpoint, int token, string stackTrace, object retValue, params object[] parameters)
        {
            return null; // Not implemented
        }

        protected override string MakeDomainsResponse(ScubaDiverMessage req)
        {
            return "{\"status\":\"Not implemented\"}";
        }

        protected override string MakeTypesResponse(ScubaDiverMessage req)
        {
            return "{\"status\":\"Not implemented\"}";
        }

        protected override string MakeTypeResponse(ScubaDiverMessage req)
        {
            return "{\"status\":\"Not implemented\"}";
        }

        protected override string MakeHeapResponse(ScubaDiverMessage arg)
        {
            return "{\"status\":\"Not implemented\"}";
        }

        protected override string MakeObjectResponse(ScubaDiverMessage arg)
        {
            return "{\"status\":\"Not implemented\"}";
        }

        protected override string MakeCreateObjectResponse(ScubaDiverMessage arg)
        {
            return "{\"status\":\"Not implemented\"}";
        }

        protected override string MakeInvokeResponse(ScubaDiverMessage arg)
        {
            return "{\"status\":\"Not implemented\"}";
        }

        protected override string MakeGetFieldResponse(ScubaDiverMessage arg)
        {
            return "{\"status\":\"Not implemented\"}";
        }

        protected override string MakeSetFieldResponse(ScubaDiverMessage arg)
        {
            return "{\"status\":\"Not implemented\"}";
        }

        protected override string MakeArrayItemResponse(ScubaDiverMessage arg)
        {
            return "{\"status\":\"Not implemented\"}";
        }

        protected override string MakeUnpinResponse(ScubaDiverMessage arg)
        {
            return "{\"status\":\"Not implemented\"}";
        }
    }
}
