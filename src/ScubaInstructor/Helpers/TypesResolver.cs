using System;
using System.Net.Http;
using ScubaDiver;
using ScubaDiver.API;

namespace ScubaInstructor.Helpers
{
    public class TypesResolver
    {
        public static TypesResolver Instance = new TypesResolver();

        private TypesResolver() { }

        public TypeDump Resolve(string type, string assembly = null)
        {
            TypeDump dump = null;
            string name = type;
            if (name == null)
            {
                return null;
            }
            HttpClient c = new HttpClient();
            string url = $"http://127.0.0.1:9977/type?name={name}" +
                         (assembly != null ? $"&assembly={assembly}" : String.Empty);
            HttpResponseMessage res = c.GetAsync(url).Result;
            var body = res.Content.ReadAsStringAsync().Result;
            try
            {
                dump = System.Text.Json.JsonSerializer.Deserialize<TypeDump>(body);
                return dump;
            }
            catch
            {
                return null;
            }
        }

    }
}