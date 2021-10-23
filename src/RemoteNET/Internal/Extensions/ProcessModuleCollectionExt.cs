using System.Collections.Generic;
using System.Diagnostics;

namespace RemoteNET.Internal.Extensions
{
    public static class ProcessModuleCollectionExt
    {
        public static IEnumerable<ProcessModule> AsEnumerable(this ProcessModuleCollection collection)
        {
            for (var i = 0; i < collection.Count; i++)
            {
                yield return collection[i];
            }
        }
    }
}
