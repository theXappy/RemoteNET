using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace RemoteNET.RttiReflection;

/// <summary>
/// Resolves local and remote types. Contains a cache so the same TypeFullName object is returned for different
/// resolutions for the same remote type.
/// </summary>
public class RttiTypesResolver
{
    // Since the resolver works with a cache that should be global we make the whole class a singleton
    public static RttiTypesResolver Instance = new();

    private RttiTypesResolver() { }

    private readonly Dictionary<Tuple<string, string>, Type> _cacheByNames = new();
    private readonly Dictionary<long, Type> _cacheByMethodTable = new();

    public void RegisterType(Type type)
    {
        // Register by name
        RegisterType(type.Assembly.GetName().Name, type.FullName, type);

        // Register by Method Table address (only for RTTI)
        if (type is RemoteRttiType rttiType)
        {
            foreach (KeyValuePair<string, long> methodTable in rttiType.MethodTables)
            {
                long methodTableAddress = methodTable.Value;
                RegisterType(methodTableAddress, type);
            }

        }
    }

    public void RegisterType(string assemblyName, string typeFullName, Type type)
    {
        _cacheByNames[new Tuple<string, string>(assemblyName, typeFullName)] = type;
    }
    public void RegisterType(long methodTableAddress, Type type)
    {
        _cacheByMethodTable[methodTableAddress] = type;
    }

    public Type Resolve(string assemblyName, string typeFullName)
    {
        // Start by searching cache
        Type resolvedType;
        if (_cacheByNames.TryGetValue(new Tuple<string, string>(assemblyName, typeFullName), out resolvedType))
        {
            return resolvedType;
        }

        // Check if we have this (managed) type locally - in one of the loaded assemblies.
        // We EXPLICITLY AVOID ENUMS, because that breaks RemoteEnum.
        IEnumerable<Assembly> assemblies = AppDomain.CurrentDomain.GetAssemblies();

        // Filter assemblies but avoid filtering for "mscorlib" because it's the devil
        if (assemblyName?.Equals("mscorlib") == false)
        {
            assemblies = assemblies.Where(assembly => assembly.FullName.Contains(assemblyName ?? ""));
        }

        // Search all assemblies which passed the filter
        foreach (Assembly assembly in assemblies)
        {
            resolvedType = assembly.GetType(typeFullName);
            if (resolvedType != null)
            {
                // Found the type!
                // But retreat if it's an enum (and get remote proxy of it instead)
                if (resolvedType.IsEnum)
                {
                    resolvedType = null;
                }
                break;
            }
        }

        if (resolvedType is RemoteRttiType)
        {
            RegisterType(assemblyName, typeFullName, resolvedType);
        }
        return resolvedType;
    }

    public Type Resolve(long methodTableAddress)
    {
        return _cacheByMethodTable.TryGetValue(methodTableAddress, out Type resolvedType) ? resolvedType : null;
    }
}