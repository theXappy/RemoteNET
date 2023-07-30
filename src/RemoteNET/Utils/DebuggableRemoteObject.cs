using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using RemoteNET.Internal;

namespace RemoteNET.Utils
{
    public class DebuggableRemoteObject
    {
        private DynamicRemoteObject _dro;

        public Dictionary<string, object> Fields => GetMembersValues(_dro.GetType().GetFields());
        public Dictionary<string, object> Properties => GetMembersValues(_dro.GetType().GetProperties());
        public Dictionary<string, object> Methods => _dro.GetType().GetMethods().ToDictionary(mi => mi.Name, mi => (object)mi);

        public DebuggableRemoteObject(DynamicRemoteObject dro)
        {
            _dro = dro;
        }

        private Dictionary<string, object> GetMembersValues(IEnumerable<MemberInfo> members)
        {
            Dictionary<string, object> output =new Dictionary<string, object>();
            foreach (var member in members)
            {
                try
                {
                    if (DynamicRemoteObject.TryGetDynamicMember(_dro, member.Name, out object value))
                        output[member.Name] = value;
                    else
                        output[member.Name] = new Exception("TryGetDynamicMember failed :(");
                }
                catch(Exception ex)
                {
                        output[member.Name] = new Exception("TryGetDynamicMember failed EXCEPTION thrown :( ex: " + ex);
                }
            }
            return output;
        }
    }

    public static class DebuggableRemoteObjectExt
    {
        public static DebuggableRemoteObject AsDebuggable(this DynamicRemoteObject dro) =>
            new DebuggableRemoteObject(dro);
        public static DebuggableRemoteObject AsDebuggable(this ManagedRemoteObject ro) =>
            new DebuggableRemoteObject(ro.Dynamify());
    }
}
