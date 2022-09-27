using System;
using System.Collections.Generic;
using System.Text;

namespace ScubaDiver.API.Utils
{
    public class Filter
    {
        public static Predicate<string> CreatePredicate(string filter)
        {
            Predicate<string> matchesFilter = (testee) => true;
            if (filter != null)
            {
                string noStartsFilter = filter.Trim('*');
                // User specified a filter. Looking for wild cards
                if (filter.StartsWith("*"))
                {
                    if (filter.EndsWith("*"))
                    {
                        // Filter of format "*phrase*", looking anywhere inside the type name
                        matchesFilter = (typeName) => typeName.Contains(noStartsFilter);
                    }
                    else
                    {
                        // Filter of format "*phrase", looking for specific suffix
                        matchesFilter = (typeName) => typeName.EndsWith(noStartsFilter);
                    }
                }
                else
                {
                    if (filter.EndsWith("*"))
                    {
                        // Filter of format "phrase*", looking for specific prefix
                        matchesFilter = (typeName) => typeName.StartsWith(noStartsFilter);
                    }
                    else
                    {
                        // Filter has no wildcards - looking for specific type
                        matchesFilter = (typeName) => typeName == filter;
                    }
                }
            }

            return matchesFilter;
        }
    }
}
