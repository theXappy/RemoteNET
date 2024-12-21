using System;
using System.Linq;
using System.Reflection;
using RemoteNET.RttiReflection;

namespace RemoteNET
{
    public class UnmanagedRemoteActivator : RemoteActivator
    {
        private UnmanagedRemoteApp _app;
        public UnmanagedRemoteActivator(UnmanagedRemoteApp app)
        {
            _app = app;
        }
        public override RemoteObject CreateInstance(Type t, params object[] parameters)
            => CreateInstance(t.Assembly.GetName().Name, t.FullName, parameters);

        public override UnmanagedRemoteObject CreateInstance(string assembly, string typeFullName, params object[] parameters)
        {
            Type type = _app.GetRemoteType(typeFullName, assembly);

            // 
            // Find ctor
            // 
            ConstructorInfo[] ctors = type.GetConstructors();
            ConstructorInfo[] ctorsWithRightParamsCount = ctors.Where(ctor => ctor.GetParameters().Length == parameters.Length).ToArray();
            if (!ctorsWithRightParamsCount.Any())
                throw new Exception("Could not find constructor with {parameters.Length} parameters");
            if (ctorsWithRightParamsCount.Length > 1)
                throw new Exception("Multiple constructors with {parameters.Length} parameters. Can't narrow down right now.");

            ConstructorInfo ctor = ctorsWithRightParamsCount.Single();

            //
            // Allocate
            //
            nint buf = _app.Marshal.AllocHGlobalZero(100); // TODO: Not a random size
            object results = ctor.Invoke(buf, []);

            if (results is DynamicUnmanagedRemoteObject dro)
                return dro.__ro as UnmanagedRemoteObject;
            throw new Exception("Invoked remote ctor but the results wasn't a DRO");
        }
    }
}
