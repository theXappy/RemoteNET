using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using RemoteNET;
using RemoteNET.Internal.Extensions;

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
                Console.WriteLine("Menu:");
                Console.WriteLine("1. Query Remote Instances");
                Console.WriteLine("2. Get Remote Object");
                Console.WriteLine("3. Call `ToString` of Remote Object");
                Console.WriteLine("4. Print methods of Remote Object");
                Console.WriteLine("5. Create remote object");
                Console.WriteLine("6. Invoke example static method (int.parse)");
                Console.WriteLine("7. Field write test");
                Console.WriteLine("8. Steal RSA keys");
                Console.WriteLine("9. Exit");
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
                            break;
                        case 3:
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

                                var type = remoteObj.GetType();
                            }
                            break;
                        case 4:
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
                            break;
                        case 5:
                            Console.WriteLine("Getting MMSData type...");
                            var typeToCreate = remoteApp.GetRemoteType("Samsung.SamsungFlow.Notification.Data.MMSData", "SamsungFlowFramework.NET");
                            Console.WriteLine($"The Type is: {typeToCreate}");
                            Console.WriteLine("Calling activator");

                            var obj = remoteApp.Activator.CreateInstance(typeToCreate);
                            Console.WriteLine("Got new object? " + obj);
                            break;
                        case 6:
                            Console.WriteLine("Invoking int.parse('123')");
                            var remoteIntType = remoteApp.GetRemoteType(typeof(int).FullName);
                            var remoteIntParse = remoteIntType.GetMethod("Parse", new[] { typeof(string) });
                            object x = remoteIntParse.Invoke(null, new object[] { "123" });
                            Console.WriteLine($"Result: {x}");
                            Console.WriteLine($"Result Type: {x.GetType()}");
                            break;
                        case 7:
                            Console.WriteLine("Value of Secrets.AlwaysOne:");
                            var candidate = remoteApp.QueryInstances("*Secrets").Single();
                            var remoteSecrets = remoteApp.GetRemoteObject(candidate);
                            dynamic dynSecrets = remoteSecrets.Dynamify();
                            Console.WriteLine(dynSecrets.AlwaysOne);
                            Console.WriteLine("Enter new value:");
                            int newValue = int.Parse(Console.ReadLine());
                            dynSecrets.AlwaysOne = newValue;
                            Console.WriteLine("Value of Secrets.AlwaysOne again:");
                            Console.WriteLine(dynSecrets.AlwaysOne);
                            Console.WriteLine("Trying fancy x = y = z; statement");
                            int sanity = dynSecrets.AlwaysOne = 777;
                            Console.WriteLine($"Is sanity set to 777? Value: {sanity}");
                            break;
                        case 8:
                            Func<byte[], string> ToHex = arr => string.Join("", arr.Select(b => b.ToString("X2")).ToArray());

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
                                Console.WriteLine("Modulus: " + ToHex(parameters.Modulus));
                                Console.WriteLine("Exponent: " + ToHex(parameters.Exponent));
                                Console.WriteLine("D: " + ToHex(parameters.D));
                                Console.WriteLine("P: " + ToHex(parameters.P));
                                Console.WriteLine("Q: " + ToHex(parameters.Q));
                                Console.WriteLine("DP: " + ToHex(parameters.DP));
                                Console.WriteLine("DQ: " + ToHex(parameters.DQ));
                                Console.WriteLine("InverseQ: " + ToHex(parameters.InverseQ));
                            }

                            break;
                        case 9:
                            // Exiting
                            return;
                    }
                }
            }

        }

        private static void LocalDive()
        {
            using Diver dive = new();
            ushort port = (ushort)(new Random()).Next();
            dive.Dive(port);
        }
    }
}
