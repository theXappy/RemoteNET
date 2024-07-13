using TestTarget;
using System.Security.AccessControl;

namespace TestTarget
{
    internal class Program
    {
        static void Main(string[] args)
        {
            TestClass t = new TestClass();
            Console.WriteLine(t.ToString());

            Thread.Sleep(TimeSpan.FromMinutes(100));

            Console.WriteLine(t.ToString());
            GC.KeepAlive(t);
        }
    }
}