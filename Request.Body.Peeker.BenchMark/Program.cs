using BenchmarkDotNet.Running;

namespace Request.Body.Peeker.BenchMark
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
