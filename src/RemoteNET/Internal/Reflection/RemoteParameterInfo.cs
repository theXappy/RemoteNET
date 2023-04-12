using System;
using System.Reflection;

namespace RemoteNET.Internal.Reflection
{
    /// <summary>
    /// A parameter of a remote method. The parameter's type itself might be a remote type (but can also be local)
    /// </summary>
    public class RemoteParameterInfo : ParameterInfo
    {
        private LazyRemoteTypeResolver _paramType;
        public override Type ParameterType => _paramType.Value;

        // TODO: Type needs to be converted to a remote type ?
        public RemoteParameterInfo(ParameterInfo pi) : this(pi.Name, new LazyRemoteTypeResolver(pi.ParameterType))
        {
        }

        public RemoteParameterInfo(string name, LazyRemoteTypeResolver paramType)
        {
            this.NameImpl = name;
            _paramType = paramType;
        }

        public override string ToString() => $"{_paramType.TypeFullName ?? _paramType.TypeName} {Name}";
    }
}