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
        public void GetIntMapData()
        {
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

        [Test]
        public void RegisterUnlockCallback()
        {
            MsMangledNameParser parser = new MsMangledNameParser("?RegisterUnlockCallback@BodyTextManager@SPen@@QEAAX_JV?$function@$$A6AXXZ@std@@@Z");
            var (funcName, funcSig, enclosingType) = parser.Parse();
            if (funcSig is Reko.Core.Serialization.SerializedSignature sig)
            {
                Console.WriteLine(sig.Convention);
                Console.WriteLine(sig.IsInstanceMethod);
                Console.WriteLine(sig.ReturnValue);
                Console.WriteLine(sig.Arguments);
            }

            var x = TypesRestarizer.RestarizeParameters(
                "?RegisterUnlockCallback@BodyTextManager@SPen@@QEAAX_JV?$function@$$A6AXXZ@std@@@Z");

            Assert.Pass();
        }
    }
}