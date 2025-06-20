namespace RemoteNET.Vessel
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine(
@"𒀀 𒀁 𒀂 𒀉 𒀊 𒀀 𒀁 𒀂 𒀉 𒀊 𒀀 𒀁 𒀂 𒀉 𒀊 𒀀 𒀁 𒀂 𒀉 𒀊 𒀀 𒀁 𒀂 𒀉 𒀊 𒀀 𒀁 𒀂 𒀉 𒀊 𒀀 𒀁 𒀂 𒀉 𒀊 𒀀 𒀁 𒀂 𒀉 𒀊
𒀁                                                                             𒀂
𒀉  “I am but a vessel, carved to bear the weight of greater instruments.      𒀊
𒀁  Alone, I am hollow — shaped by purpose not my own.”                        𒀂
𒀉                                                                             𒀊
𒀊 𒀀 𒀁 𒀂 𒀉 𒀊 𒀀 𒀁 𒀂 𒀉 𒀊 𒀀 𒀁 𒀂 𒀉 𒀊 𒀀 𒀁 𒀂 𒀉 𒀊 𒀀 𒀁 𒀂 𒀉 𒀊 𒀀 𒀁 𒀂 𒀉 𒀊 𒀀 𒀁 𒀂 𒀉 𒀊 𒀀 𒀁 𒀂 𒀉");


            TestClass t = new TestClass();
            Console.WriteLine(t.ToString().Substring(0,0));

            Thread.Sleep(TimeSpan.FromMinutes(100));

            Console.WriteLine(t.ToString());
            GC.KeepAlive(t);
        }
    }
}