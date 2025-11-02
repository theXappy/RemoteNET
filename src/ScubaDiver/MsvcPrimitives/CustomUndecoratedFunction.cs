using ScubaDiver.Rtti;

namespace ScubaDiver
{
    /// <summary>
    /// Represents a custom user-defined function that can be registered on a type
    /// </summary>
    public class CustomUndecoratedFunction : UndecoratedFunction
    {
        private readonly ModuleInfo _module;
        private readonly nuint _address;
        private readonly string _retType;
        private readonly string[] _argTypes;

        public CustomUndecoratedFunction(
            string moduleName,
            nuint baseAddress,
            ulong offset,
            string functionName,
            string returnType,
            string[] argTypes)
            : base(functionName, functionName, functionName, argTypes?.Length)
        {
            _module = new ModuleInfo { Name = moduleName, BaseAddress = baseAddress };
            _address = baseAddress + offset;
            _retType = returnType;
            _argTypes = argTypes ?? new string[0];
        }

        public override ModuleInfo Module => _module;
        public override nuint Address => _address;
        public override string RetType => _retType;
        public override string[] ArgTypes => _argTypes;
    }
}
