using BenchmarkDotNet.Running;

namespace Request.Body.Peeker.BenchMark
{
    public class Program
    {
        public static void Main(string[] args)
        {
            BenchmarkRunner.Run<BenchMarkTest>();
        }
    }
}