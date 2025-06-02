using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RemoteNET.BuildOrderHack
{
    internal class BuildOrderHack
    {
        static BuildOrderHack()
        {
            // SHOULD force building of Lifeboat before RemoteNET
            typeof(Lifeboat.Program).ToString();
        }
    }
}
