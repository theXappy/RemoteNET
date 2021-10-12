using System;
using System.Reflection;

namespace RemoteObject.Internal.Reflection
{
    /// <summary>
    /// A parameter of a remote method. The parameter's type itself might be a remote type (but can also be local)
    /// </summary>
    public class RemoteParameterInfo : ParameterInfo
    {
        public RemoteParameterInfo(string name, Type t)
        {
            this.NameImpl = name;
            this.ClassImpl = t;
        }
    }
}