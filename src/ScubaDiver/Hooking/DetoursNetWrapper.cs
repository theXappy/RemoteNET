using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using ScubaDiver.API.Hooking;

namespace ScubaDiver.Hooking;

public class DetoursNetWrapper
{
    private static DetoursNetWrapper _instance = null;
    public static DetoursNetWrapper Instance => _instance ??= new();


    private static readonly ConcurrentDictionary<string, HarmonyWrapper.HookCallback> _actualHooks = new();

    public void AddHook(MsvcDiver.UndecoratedExport target, HarmonyPatchPosition pos, MethodInfo mi, Type dlgt)
    {
        //
        // Save a side the patch callback to invoke when the target is called
        //
        //string uniqueId = target.Export.ModulePath + ":" + target.Export.Name;
        //_actualHooks[uniqueId] = patch;

        string moduleName = Path.GetFileNameWithoutExtension(target.Export.ModulePath);
        DetoursNet.Loader.HookMethod(moduleName, target.Export.Name, dlgt, mi);
    }
}