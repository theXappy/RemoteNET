using System;
using System.Reflection;

namespace RemoteNET.Internal.Reflection.Rtti
{
    public class RemoteRttiMethodTableInfo : MemberInfo
    {
        public long StartAddress { get; private set; }

        /// <param name="declaringType">Containing type</param>
        /// <param name="name">Name of the method table (including the specific parent's name, if applicable)</param>
        /// <param name="startAddress">Address of the first entry in the Method Table.</param>
        public RemoteRttiMethodTableInfo(Type declaringType, string name, string mangledName, long startAddress)
        {
            DeclaringType = declaringType;
            Name = name;
            MangledName = mangledName;
            StartAddress = startAddress;
        }

        public override Type DeclaringType { get; }

        public override MemberTypes MemberType => MemberTypes.Custom; // .NET doesn't have one for "Method Table"

        public override string Name { get; }
        public string MangledName { get; }

        public override Type ReflectedType => typeof(nuint);

        public override object[] GetCustomAttributes(bool inherit) => Array.Empty<object>();

        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => Array.Empty<object>();

        public override bool IsDefined(Type attributeType, bool inherit) => false;

        public override string ToString() => "Remote RTTI Method Table: " + Name;
    }
}
