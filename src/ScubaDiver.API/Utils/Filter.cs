using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ScubaDiver.API.Utils
{
    public class Filter
    {
        /// <summary>
        /// Turn a "simple filter string" into a regex
        /// </summary>
        /// <param name="simpleFilter">A string that only allow '*' as a wild card meaning "0 or more characters"</param>
        private static Regex SimpleFilterToRegex(string simpleFilter)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("^"); // Begining of string
            foreach (char c in simpleFilter)
            {
                if (c == '*')
                {
                    sb.Append(".*");
                }
                else
                {
                    string asEscaped = Regex.Escape(c.ToString());
                    sb.Append(asEscaped);
                }
            }
            sb.Append("$"); // End of string
            return new Regex(sb.ToString());
        }

        public static Predicate<string> CreatePredicate(string filter)
        {
            Predicate<string> matchesFilter = (testee) => true;
            if (filter != null)
            {
                Regex r = SimpleFilterToRegex(filter);
                string noStartsFilter = filter.Trim('*');
                // Filter has no wildcards - looking for specific type
                matchesFilter = r.IsMatch;
            }
            return matchesFilter;
        }
    }
}
