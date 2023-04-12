using System;
using System.Collections.Generic;
using System.Text;

namespace RemoteNET.Internal.Reflection
{
    public class LazyRemoteTypeResolver
    {
        private Lazy<Type> _factory;
        private string _beforeDumpingTypeName;
        private string _beforeDumpingFullTypeName;
        private string _beforeDumpingAssemblyName;
        private Type _resolved;

        public string Assembly => _resolved?.Assembly?.FullName ?? _beforeDumpingAssemblyName;
        public string TypeFullName => _resolved?.FullName ?? _beforeDumpingFullTypeName;
        public string TypeName => _resolved?.Name ?? _beforeDumpingTypeName;

        public Type Value
        {
            get
            {
                _resolved ??= _factory.Value;
                return _resolved;
            }
        }

        public LazyRemoteTypeResolver(Lazy<Type> factory, string assembly, string fullTypeFullName, string typeName)
        {
            _factory = factory;
            _beforeDumpingAssemblyName = assembly;
            _beforeDumpingFullTypeName = fullTypeFullName;
            _beforeDumpingTypeName = typeName;
        }

        public LazyRemoteTypeResolver(Type resolved)
        {
            _resolved = resolved;
        }
    }
}
