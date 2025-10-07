using RemoteNET.Access;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RemoteNET.Tests
{
    public class RemoteAppFactoryTests
    {
        [Test]
        public void TestDllHijack()
        {
            // Arrange

            // Act
            ConnectionConfig cc = new ConnectionConfig
            {
                Strategy = ConnectionStrategy.DllHijack,
                TargetDllToProxy = "TODO"
            };
            var remoteApp = RemoteAppFactory.Connect("notepad++", RuntimeType.Unmanaged, cc);

            // Assert
        }

    }
}
