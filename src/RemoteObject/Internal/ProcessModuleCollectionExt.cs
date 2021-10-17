using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RemoteObject.Internal
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
