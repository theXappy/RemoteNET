using System;
using System.Reflection;

namespace RemoteNET.Internal.Reflection
{
    public class RemoteAssemblyDummy : Assembly
    {
        AssemblyName _name;
        public RemoteAssemblyDummy(string assemblyName)
        {
            _name = new AssemblyName(assemblyName);
        }
        public override string FullName => throw new Exception($"You tried to get the 'FullName' property on a {nameof(RemoteAssemblyDummy)}." +
                                                               $"Currently, this is forbidden to reduce confusion between 'full name' and 'short name'. You shoudl call 'GetName().Name' instead.");

        public override AssemblyName GetName() => _name;
    }
}