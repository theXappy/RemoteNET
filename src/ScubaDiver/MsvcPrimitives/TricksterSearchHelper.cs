using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ScubaDiver.Rtti;

public static class TricksterScanHelper
{

    /// <summary>
    /// Scan the process memory for vftables to spot instances of First-Class types.
    /// </summary>
    /// <param name="trickster"></param>
    /// <param name="typeInfos"></param>
    /// <returns></returns>
    public static Dictionary<FirstClassTypeInfo, IReadOnlyCollection<ulong>> Scan(Trickster trickster, IEnumerable<FirstClassTypeInfo> typeInfos)
    {

        Dictionary< /*xored vftable*/ nuint, FirstClassTypeInfo> xoredVftableToType = new();
        foreach (var typeInfo in typeInfos)
        {
            if (xoredVftableToType.TryGetValue(typeInfo.XoredVftableAddress, out var old))
            {
                continue;
            }
            xoredVftableToType[typeInfo.XoredVftableAddress] = typeInfo;
        }
        IDictionary</*xored vftable*/ ulong, /*instance pointers*/ IReadOnlyCollection<ulong>> xoredVftablesToInstances = trickster.ScanRegions(xoredVftableToType.Keys, FirstClassTypeInfo.XorMask);

        Dictionary<FirstClassTypeInfo, IReadOnlyCollection<ulong>> res = new();
        foreach (var kvp in xoredVftablesToInstances)
        {
            res[xoredVftableToType[(nuint)kvp.Key]] = kvp.Value;
        }

        return res;
    }
}