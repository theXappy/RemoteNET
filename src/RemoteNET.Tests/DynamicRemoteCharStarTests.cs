using RemoteNET;
using System.Globalization;

[TestFixture]
public class DynamicRemoteCharStarTests
{
    [Test]
    public void TestPropertyAccess()
    {
        string initialString = "Hello, world!";
        dynamic proxy = new DynamicRemoteCharStar(null, 0x100, initialString);

        // Test property access using dynamic proxy
        Assert.AreEqual(13, proxy.Length);
        Assert.AreEqual("Hello", proxy.Substring(0, 5));
    }

    [Test]
    public void TestPropertyModification()
    {
        string initialString = "Hello, world!";
        dynamic proxy = new DynamicRemoteCharStar(null, 0x100, initialString);

        // Modify property using dynamic proxy
        proxy = proxy.Replace("world", "C#");
        Assert.AreEqual("Hello, C#!", proxy);
    }

    [Test]
    public void TestMethodInvocation()
    {
        string initialString = "Hello, world!";
        dynamic proxy = new DynamicRemoteCharStar(null, 0x100, initialString);

        // Invoke method using dynamic proxy
        proxy = proxy.ToUpper();
        Assert.AreEqual("HELLO, WORLD!", proxy);
    }

    [Test]
    public void TestSubclassParameter()
    {
        string initialString = "Hello, world!";
        dynamic proxy = new DynamicRemoteCharStar(null, 0x100, initialString);

        // Invoke ToString method with a string parameter
        string formattedString = proxy.ToString(CultureInfo.InvariantCulture);
        Assert.AreEqual("Hello, world!", formattedString);

        // Invoke ToString method with an IFormatProvider (a subclass of object) as a parameter
        IFormatProvider formatProvider = CultureInfo.InvariantCulture;
        string formattedStringWithProvider = proxy.ToString(formatProvider);
        Assert.AreEqual("Hello, world!", formattedStringWithProvider);
    }

    [Test]
    public void TestImplicitConversion()
    {
        string initialString = "Hello, world!";
        dynamic proxy = new DynamicRemoteCharStar(null, 0x100, initialString);

        // Implicit conversion from DynamicRemoteCharStar to string
        string convertedString = proxy;
        Assert.AreEqual("Hello, world!", convertedString);
    }

    [Test]
    public void TestExplicitConversion()
    {
        string initialString = "Hello, world!";
        dynamic proxy = new DynamicRemoteCharStar(null, 0x100, initialString);

        // Explicit conversion from DynamicRemoteCharStar to string
        string convertedString = (string)proxy;
        Assert.AreEqual("Hello, world!", convertedString);
    }

    [Test]
    public void TestToStringConversion()
    {
        string initialString = "Hello, world!";
        dynamic proxy = new DynamicRemoteCharStar(null, 0x100, initialString);

        // Explicit conversion from DynamicRemoteCharStar to string
        string convertedString = proxy.ToString();
        Assert.AreEqual("Hello, world!", convertedString);
    }
}