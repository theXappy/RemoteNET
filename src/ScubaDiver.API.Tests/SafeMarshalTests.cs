using ScubaDiver.API.Memory;

[TestFixture]
public class SafeMarshalTests
{
    [Test]
    public void AllocHGlobal_ValidSize_ReturnsValidIntPtr()
    {
        int size = 100;
        IntPtr result = SafeMarshal.AllocHGlobal(size);

        Assert.AreNotEqual(IntPtr.Zero, result);
        SafeMarshal.FreeHGlobal(result); // Cleanup
    }

    [Test]
    public void FreeHGlobal_ValidIntPtr_NoExceptionThrown()
    {
        IntPtr hglobal = SafeMarshal.AllocHGlobal(100);

        Assert.DoesNotThrow(() => SafeMarshal.FreeHGlobal(hglobal));
    }

    [Test]
    public void Copy_ByteArrayToIntPtr_ValidArguments_NoExceptionThrown()
    {
        IntPtr destination = SafeMarshal.AllocHGlobal(100);
        byte[] source = new byte[] { 1, 2, 3, 4 };
        int startIndex = 0;
        int length = source.Length;

        Assert.DoesNotThrow(() => SafeMarshal.Copy(source, startIndex, destination, length));
        SafeMarshal.FreeHGlobal(destination); // Cleanup
    }

    [Test]
    public void Copy_IntPtrToByteArray_ValidArguments_NoExceptionThrown()
    {
        IntPtr source = SafeMarshal.AllocHGlobal(100);
        byte[] destination = new byte[4];
        int startIndex = 0;
        int length = destination.Length;

        Assert.DoesNotThrow(() => SafeMarshal.Copy(source, destination, startIndex, length));
        SafeMarshal.FreeHGlobal(source); // Cleanup
    }

    [Test]
    public void PtrToStringAnsi_ValidIntPtr_ReturnsValidString()
    {
        IntPtr ptr = SafeMarshal.AllocHGlobal(100);

        Assert.DoesNotThrow(() => SafeMarshal.PtrToStringAnsi(ptr));
        SafeMarshal.FreeHGlobal(ptr); // Cleanup
    }


    [TestCase(0)] // Test with IntPtr.Zero
    [TestCase(unchecked((long)0xFFEE000000000000))] // Test with a very high address
    public void Copy_ByteArrayToIntPtr_InvalidArguments_ThrowsException(long invalidAddressValue)
    {
        IntPtr invalidAddress = new IntPtr(invalidAddressValue);
        byte[] source = new byte[] { 1, 2, 3, 4 };
        int startIndex = 0;
        int length = source.Length;

        Assert.Throws<InvalidOperationException>(() => SafeMarshal.Copy(source, startIndex, invalidAddress, length));
    }

    [TestCase(0)] // Test with IntPtr.Zero
    [TestCase(unchecked((long)0xFFEE000000000000))] // Test with a very high address
    public void Copy_IntPtrToByteArray_InvalidArguments_ThrowsException(long invalidAddressValue)
    {
        IntPtr invalidAddress = new IntPtr(invalidAddressValue);
        byte[] destination = new byte[4];
        int startIndex = 0;
        int length = destination.Length;

        Assert.Throws<InvalidOperationException>(() => SafeMarshal.Copy(invalidAddress, destination, startIndex, length));
    }

    [TestCase(0)] // Test with IntPtr.Zero
    [TestCase(unchecked((long)0xFFEE000000000000))] // Test with a very high address
    public void PtrToStringAnsi_InvalidIntPtr_ThrowsException(long invalidAddressValue)
    {
        IntPtr invalidAddress = new IntPtr(invalidAddressValue);
        Assert.Throws<InvalidOperationException>(() => SafeMarshal.PtrToStringAnsi(invalidAddress));
    }
}