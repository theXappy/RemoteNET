using RemoteNET.Internal.Extensions;
using System;
using System.Diagnostics;
using System.Linq;

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
                throw new ArgumentException($"Could not find any processes with the name filter '{procName}'");
            }

            // Easy case - single match
            if (candidateProcs.Length == 1)
            {
                return candidateProcs.Single();
            }


            // Harder case - multiple matches. Let's hope it's a single tree
            // Get the only process which doesn't have a parent with the same name.
            var possibleRoots = candidateProcs.Where(proc =>
            {
                Process parentProc;
                try
                {
                    parentProc = proc.GetParent();
                }
                catch (Exception)
                {
                    parentProc = null;
                }
                // if we can't find the parent just go with this process. Hopefully it's the only one anyway
                if (parentProc == null)
                    return true;
                return parentProc.ProcessName != proc.ProcessName;
            }).ToList();

            if (possibleRoots.Count == 1)
            {
                // Single root! Just what we hoped to find.
                return possibleRoots.Single();
            }
            else
            {
                // Multiple roots... this is an issue we can't resolve and have to report back.
                string procsList = null;
                try
                {
                    procsList = string.Join("\n", candidateProcs.Select(proc => $"\t({proc.Id}) {proc.ProcessName}").ToArray());
                }
                catch
                {
                    procsList = "** FAILED TO GENERATE PROCESS LIST **";
                }
                throw new TooManyProcessesException($"Too many processes contains '{procName}' in their name.\n" +
                    $"Those were NOT found to be a single 'tree' of processes with a single parent (root) process.\n" +
                    $"The processes that were found:\n" +
                    $"{procsList}\n" +
                    $"You should narrow it down with a more exclusive substring", candidateProcs.ToArray());
            }
        }
    }
}
