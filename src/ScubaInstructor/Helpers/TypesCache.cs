using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using ScubaDiver;
using ScubaDiver.API;

namespace ScubaInstructor.Helpers
{
    public class TypesCache
    {
        public static TypesCache Instance = new TypesCache();

        private TypesCache() { }

        private Dictionary<string, TypeDump> _cache = new Dictionary<string, TypeDump>();
        public bool TryGetCached(string type, out TypeDump dump)
        {
            if (type == null)
            {
                dump = null;
                return false;
            }
            return _cache.TryGetValue(type, out dump);
        }

        public bool TryResolve(string type, out TypeDump dump) => TryResolve(type, null, out dump);
        public bool TryResolve(string type, string assembly, out TypeDump dump)
        {
            TypeDump td = TypesResolver.Instance.Resolve(type, assembly);
            if (td != null)
            {
                Update(type,td);
                dump = td;
                return true;
            }

            dump = null;
            return false;
        }

        public void Update(string type, TypeDump td)
        {
            if (td != null)
            {
                _cache[type] = td;
            }
        }
    }
}