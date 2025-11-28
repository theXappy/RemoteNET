using Microsoft.Diagnostics.Runtime.AbstractDac;
using ScubaDiver.Rtti;
using System;

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
            ModuleInfo module,
            ulong offset,
            string functionName,
            string returnType,
            string[] argTypes)
            : base(functionName, functionName, functionName, argTypes?.Length)
        {
            _module = module;
            
            // Check for potential overflow when adding baseAddress and offset
            checked
            {
                _address =  module.BaseAddress + (nuint)offset;
            }
            
            _retType = returnType;
            _argTypes = argTypes ?? Array.Empty<string>();
        }

        public override ModuleInfo Module => _module;
        public override nuint Address => _address;
        public override string RetType => _retType;
        public override string[] ArgTypes => _argTypes;
    }
}
