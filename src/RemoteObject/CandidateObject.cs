using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RemoteObject
{
    /// <summary>
    /// A candidate for a remote object.
    /// Holding this item does not mean having a meaningful hold of the remote object. To gain one use <see cref="RemoteApp"/>
    /// </summary>
    public class CandidateObject
    {
            public ulong Address { get; set; }
            public string TypeFullName { get; set; }

            public CandidateObject(ulong address, string typeFullName)
            {
                Address = address;
                TypeFullName = typeFullName;
            }
    }
}
