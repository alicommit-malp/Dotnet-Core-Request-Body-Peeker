using System.IO;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http;
using Moq;
using Newtonsoft.Json;

namespace Request.Body.Peeker.BenchMark
{
    [MemoryDiagnoser]
    public class BenchMarkTest
    {
        private readonly HttpContext _httpContext;
        private readonly DummyClass _dummyClass;

        private HttpContext MakeFakeContext(object body)
        {
            var context = new Mock<HttpContext>();
            var request = new Mock<HttpRequest>();
            var response = new Mock<HttpResponse>();

            var mem = new MemoryStream();
            mem.WriteAsync(Encoding.UTF8.GetBytes(body.ToString()!));
            mem.Seek(0, 0);

            request.Setup(z => z.Body).Returns(mem);

            request.Setup(z => z.ContentLength).Returns(body.ToString()!.Length);

            context.Setup(c => c.Request).Returns(request.Object);
            context.Setup(c => c.Response).Returns(response.Object);

            return context.Object;
        }

        public BenchMarkTest()
        {
            _httpContext = MakeFakeContext(JsonConvert.SerializeObject(_dummyClass));
            _dummyClass = new DummyClass() {Name = "Ali", SurName = "Alp"};
        }

        [Benchmark]
        public async Task PeekBodyAsync()
        {
            await _httpContext.Request.PeekBodyAsync<DummyClass>();
        }

        [Benchmark]
        public void PeekBody()
        {
            _httpContext.Request.PeekBody<DummyClass>();
        }
    }
}