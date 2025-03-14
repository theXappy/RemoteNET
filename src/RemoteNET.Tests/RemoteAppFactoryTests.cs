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
            var remoteApp = RemoteAppFactory.Connect("notepad++", RuntimeType.Unmanaged, ConnectionStrategy.DllHijack);

            // Assert
        }

    }
}
