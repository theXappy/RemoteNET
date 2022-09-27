using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ScubaDiver.API
{
    public static class JsonConvert
    {
        public static T DeserializeObject<T>(string body, object _withErrors = null) where T : class
        {
            Type convert = NewtonsoftProxy.JsonConvert;

            List<object> args = new List<object>();
            args.Add(body);
            if(_withErrors != null)
                args.Add(_withErrors);

            var DeserializeObject = convert.GetMethods((BindingFlags)0xffff)
                .Where(mi=>mi.Name=="DeserializeObject")
                .Where(mi=>mi.IsGenericMethod)
                .Where(mi=>mi.GetParameters().Length == args.Count)
                .Where(mi=>!mi.GetParameters().Last().ParameterType.IsArray)
                .Single();

            try
            {
                var x = DeserializeObject.MakeGenericMethod(typeof(T)).Invoke(null, args.ToArray());
                return (T)x;
            }
            catch(TargetInvocationException ex)
            {
                throw ex.InnerException;
            }
        }

        public static string SerializeObject(object o)
        {
            Type convert = NewtonsoftProxy.JsonConvert;

            List<object> args = new List<object>();
            args.Add(o);

            var SerializeObject = convert.GetMethods((BindingFlags)0xffff)
                .Where(mi => mi.Name == "SerializeObject")
                .Where(mi => mi.GetParameters().Length == 1)
                .Single();

            try
            {
                var x = SerializeObject.Invoke(null, args.ToArray());
                return (string)x;
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException;
            }
        }
    }


    public static class NewtonsoftProxy
    {
     
        private static Assembly _newtonsoft;

        public static void Init(Assembly assembly)
        {
            _newtonsoft = assembly;
        }

        private static void AutoInit()
        {
            if(_newtonsoft == null)
                _newtonsoft = AppDomain.CurrentDomain.Load(new AssemblyName("Newtonsoft.Json"));
        }

        public static object JsonSerializerSettingsWithErrors
        {
            get
            {
                AutoInit();
                //JsonSerializerSettings _withErrors = new()
                //{
                //    MissingMemberHandling = MissingMemberHandling.Error
                //};
                Type MissingMemberHandlingEnum = _newtonsoft.GetType("Newtonsoft.Json.MissingMemberHandling");
                var MissingMemberHandling_Error = Enum.GetValues(MissingMemberHandlingEnum).Cast<object>()
                    .Single(val => val.ToString() == "Error");

                Type t = _newtonsoft.GetType("Newtonsoft.Json.JsonSerializerSettings");
                var inst = Activator.CreateInstance(t);
                t.GetProperty("MissingMemberHandling").SetValue(inst, MissingMemberHandling_Error);

                return inst;
            }
        }

        public static Type JsonConvert
        {
            get
            {
                AutoInit();
                return _newtonsoft.GetType("Newtonsoft.Json.JsonConvert");
            }
        }
    }
}