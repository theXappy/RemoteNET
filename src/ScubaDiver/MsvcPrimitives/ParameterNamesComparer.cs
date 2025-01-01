using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ScubaDiver
{
    internal class ParameterNamesComparer : IEqualityComparer<string>
    {
        public bool Equals(string x, string y)
        {
            // first compare including namespaces (if exists)
            if (x == y)
                return true;
            // second compare without namespaces
            string xNoNamespace = x.Split('!').Last();
            string yNoNamespace = y.Split('!').Last();
            if (xNoNamespace == yNoNamespace)
                return true;
            // Check if one is pointer and the other isn't, and they're not primitve
            {
                bool xIsPointer = xNoNamespace.EndsWith('*');
                bool yIsPointer = yNoNamespace.EndsWith('*');
                if (xIsPointer && !yIsPointer)
                {
                    string xWithoutAsterisk = xNoNamespace.Substring(0, xNoNamespace.Length - 1);
                    return xWithoutAsterisk == yNoNamespace;
                }
                else if (!xIsPointer && yIsPointer)
                {
                    string yWithoutAsterisk = yNoNamespace.Substring(0, yNoNamespace.Length - 1);
                    return xNoNamespace == yWithoutAsterisk;
                }
            }
            return false;
        }

        public int GetHashCode([DisallowNull] string obj)
        {
            string objNoNamespace = obj.Split('!').Last();
            if (objNoNamespace[^1] == '*')
                objNoNamespace = objNoNamespace.Substring(0, objNoNamespace.Length - 1);
            return objNoNamespace.GetHashCode();
        }
    }
}