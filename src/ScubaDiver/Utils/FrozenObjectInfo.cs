using System.Threading;
using System.Threading.Tasks;

namespace ScubaDiver.Utils
{
    /// <summary>
    /// This object represents an object that has been pinned by <see cref="Diver"/>.
    /// Creating this object alone isn't enough to pin an object. It is only used to keep track of
    /// pinned objects' metadata.
    /// </summary>
    public class FrozenObjectInfo 
    {
        /// <summary>
        /// Reference to the actually pinned object
        /// </summary>
        public object Object { get; private set; }

        /// <summary>
        /// Address of the pinned object (at time of pinning, hopefully forever)
        /// </summary>
        public ulong Address { get; private set; }

        /// <summary>
        /// The event to call when unfreezing is desired
        /// </summary>
        public ManualResetEvent UnfreezeEvent { get; private set; }

        /// <summary>
        /// The task that is freezing the object
        /// </summary>
        public Task FreezeTask { get; private set; }

        public FrozenObjectInfo(object o, ulong address, ManualResetEvent unfreezeEvent, Task freezeTask)
        {
            Object = o;
            Address = address;
            UnfreezeEvent = unfreezeEvent;
            FreezeTask = freezeTask;
        }

        public override string ToString()
        {
            return $"{nameof(FrozenObjectInfo)} : {{ Obj=[{this.Object}], HashCode={this.Object.GetHashCode()}, Address={this.Address:X16} }}";
        }
    }
}
