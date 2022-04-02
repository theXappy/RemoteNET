using System;
using System.Reflection;

namespace RemoteNET.Internal.Reflection
{
    /// <summary>
    /// A parameter of a remote method. The parameter's type itself might be a remote type (but can also be local)
    /// </summary>
    public class RemoteParameterInfo : ParameterInfo
    {
        // TODO: Type needs to be converted to a remote type ?
        public RemoteParameterInfo(ParameterInfo pi) : this(pi.Name, pi.ParameterType)
        {
        }

        public RemoteParameterInfo(string name, Type t)
        {
            this.NameImpl = name;
            this.ClassImpl = t;
        }
    }
}