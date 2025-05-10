using RemoteNET;
using RemoteNET.Access;
using RemoteNET.Common;
using RemoteNET.RttiReflection;
using ScubaDiver.API.Hooking;
using SPen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace ConsoleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Starting...");

            // Connect to the C++ target process named "gN"  
            RemoteApp remoteApp = RemoteAppFactory.Connect("gN", RuntimeType.Unmanaged);
            Console.WriteLine("Successfully connected to the target process");

            SPen.Uuid dummyUuid = new Uuid(remoteApp);
            SPen.String dummyStr = new SPen.String(remoteApp);
            dummyStr.Construct(1);
            dummyUuid.ToString(dummyStr);

            // Dump bytes of String
            ulong address = dummyStr.__address;
            Console.WriteLine($"String address: 0x{address:X16} (Allocated by us)");
            remoteApp.Marshal.QuickDump(address, 0x30);
            // Print content of dummy string
            Console.WriteLine("Dummy string: " + ReadString(dummyStr));

            NoteZip noteZip = new NoteZip(remoteApp);
            Dictionary<ulong, ulong> stringAddresses = new Dictionary<ulong, ulong>();
            for (int i = 0; i < 4096; i++)
            {
                remoteApp.Marshal.WriteQword(address, 0);

                ulong zeroTest = remoteApp.Marshal.ReadQword(address);
                if (zeroTest != 0)
                {
                    throw new Exception("Failed to zero out the address");
                }

                noteZip.Construct(dummyStr);
                // Dump bytes of NoteZip
                address = noteZip.__address;
                //Console.WriteLine($"NoteZip address: 0x{address:X16} (Allocated by us)");
                //remoteApp.Marshal.QuickDump(address, 0x30);
                // Read first 8 bytes - those are the pointer to the unnamed inner struct
                ulong innerStructAddress = remoteApp.Marshal.ReadQword(address);
                ulong strAddress = innerStructAddress + 8;
                ulong vmtAddress = remoteApp.Marshal.ReadQword(strAddress);


                //var spenStringRo = remoteApp.GetRemoteObject(strAddress, "libSpen_base.dll!SPen::String");
                //SPen.String s = new SPen.String(spenStringRo.Dynamify());
                Console.WriteLine($"[{i:d2}]String address: 0x{strAddress:X16} (Allocated by remote lib)");
                ulong rangeStart = strAddress & 0xFFFFFFFFFFFFFF00;
                stringAddresses[rangeStart] = strAddress;
            }

            int uuidCount = 0;
            // Define the hook callback that will be called when the method is invoked  
            HookAction callback = (HookContext context, dynamic instance, dynamic[] args, dynamic retValue) =>
            {
                uuidCount++;

                // Target
                ulong? instanceAddress = null;
                if (instance is ulong ul)
                    instanceAddress = ul;
                else if (instance is DynamicRemoteObject dro)
                    instanceAddress = dro.__ro.RemoteToken;

                if (instanceAddress == null)
                {
                    return;
                }

                // Check if any string address is adjacent to the instance address
                ulong rangeStart = instanceAddress.Value & 0xFFFFFFFFFFFFFF00;
                if (stringAddresses.TryGetValue(rangeStart, out ulong strAddress))
                {
                    Console.WriteLine($"[uuidCount={uuidCount}] Found string address: 0x{strAddress:X16} (Allocated by remote lib) !!!");
                    //var spenStringRo = remoteApp.GetRemoteObject(strAddress, "libSpen_base.dll!SPen::String");
                    //SPen.String s = new SPen.String(spenStringRo.Dynamify());
                    //Console.WriteLine("String content: " + ReadString(s));
                }
                else
                {
                    Console.WriteLine($"[uuidCount={uuidCount}] No string found in the range 0x{rangeStart:X16}");
                }
            };

            // Get the type and method to hook  
            var type = remoteApp.GetRemoteType("SPen::Uuid");
            var method = type.GetMethod("ApplyBinary", new Type[1] { typeof(byte[]) });
            remoteApp.HookingManager.Patch(method, prefix: callback);

            Console.WriteLine("Hook registered successfully. Press Enter to exit...");
            Console.ReadLine();

            Console.WriteLine("Unhooking");
            remoteApp.HookingManager.UnhookMethod(method, callback);

            // Clean up  
            Console.WriteLine("Disposing Remote App");
            remoteApp.Dispose();



            Console.WriteLine("Done.");
            Console.ReadKey();
        }

        static string ReadString(SPen.String s)
        {
            var app = GetApp(s);
            ulong ptr1 = app.Marshal.ReadQword(s.__address + 8);
            if (ptr1 == 0)
                throw new Exception("Failed to read string pointer");


            // DEBUG: print around ptr 2
            app.Marshal.QuickDump(ptr1, 0x30);

            ulong ptr2 = app.Marshal.ReadQword(ptr1 + 16);
            if (ptr2 == 0)
                throw new Exception("Failed to read string data pointer");

            byte[] data = app.Marshal.Read((nint)ptr2, s.GetLength() * 2);
            return Encoding.Unicode.GetString(data);
        }

        static RemoteApp GetApp(object o)
        {
            var dro = o.GetType().GetField("__dro", ~(BindingFlags.Static)).GetValue(o) as DynamicRemoteObject;
            if (dro == null)
                throw new Exception("Failed to get __dro field");
            return dro.__ra;
        }
    }
}
