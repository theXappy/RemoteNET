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
            // Fetch the WNote object from the target process
            remoteApp.Communicator.StartOffensiveGC("Samsung.SPenNative.Glue.dll.dll");
            CandidateObject cand = remoteApp.QueryInstances("Samsung.SPenNative.Glue.dll!SPen::WNote").FirstOrDefault();
            if (cand == null)
            {
                Console.WriteLine("Failed to find WNote object");
                return;
            }
            // Upgrading to RemoteObject
            RemoteObject ro = remoteApp.GetRemoteObject(cand);
            // Cast
            Type target = remoteApp.GetRemoteType("libSpen_worddoc.dll!SPen::WNote");
            RemoteObject castedRo = ro.Cast(target);
            // Upgrading to DRO
            DynamicRemoteObject dro = castedRo.Dynamify();
            // Upgrade to WNote
            SPen.WNote wNote = new SPen.WNote(dro);
            Console.WriteLine("Successfully fetched WNote object");

            int pageCount = wNote.GetPageCount();
            Console.WriteLine($"Page count: {pageCount}");
            // Fetch the first page
            SPen.WPage wPage = null;
            try
            {
                wPage = wNote.GetPage(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to fetch WPage object: {ex.Message}");
                return;
            }
            if (wPage == null)
            {
                Console.WriteLine("Failed to fetch WPage object");
                return;
            }

            Console.WriteLine("Successfully fetched WPage object");
            // Num of objects within
            int objectCount = wPage.GetObjectCount(true);
            Console.WriteLine($"Object count (true?): {objectCount}");
            objectCount = wPage.GetObjectCount(false);
            Console.WriteLine($"Object count (false?): {objectCount}");


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
