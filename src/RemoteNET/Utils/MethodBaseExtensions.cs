using System.Reflection;

namespace RemoteNET.Utils
{
    public static class MethodBaseExtensions
    {
        public static bool SignatureEquals(this MethodBase a, MethodBase b)
        {
            if (a.Name != b.Name)
                return false;
            if (!ParametersEqual(a.GetParameters(), b.GetParameters()))
                return false;

            if((a is MethodInfo aInfo) && (b is MethodInfo bInfo)
               && aInfo.ReturnType != null && bInfo.ReturnType != null)
            {
                return aInfo.ReturnType.FullName == bInfo.ReturnType.FullName;
            }
            else if((a is ConstructorInfo aCtro) && (b is ConstructorInfo bCtor))
            {
                // The "Return" type of a ctor is it's defining type...
                return aCtro.DeclaringType == bCtor.DeclaringType;
            }
            else
            {
                // Unknown derived class of MethodBase
                return false;
            }
        }

        public static bool ParametersEqual(ParameterInfo[] a, ParameterInfo[] b)
        {
            if(a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i].ParameterType != b[i].ParameterType)
                    return false;
            return true;
        }
    }
}
