using System;
using System.Reflection;

namespace RemoteNET.Internal.Reflection
{
    /// <summary>
    /// A parameter of a remote method. The parameter's type itself might be a remote type (but can also be local)
    /// </summary>
    public class RemoteParameterInfo : ParameterInfo
    {

        private Lazy<Type> _paramType;
        public override Type ParameterType => _paramType.Value;

        // TODO: Type needs to be converted to a remote type ?
        public RemoteParameterInfo(ParameterInfo pi) : this(pi.Name, new Lazy<Type>(()=> pi.ParameterType))
        {
        }

        public RemoteParameterInfo(string name, Lazy<Type> paramType)
        {
            this.NameImpl = name;
            _paramType = paramType;
        }
    }
}