using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using RemoteNET;
using RemoteNET.Internal.Extensions;
using ScubaDiver.API;

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
                var candidateProcs = Process.GetProcesses().Where(proc => (proc.ProcessName+".exe").Contains(procName)).ToArray();
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
                    Process curr = candidateProcs[i];
                    Console.WriteLine($"{i + 1}. {curr.ProcessName} " +
                                      $"(ID = {curr.Id}) " +
                                      $"[Parent: {curr.GetParent().ProcessName} (ID = {curr.GetParent().Id})]");
                }

                // Get the only process which doesn't have a parent with the same name.
                Console.WriteLine("Assuming all process are in the same process tree.\n" +
                                  "Getting root (First with different parent process name).");
                target = candidateProcs.Single(proc =>
                {
                    var parentProc = proc.GetParent();
                    Debug.WriteLine($"Procces {proc.ProcessName} (Id={proc.Id} is son of {parentProc.ProcessName} (Id={parentProc.Id})");
                    return parentProc.ProcessName != proc.ProcessName;
                });
                break;
            }

            Console.WriteLine($"Selected target: {target.ProcessName} " +
                              $"(ID = {target.Id}) " +
                              $"[Parent: {target.GetParent().ProcessName} (ID = {target.GetParent().Id})]");
            RemoteApp remoteApp = RemoteApp.Connect(target);
            if (remoteApp == null)
            {
                Console.WriteLine("Something went wrong and we couldn't connect to the remote process... Aborting.");
                return;
            }

            List<RemoteObject> remoteObjects = new List<RemoteObject>();
            while (true)
            {
                if (DoSingleMenu(remoteApp, remoteObjects)) 
                    break;
            }
            
            remoteApp.Dispose();
        }

        private static bool DoSingleMenu(RemoteApp remoteApp, List<RemoteObject> remoteObjects)
        {
            Console.WriteLine("Menu:");
            Console.WriteLine("1. Query Remote Instances");
            Console.WriteLine("2. Get Remote Object");
            Console.WriteLine("3. Call `ToString` of Remote Object");
            Console.WriteLine("4. Print methods of Remote Object");
            Console.WriteLine("5. Invoke example static method (int.parse)");
            Console.WriteLine("6. Steal RSA keys");
            Console.WriteLine("7. Subscribe to Event");
            Console.WriteLine("8. Exit");
            string input = Console.ReadLine();
            ulong addr;
            uint index;
            if (int.TryParse(input, out int userChoice))
            {
                switch (userChoice)
                {
                    case 1:
                        {
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
                        }
                        break;
                    case 2:
                        {
                            // Getting object
                            Console.WriteLine("Enter address (decimal):");
                            input = Console.ReadLine();
                            if (ulong.TryParse(input, out addr))
                            {
                                try
                                {
                                    RemoteObject remoteObject = remoteApp.GetRemoteObject(addr);
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
                        }
                        break;
                    case 3:
                        {
                            // Getting object
                            Console.WriteLine("Enter local index of remote object:");
                            if (!uint.TryParse(Console.ReadLine(), out index) || index >= remoteObjects.Count)
                            {
                                Console.WriteLine("Bad input.");
                            }
                            else
                            {
                                var remoteObj = remoteObjects[(int)index];
                                var dynObject = remoteObj.Dynamify();
                                var toStringRes = dynObject.ToString();
                                Console.WriteLine(toStringRes);
                            }
                        }
                        break;
                    case 4:
                        {
                            // Getting object
                            Console.WriteLine("Enter local index of remote object:");
                            if (!uint.TryParse(Console.ReadLine(), out index) || index >= remoteObjects.Count)
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
                        }
                        break;
                    case 5:
                        {
                            Console.WriteLine("Invoking int.parse('123')");
                            var remoteIntType = remoteApp.GetRemoteType(typeof(int));
                            var remoteIntParse = remoteIntType.GetMethod("Parse", new[] { typeof(string) });
                            object x = remoteIntParse.Invoke(null, new object[] { "123" });
                            Console.WriteLine($"Result: {x}");
                            Console.WriteLine($"Result Type: {x.GetType()}");
                        }
                        break;
                    case 6:
                        {
                            // Finding every RSACryptoServiceProvider instance
                            var rsaProviderCandidates = remoteApp.QueryInstances(typeof(RSACryptoServiceProvider));
                            foreach (CandidateObject candidateRsa in rsaProviderCandidates)
                            {
                                RemoteObject rsaProv = remoteApp.GetRemoteObject(candidateRsa);
                                dynamic dynamicRsaProv = rsaProv.Dynamify();
                                // Calling remote `ExportParameters`.
                                // First parameter (true) indicates we want the private key.
                                Console.WriteLine(" * Key found:");
                                dynamic parameters = dynamicRsaProv.ExportParameters(true);
                                Console.WriteLine("Modulus: " + HexUtils.ToHex(parameters.Modulus));
                                Console.WriteLine("Exponent: " + HexUtils.ToHex(parameters.Exponent));
                                Console.WriteLine("D: " + HexUtils.ToHex(parameters.D));
                                Console.WriteLine("P: " + HexUtils.ToHex(parameters.P));
                                Console.WriteLine("Q: " + HexUtils.ToHex(parameters.Q));
                                Console.WriteLine("DP: " + HexUtils.ToHex(parameters.DP));
                                Console.WriteLine("DQ: " + HexUtils.ToHex(parameters.DQ));
                                Console.WriteLine("InverseQ: " + HexUtils.ToHex(parameters.InverseQ));
                            }
                        }
                        break;
                    case 7:
                        {
                            Console.WriteLine("Enter local index of remote object:");
                            if (!uint.TryParse(Console.ReadLine(), out index) || index >= remoteObjects.Count)
                            {
                                Console.WriteLine("Bad input.");
                            }
                            else
                            {
                                RemoteObject remoteObj = remoteObjects[(int)index];
                                Console.WriteLine("Enter event name:");
                                string eventName = Console.ReadLine().Trim();
                                DiverCommunicator.LocalEventCallback func = (args) =>
                                {
                                    Console.WriteLine($"[!] Caught event {eventName} firing! Recieved {args.Length} args.");
                                    foreach (ObjectOrRemoteAddress item in args)
                                    {
                                        RemoteObject ro = remoteApp.GetRemoteObject(item.RemoteAddress);
                                        dynamic dro = ro.Dynamify();
                                        Console.WriteLine($"  ARG: {dro.ToString()} ({dro.GetType().FullName})");
                                        ro.Dispose();

                                    }
                                    return (false, null);
                                };
                                remoteObj.EventSubscribe(eventName, func);
                                Console.WriteLine("Done registring. wait for invocations.");
                            }
                        }
                        break;
                    case 8:
                        // Exiting
                        return true;
                }
            }

            return false;
        }

        private static void LocalDive()
        {
            throw new NotImplementedException("Disbaled for now.");
            //using Diver dive = new();
            //ushort port = (ushort)(new Random()).Next();
            //dive.Dive(port);
        }
    }
}
