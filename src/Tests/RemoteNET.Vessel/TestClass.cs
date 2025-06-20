using System.Text;

namespace RemoteNET.Vessel
{
    public class TestClass
    {
        public int TestField1 = 5;
        public int TestProp1 => 6;

        public int TestProp2
        {
            get;
            set;
        } = 7;

        public int TestMethod1()
        {
            return 8;
        }
        public int TestMethod2(int i)
        {
            return i + 9;
        }

        public void TestMethod3(StringBuilder sb)
        {
            sb.Append(10);
        }
    }
}