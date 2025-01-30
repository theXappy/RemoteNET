using ScubaDiver.API.Hooking;
using ScubaDiver.API.Interactions.Callbacks;
using ScubaDiver.Hooking;
using ScubaDiver.Rtti;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ScubaDiver
{
    public partial class MsvcDiver
    {
        protected override Action HookFunction(FunctionHookRequest request, HarmonyWrapper.HookCallback patchCallback)
        {
            string methodName = request.MethodName;
            ParseRequest(request,
                out ModuleInfo module,
                out TypeInfo typeInfo,
                out HarmonyPatchPosition hookPosition);

            UndecoratedFunction methodToHook;

            if (request.IsBlind)
            {
                methodToHook = new  UndecoratedInternalFunction(
                    methodName,
                    methodName,
                    $"{module.Name}!{methodName}",
                    (long)(module.BaseAddress + request.FuncRelativeAddress),
                    request.ParametersTypeFullNames.Count,
                    module);
            }
            else
            {
                IEnumerable<UndecoratedFunction> allFuncs = GetAllFunctions(module, typeInfo);
                methodToHook = FindMethod(allFuncs, methodName, request.ParametersTypeFullNames);
            }

            Logger.Debug($"[MsvcDiver] Hooking function {methodName}...");
            DetoursNetWrapper.Instance.AddHook(typeInfo, methodToHook, patchCallback, hookPosition);
            Logger.Debug($"[MsvcDiver] Hooked function {methodName}!");

            Action unhook = () =>
            {
                DetoursNetWrapper.Instance.RemoveHook(methodToHook, patchCallback);
            };
            return unhook;
        }

        private UndecoratedFunction FindMethod(IEnumerable<UndecoratedFunction> allFuncs, string methodName, List<string> parametersTypeFullNames)
        {
            // Find all methods with the requested name
            IEnumerable<UndecoratedFunction> overloads = allFuncs.Where(method => method.UndecoratedName == methodName);
            // Find the specific overload with the right argument types
            UndecoratedFunction methodToHook = overloads.SingleOrDefault(method =>
                method.ArgTypes.Skip(1).SequenceEqual(parametersTypeFullNames, TypesComparer));

            if (methodToHook == null)
                throw new Exception($"No matches for {methodName}");
            return methodToHook;
        }

        private void ParseRequest(FunctionHookRequest request, out ModuleInfo module, out TypeInfo typeInfo, out HarmonyPatchPosition hookPosition)
        {
            hookPosition = 0;
            string rawTypeFilter = request.TypeFullName;
            ParseFullTypeName(rawTypeFilter, out var rawAssemblyFilter, out rawTypeFilter);
            var modulesToTypes = _tricksterWrapper.SearchTypes(rawAssemblyFilter, rawTypeFilter);
            if (modulesToTypes.Count != 1)
                throw new Exception($"Expected exactly 1 match for module, got {modulesToTypes.Count}");

            module = modulesToTypes.Keys.Single();
            Rtti.TypeInfo[] typeInfos = modulesToTypes[module].ToArray();
            if (typeInfos.Length != 1)
                throw new Exception($"Expected exactly 1 match for type, got {typeInfos.Length}");
            try
            {
                typeInfo = typeInfos.Single();
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message +
                                    $".\n typeInfos: {String.Join(", ", typeInfos.Select(x => x.ToString()))}");
            }

            string hookPositionStr = request.HookPosition;
            hookPosition = (HarmonyPatchPosition)Enum.Parse(typeof(HarmonyPatchPosition), hookPositionStr);
            if (!Enum.IsDefined(typeof(HarmonyPatchPosition), hookPosition))
                throw new Exception("hook_position has an invalid or unsupported value");
        }

        private IEnumerable<UndecoratedFunction> GetAllFunctions(ModuleInfo module, TypeInfo typeInfo)
        {
            // Get all exported members of the requseted type
            List<UndecoratedSymbol> members = _exportsMaster.GetExportedTypeMembers(module, typeInfo.Name).ToList();

            // Find the first vftable within all members
            // (TODO: Bug? How can I tell this is the "main" vftable?)
            UndecoratedSymbol vftable = members.FirstOrDefault(member => member.UndecoratedName.EndsWith("`vftable'"));

            List<UndecoratedFunction> exportedFuncs = members.OfType<UndecoratedFunction>().ToList();
            List<UndecoratedFunction> virtualFuncs = new List<UndecoratedFunction>();
            if (vftable != null)
            {

                virtualFuncs = VftableParser.AnalyzeVftable(_tricksterWrapper.GetProcessHandle(),
                    module,
                    _exportsMaster.GetUndecoratedExports(module).ToList(),
                    vftable.Address);

                // Remove duplicates - the methods which are both virtual and exported.
                virtualFuncs = virtualFuncs.Where(method => !exportedFuncs.Contains(method)).ToList();
            }
            var allFuncs = exportedFuncs.Concat(virtualFuncs);
            return allFuncs;
        }
    }
}
