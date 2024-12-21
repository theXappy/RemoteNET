using System;

namespace RemoteNET
{
    public abstract class RemoteActivator
    {
        public abstract RemoteObject CreateInstance(string assembly, string typeFullName, params object[] parameters);

        public virtual RemoteObject CreateInstance(Type t)
            => CreateInstance(t, []);
        public virtual RemoteObject CreateInstance(Type t, params object[] parameters)
            => CreateInstance(t.Assembly.FullName, t.FullName, parameters);
        public virtual RemoteObject CreateInstance(string typeFullName, params object[] parameters)
            => CreateInstance(null, typeFullName, parameters);
        public virtual RemoteObject CreateInstance<T>() 
            => CreateInstance(typeof(T));
        public virtual RemoteObject CreateInstance<T>(params object[] parameters) 
            => CreateInstance(typeof(T), parameters);
    }
}