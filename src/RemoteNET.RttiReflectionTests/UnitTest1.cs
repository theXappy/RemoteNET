using Reko.Environments.Windows;
using RemoteNET.RttiReflection.Demangle;

namespace RemoteNET.RttiReflectionTests
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void Test1()
        {
            //string input = "??_7Bundle@SPen@@6B@";
            //var parameters = TypesRestarizer.RestarizeParameters(input);
            MsMangledNameParser parser = new MsMangledNameParser("?GetIntMapData@Bundle@SPen@@QEAA?AV?$map@V?$basic_string@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@HU?$less@V?$basic_string@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@@2@V?$allocator@U?$pair@$$CBV?$basic_string@DU?$char_traits@D@std@@V?$allocator@D@2@@std@@H@std@@@2@@std@@XZ");
            var (funcName, funcSig, enclosingType) = parser.Parse();
            if (funcSig is Reko.Core.Serialization.SerializedSignature sig)
            {
                Console.WriteLine(sig.Convention);
                Console.WriteLine(sig.IsInstanceMethod);
                Console.WriteLine(sig.ReturnValue);
                Console.WriteLine(sig.Arguments);
            }

            Assert.Pass();
        }
    }
}