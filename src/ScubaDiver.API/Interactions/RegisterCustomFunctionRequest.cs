using System.Collections.Generic;

namespace ScubaDiver.API.Interactions
{
    /// <summary>
    /// Request to register a custom function on a type (primarily for unmanaged targets)
    /// </summary>
    public class RegisterCustomFunctionRequest
    {
        /// <summary>
        /// Full type name of the parent type that this function belongs to
        /// </summary>
        public string ParentTypeFullName { get; set; }

        /// <summary>
        /// Assembly name of the parent type
        /// </summary>
        public string ParentAssembly { get; set; }

        /// <summary>
        /// Name of the function to register
        /// </summary>
        public string FunctionName { get; set; }

        /// <summary>
        /// Module name where the function is located (e.g., "MyModule.dll")
        /// </summary>
        public string ModuleName { get; set; }

        /// <summary>
        /// Offset within the module where the function is located
        /// </summary>
        public ulong Offset { get; set; }

        /// <summary>
        /// Full type name of the return type
        /// </summary>
        public string ReturnTypeFullName { get; set; }

        /// <summary>
        /// Assembly of the return type
        /// </summary>
        public string ReturnTypeAssembly { get; set; }

        /// <summary>
        /// List of parameter types (full type names)
        /// </summary>
        public List<ParameterTypeInfo> Parameters { get; set; }

        public class ParameterTypeInfo
        {
            public string Name { get; set; }
            public string TypeFullName { get; set; }
            public string Assembly { get; set; }
        }
    }
}
