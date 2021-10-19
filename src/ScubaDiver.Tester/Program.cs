using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using RemoteObject;

namespace ScubaDiver.Tester
{
    class Program
    {
        static void Main(string[] args)
        {
            //LocalDive();
            RemoteDive();
        }

        private static void RemoteDive()
        {
            Process target;
            while (true)
            {
                Console.WriteLine("Enter process name (or substring)");
                string procName = Console.ReadLine();
                var candidateProcs = Process.GetProcesses().Where(proc=>proc.ProcessName.Contains(procName)).ToArray();
                if (candidateProcs.Length == 0)
                {
                    Console.WriteLine("No processes found.");
                    continue;
                }
                if (candidateProcs.Length == 1)
                {
                    target = candidateProcs.Single();
                    break;
                }
                Console.WriteLine("There were too many results:");
                for (int i = 0; i < candidateProcs.Length; i++)
                {
                    Console.WriteLine($"{i + 1}. {candidateProcs[i].ProcessName}");
                }

                target = candidateProcs.First();
                break;
            }
            Console.WriteLine($"Target Process: {target.ProcessName}");
            RemoteApp remoteApp = RemoteApp.Connect(target);

            List<RemoteObject.RemoteObject> remoteObjects = new List<RemoteObject.RemoteObject>();
            while (true)
            {
                Console.WriteLine("Menu:");
                Console.WriteLine("1. Query Remote Instances");
                Console.WriteLine("2. Get Remote Object");
                Console.WriteLine("3. Call `ToString` of Remote Object");
                Console.WriteLine("4. Print methods of Remote Object");
                Console.WriteLine("5. Create remote object");
                Console.WriteLine("6. Exit");
                string input = Console.ReadLine();
                ulong addr;
                uint index;
                if (int.TryParse(input, out int userChoice))
                {
                    switch (userChoice)
                    {
                        case 1:
                            Console.WriteLine("Enter type name to query");
                            string typeName = Console.ReadLine().Trim();
                            if (string.IsNullOrWhiteSpace(typeName))
                            {
                                // Assuming user wants all types
                                typeName = null;
                            }
                            var res = remoteApp.QueryInstances(typeName).ToList();
                            Console.WriteLine("Instances:");
                            for (int i = 0; i < res.Count; i++)
                            {
                                Console.WriteLine($"{i + 1}. {res[i].Address}, TypeFullName: {res[i].TypeFullName}");
                            }

                            break;
                        case 2:
                            // Getting object
                            Console.WriteLine("Enter address (decimal):");
                            input = Console.ReadLine();
                            if (ulong.TryParse(input, out addr))
                            {
                                try
                                {
                                    RemoteObject.RemoteObject remoteObject = remoteApp.GetRemoteObject(addr);
                                    remoteObjects.Add(remoteObject);
                                    Console.WriteLine($"Get back this object: {remoteObject}");
                                    Console.WriteLine($"This object's local index is {remoteObjects.IndexOf(remoteObject)}");
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine("ERROR:");
                                    Console.WriteLine(e);
                                }
                            }
                            break;
                        case 3:
                            // Getting object
                            Console.WriteLine("Enter local index of remote object:");
                            if (!uint.TryParse(Console.ReadLine(), out  index) || index >= remoteObjects.Count)
                            {
                                Console.WriteLine("Bad input.");
                            }
                            else
                            {
                                var remoteObj = remoteObjects[(int)index];
                                var dynObject = remoteObj.Dynamify();
                                var toStringRes = dynObject.ToString();
                                Console.WriteLine(toStringRes);

                                var type = remoteObj.GetType();
                            }
                            break;
                        case 4:
                            // Getting object
                            Console.WriteLine("Enter local index of remote object:");
                            if (!uint.TryParse(Console.ReadLine(), out  index) || index >= remoteObjects.Count)
                            {
                                Console.WriteLine("Bad input.");
                            }
                            else
                            {
                                var remoteObj = remoteObjects[(int)index];
                                var type = remoteObj.GetType();
                                Console.WriteLine($"Methods of {type.FullName}:");
                                int i = 1;
                                foreach (MethodInfo methodInfo in type.GetMethods())
                                {
                                    var argsString = string.Join(", ",
                                        methodInfo.GetParameters().Select(arg => arg.ParameterType + " " + arg.Name));
                                    Console.WriteLine($"{i++}. {methodInfo.ReturnType} {methodInfo.Name}({argsString})");
                                }
                            }
                            break;
                        case 5:
                            Console.WriteLine("Getting MMSData type...");
                            var typeToCreate = remoteApp.GetRemoteType("Samsung.SamsungFlow.Notification.Data.MMSData", "SamsungFlowFramework.NET");
                            Console.WriteLine($"The Type is: {typeToCreate}");
                            Console.WriteLine("Calling activator");

                            var obj = remoteApp.Activator.CreateInstance(typeToCreate);
                            Console.WriteLine("Got new object? "+obj);
                            break;
                        case 6:
                            // Exiting
                            return;
                    }
                }
            }

        }

        private static void LocalDive()
        {
            using Diver dive = new();
            dive.Dive();
        }
    }
}
