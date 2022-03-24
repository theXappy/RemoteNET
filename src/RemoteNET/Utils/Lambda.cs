using System;

namespace RemoteNET.Utils
{
    public static class Lambda
    {
        public static object __(Action a) => a;
        public static object __(Action<dynamic> a) => a;
        public static object __(Action<dynamic, dynamic> a) => a;
        public static object __(Action<dynamic, dynamic, dynamic> a) => a;
        public static object __(Action<dynamic, dynamic, dynamic, dynamic> a) => a;
        public static object __(Action<dynamic, dynamic, dynamic, dynamic, dynamic> a) => a;
        public static object __(Action<dynamic, dynamic, dynamic, dynamic, dynamic, dynamic> a) => a;
        public static object __(Action<dynamic, dynamic, dynamic, dynamic, dynamic, dynamic, dynamic> a) => a;
        public static object __(Action<dynamic, dynamic, dynamic, dynamic, dynamic, dynamic, dynamic, dynamic> a) => a;
        public static object __(Action<dynamic, dynamic, dynamic, dynamic, dynamic, dynamic, dynamic, dynamic, dynamic> a) => a;
        public static object __(Action<dynamic, dynamic, dynamic, dynamic, dynamic, dynamic, dynamic, dynamic, dynamic, dynamic> a) => a;
    }
}
