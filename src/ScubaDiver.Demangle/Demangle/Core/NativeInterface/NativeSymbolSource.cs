using System.Runtime.InteropServices;
using ScubaDiver.Demangle.Demangle.Core.Loading;
using ScubaDiver.Demangle.Demangle.Core.NativeInterface.Interfaces;
using ScubaDiver.Demangle.Demangle.Core.Types;

namespace ScubaDiver.Demangle.Demangle.Core.NativeInterface
{
	public class NativeSymbolSource : ISymbolSource
	{
        private IProcessorArchitecture arch;
		private IntPtr handle;
		private NativeSymbolSourceProvider prv;
		private readonly ILibraryLoader loader;

		private const string SYM_NAME = "gSymProvider";

		public NativeSymbolSource(IProcessorArchitecture arch, string libPath, ILibraryLoader ldr)
		{
            this.arch = arch;
			loader = ldr;
			handle = loader.LoadLibrary(libPath);

			IntPtr gSymProvider = loader.GetSymbol(handle, SYM_NAME);
			prv = (NativeSymbolSourceProvider)Marshal.PtrToStructure(gSymProvider, typeof(NativeSymbolSourceProvider))!;
		}

		public bool CanLoad(string filename, byte[] fileContents)
		{
			return false;
		}

		public void Dispose()
		{
			prv.Dispose();
		}

		public List<ImageSymbol> GetAllSymbols()
		{
			//TODO: ask if number of symbols is available and preallocate list
			List<ImageSymbol> symbols = new List<ImageSymbol>();

			IntPtr syms = prv.GetSymbols();
			IntPtr sym;
			for(int off=0; ; off += Marshal.SizeOf(sym)) {
				sym = Marshal.ReadIntPtr(syms, off);
				if (sym == IntPtr.Zero)
					break;

				ulong start = prv.GetSymbolStart(sym);
				ulong end = prv.GetSymbolEnd(sym);

                symbols.Add(ImageSymbol.Create(
                    SymbolType.Unknown,
                    arch,
                    Address.Ptr32((uint)start),
                    name: prv.GetSymbolName(sym),
                    dataType: new UnknownType((int)(end - start))));
			}

			return symbols;
		}
	}
}
