using RemoteNET.Internal.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace RemoteNET.Utils
{
    public static class ProcessHelper
    {
        /// <summary>
        /// Gets the single process in the system with a given name. 'Root' means that if a process tree of several
        /// proceses with the same name exist it returns the tree's root.
        /// </summary>
        public static Process GetSingleRoot(string procName)
        {
            // Need to add ".exe" extensions so if 2 proccesses exists such that 1 has a substring proc name of the other
            // we'd still be able to tell them apart. Example:
            // Proc #1 - amazingStuff.exe
            // Proce #2 - amazingStuffForEver.exe
            //
            // To get process 1 we can use the argument "amazingstuff.exe"
            // To get process 2 we can use the argument "amazingStuffFor"
            var candidateProcs = Process.GetProcesses().Where(proc => (proc.ProcessName + ".exe").Contains(procName)).ToArray();
            if (candidateProcs.Length == 0)
            {
                return null;
            }

            // Easy case - single match
            if (candidateProcs.Length == 1)
            {
                return candidateProcs.Single();
            }


            // Harder case - multiple matches. Let's hope it's a single tree
            // Get the only process which doesn't have a parent with the same name.
            var target = candidateProcs.SingleOrDefault(proc =>
            {
                var parentProc = proc.GetParent();
                // if we can't find the parent just go with this process. Hopefully it's the only one anyway
                if (parentProc == null)
                    return true;
                return parentProc.ProcessName != proc.ProcessName;
            });
            return target;
        }
    }
}
