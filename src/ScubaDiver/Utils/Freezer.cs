using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ScubaDiver.Utils
{

    internal static class Freezer
    {
        public static PinnedObjectInfo Freeze(object target)
        {
            ulong freezeAddr = 0;
            // Allows the freeze function to indicate freezing was done
            ManualResetEvent freezeFeedback = new ManualResetEvent(false);
            // Allows us to unfreeze later
            ManualResetEvent unfreezeRequired = new ManualResetEvent(false);

            Task freezingTask = Task.Run(() => FreezeInternal(target, ref freezeAddr, freezeFeedback, unfreezeRequired);

            // Wait for freezing task to report back address
            freezeFeedback.WaitOne();
            freezeFeedback.Close();

            return new PinnedObjectInfo(target, freezeAddr, unfreezeRequired, freezingTask);
        }

        /// <summary>
        /// Freezes an object at it's current address
        /// </summary>
        /// <param name="o">Object to freeze</param>
        /// <param name="freezeAddr">
        /// Used to report back the freezed object's address. Only valid after <see cref="freezeFeedback"/> was set!
        /// </param>
        /// <param name="freezeFeedback">Event which the freezer will call once the object is frozen</param>
        /// <param name="unfreezeRequested">Event the freezer waits on until unfreezing is requested by the caller</param>
        private static unsafe void FreezeInternal(object o, ref ulong freezeAddr, ManualResetEvent freezeFeedback, ManualResetEvent unfreezeRequested)
        {
            // TODO: This "costs" us a thread (probably from the thread pool) for every pinned object.
            // Maybe this should be done in another class and support multiple objects per thread
            // something like:
            // fixed(byte* first ...)
            // fixed(byte* second...)
            // fixed(byte* third ...)
            // {
            // ...
            // }
            fixed (byte* ptr = &Unsafe.As<Pinnable>(o).Data)
            {
                // Our fixed pointer to the first field of the class lets
                // us calculate the address to the object.
                // We have:
                //                 🠗
                // [ Method Table ][ Field 1 ][ Field 2 ]...
                //
                // And we want: 
                // 🠗
                // [ Method Table ][ Field 1 ][ Field 2 ]...
                //
                // As far as I understand the Method Table is a pointer which means
                // it's 4 bytes in x32 and 8 bytes in x64 (Hence using `IntPtr.Size`)
                IntPtr iPtr = new IntPtr(ptr);
                freezeAddr = ((ulong)iPtr.ToInt64()) - (ulong)IntPtr.Size;
                freezeFeedback.Set();
                unfreezeRequested.WaitOne();
                GC.KeepAlive(iPtr);
            }
        }

    }
}
