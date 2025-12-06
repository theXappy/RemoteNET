namespace RemoteNET.Vessel
{
    public  class VesselProgram
    {
        public static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine(
@"
◰ ◲ ◱ ◳ ◰ ◲ ◱ ◳ ◰ ◲ ◱ ◳
◳⠀⠀⠀⠀⠀⠀⠀  ⣀⣀⣀⡀⠀⠀ ⠀⠀⠀⠀⠀◳
◲⠀⠀⠀⠀⠀⠀⠀⠀⣠⣤⣤⣤⡄⢀⡀⠀⠀⠀⠀ ⠀◲
◱⠀⠀⠀⠀⠀⠀⠀⠀⢹⣿⣿⣿⠀⠈⠙⢷⣄⠀⠀⠀⠀◱
◰⠀⠀⠀⠀⠀⠀⠀⠀⢸⣿⣿⣿⠀⠀⠀⠀⢻⡆⠀⠀⠀◰
◳⠀⠀⠀⠀⠀⠀⠀⠀⣼⣿⣿⣿⡄⠀⠀⠀⢸⡇⠀⠀⠀◳
◲⠀⠀⠀⠀⠀⠀⠀⢠⣿⣿⣿⣿⣷⡀⠀⠀⣿⠃⠀⠀⠀◲
◱⠀⠀⠀⠀⠀⠀⣠⣿⣿⣿⣿⣿⣿⣷⡄⢸⠏⠀⠀⠀⠀◱
◰⠀⠀⠀⠀⠀⣰⣿⣿⣿⣿⣿⣿⣿⣿⣿⡄⠀⠀⠀⠀⠀◰
◳⠀⠀⠀⠀⢰⣿⣿⠉⣿⣿⣿⣿⠉⣿⣿⣧⠀⠀⠀⠀⠀◳
◲⠀⠀⠀⠀⢸⣿⣿ ⣿⣿⣿⣿ ⣿⣿⣿⠀⠀⠀⠀⠀◲
◱⠀⠀⠀⠀⠘⣿⣿⣿ ⣿⣿ ⣿⣿⣿⡟⠀⠀⠀⠀⠀◱
◰⠀⠀⠀⠀⠀⢻⣿⣿⣿⣀⣀⣿⣿⣿⣿⠁⠀⠀⠀⠀⠀◰
◱⠀⠀⠀⠀⠀⠀⠉⠉⠉⠉⠉⠉⠉⠉⠁ ⠀⠀⠀⠀ ◱
◰ ◲ ◱ ◳ ◰ ◲ ◱ ◳ ◰ ◲ ◱ ◳");

            TestClass t = new TestClass();
            Console.WriteLine(t.ToString().Substring(0,0));

            Thread.Sleep(TimeSpan.FromMinutes(100));

            Console.WriteLine(t.ToString());
            GC.KeepAlive(t);
        }
    }
}