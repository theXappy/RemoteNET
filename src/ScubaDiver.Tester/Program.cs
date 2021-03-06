using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using RemoteNET;
using static RemoteNET.Utils.Lambda;

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
            RemoteApp remoteApp = null;
            while (true)
            {
                Console.WriteLine("Enter process name (or substring)");
                string procName = Console.ReadLine();
                try
                {
                    remoteApp = RemoteApp.Connect(procName);
                    if (remoteApp == null)
                    {
                        Console.WriteLine("Something went wrong, try again.");
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("ERROR: "+ex);
                    continue;
                }
                break;
            }

            List<RemoteObject> remoteObjects = new();
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
            Console.WriteLine("8. Hook Method");
            Console.WriteLine("9. Get Remote Object");
            Console.WriteLine("10. Exit");
            string input = Console.ReadLine();
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
                            if (ulong.TryParse(input, out ulong addr))
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
                                dynamic dro = remoteObj.Dynamify();
                                Action<dynamic, dynamic> callback = new((dynamic arg1, dynamic arg2) => Console.WriteLine("INVOKED!!"));

                                Console.WriteLine("Enter event name:");
                                string eventName = Console.ReadLine().Trim();
                                //dynamic eventObj = DynamicRemoteObject.GetDynamicMember(dro, eventName);
                                //eventObj += callback;
                                dro.SomeEvent += __((dynamic arg1, dynamic arg2) => Console.WriteLine("INVOKED!!"));

                                Console.WriteLine("Done registring. wait for invocations.");
                                Console.WriteLine("Press ENTER to unsubscribe.");
                                Console.ReadLine();
                                //dro.SomeEvent -= callback;
                            }
                        }
                        break;
                    case 8:
                        {
                            MethodInfo mi;
                            Console.WriteLine("Full type name:");
                            string type = Console.ReadLine();
                            string methodName = String.Empty;
                            try
                            {
                                Type t = remoteApp.GetRemoteType(type);
                                if (t == null)
                                {
                                    Console.WriteLine("Failed to find the type.");
                                    break;
                                }
                                Console.WriteLine("Method name:");
                                methodName = Console.ReadLine();

                                var methods = t.GetMethods((BindingFlags)0xffff).Where(mInfo=>mInfo.Name == methodName).ToArray();
                                if(methodName.Length < 0 )
                                {
                                    throw new Exception("No such function");
                                }
                                else if (methods.Length > 1)
                                {
                                    Console.WriteLine("More than one overload found:");
                                    for (int i = 0; i < methods.Length; i++)
                                    {
                                        string paramsList = string.Join(",", methods[i].GetParameters().Select(prm => prm.ParameterType.FullName+ " " + prm.Name));
                                        Console.WriteLine($"{i + 1}. {methods[i].ReturnType.Name} {methodName}({paramsList})");
                                    }
                                    Console.WriteLine("Which overload?");
                                    int res = int.Parse(Console.ReadLine())-1;
                                    mi = methods[res];
                                }
                                else
                                {
                                    mi = methods.Single();
                                }
                                if (mi == null)
                                {
                                    Console.WriteLine("Failed to find the method in the resolved type.");
                                    break;
                                }
                            }
                            catch (Exception)
                            {
                                Console.WriteLine("Something went wrong...");
                                break;
                            }

                            BlockingCollection<string> pcCollection = new();
                            Task printerTask = Task.Run(() =>
                            {
                                while(pcCollection.IsCompleted)
                                {
                                    if (pcCollection.TryTake(out string item))
                                    {
                                        Console.WriteLine(item);
                                    }
                                    else
                                    {
                                        Thread.Sleep(100);
                                    }
                                }
                            });

                            HookAction callback = (dynamic instace, dynamic[] args) =>
                            {
                                Console.WriteLine($"$$$ Hooked Function {type}.{methodName} called! $$$");
                                foreach (dynamic d in args)
                                {
                                    Console.WriteLine($"ARG: {d.ToString()}");
                                    if(d is string str)
                                    {
                                        pcCollection.Add(str);
                                    }
                                    else
                                    {
                                        pcCollection.Add("ERROR: NON STR ARGUMENT!!");
                                    }
                                }
                            };

                            Console.WriteLine("Which position to hook?");
                            Console.WriteLine("1. Prefix");
                            Console.WriteLine("2. Postfix");
                            Console.WriteLine("3. Finalizer");
                            if (!int.TryParse(Console.ReadLine(), out int selection) || (selection < 1 || selection > 4))
                            {
                                Console.WriteLine("Bad input.");
                                break;
                            }
                            switch(selection)
                            {
                                case 1:
                                    remoteApp.Harmony.Patch(mi, prefix: callback);
                                    break;
                                case 2:
                                    remoteApp.Harmony.Patch(mi, postfix: callback);
                                    break;
                                case 3:
                                    remoteApp.Harmony.Patch(mi, finalizer: callback);
                                    break;
                            }
                        }
                        break;
                    case 9:
                        {
                            try
                            {
                                Console.WriteLine("Enter full type name to get");
                                string typeName = Console.ReadLine().Trim();
                                if (string.IsNullOrWhiteSpace(typeName))
                                {
                                    // Assuming user wants all types
                                    typeName = null;
                                }

                                var res = remoteApp.GetRemoteType(typeName);
                                Console.WriteLine("Results:");
                                Console.WriteLine(res);
                                Console.WriteLine("Props:");
                                foreach (var a in res.GetProperties())
                                {
                                    Console.WriteLine(a);
                                    Console.WriteLine("Getter: " + a.GetGetMethod());
                                    Console.WriteLine("Setter: " + a.GetSetMethod());
                                }
                            }
                            catch { }
                        }
                        break;
                    case 10:
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
