namespace ScubaDiver.Demangle.Demangle.Core.NativeInterface
{
	public interface ILibraryLoader
	{
		IntPtr LoadLibrary(string libPath);
		int Unload(IntPtr handle);
		IntPtr GetSymbol(IntPtr handle, string symName);
    }
}
