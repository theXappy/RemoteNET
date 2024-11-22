using RemoteNET;
using System.Globalization;


namespace RemoteNET.Tests
{

    [TestFixture]
    public class DynamicRemoteCharStarTests
    {
        [Test]
        public void TestPropertyAccess()
        {
            string initialString = "Hello, world!";
            dynamic proxy = new DynamicRemoteCharStar(null, 0x100, initialString);

            // Test property access using dynamic proxy
            Assert.That(proxy.Length, Is.EqualTo(13));
            Assert.That(proxy.Substring(0, 5), Is.EqualTo("Hello"));
        }

        [Test]
        public void TestPropertyModification()
        {
            string initialString = "Hello, world!";
            dynamic proxy = new DynamicRemoteCharStar(null, 0x100, initialString);

            // Modify property using dynamic proxy
            proxy = proxy.Replace("world", "C#");
            Assert.That(proxy, Is.EqualTo("Hello, C#!"));
        }

        [Test]
        public void TestMethodInvocation()
        {
            string initialString = "Hello, world!";
            dynamic proxy = new DynamicRemoteCharStar(null, 0x100, initialString);

            // Invoke method using dynamic proxy
            proxy = proxy.ToUpper();
            Assert.That(proxy, Is.EqualTo("HELLO, WORLD!"));
        }

        [Test]
        public void TestSubclassParameter()
        {
            string initialString = "Hello, world!";
            dynamic proxy = new DynamicRemoteCharStar(null, 0x100, initialString);

            // Invoke ToString method with a string parameter
            string formattedString = proxy.ToString(CultureInfo.InvariantCulture);
            Assert.That(formattedString, Is.EqualTo("Hello, world!"));

            // Invoke ToString method with an IFormatProvider (a subclass of object) as a parameter
            IFormatProvider formatProvider = CultureInfo.InvariantCulture;
            string formattedStringWithProvider = proxy.ToString(formatProvider);
            Assert.That(formattedStringWithProvider, Is.EqualTo("Hello, world!"));
        }

        [Test]
        public void TestImplicitConversion()
        {
            string initialString = "Hello, world!";
            dynamic proxy = new DynamicRemoteCharStar(null, 0x100, initialString);

            // Implicit conversion from DynamicRemoteCharStar to string
            string convertedString = proxy;
            Assert.That(convertedString, Is.EqualTo("Hello, world!"));
        }

        [Test]
        public void TestExplicitConversion()
        {
            string initialString = "Hello, world!";
            dynamic proxy = new DynamicRemoteCharStar(null, 0x100, initialString);

            // Explicit conversion from DynamicRemoteCharStar to string
            string convertedString = (string)proxy;
            Assert.That(convertedString, Is.EqualTo("Hello, world!"));
        }

        [Test]
        public void TestToStringConversion()
        {
            string initialString = "Hello, world!";
            dynamic proxy = new DynamicRemoteCharStar(null, 0x100, initialString);

            // Explicit conversion from DynamicRemoteCharStar to string
            string convertedString = proxy.ToString();
            Assert.That(convertedString, Is.EqualTo("Hello, world!"));
        }
    }
}