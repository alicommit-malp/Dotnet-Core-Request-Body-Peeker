using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http;

namespace Request.Body.Peeker.BenchMark
{
    public record Person(string Name, string SurName);

    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
    [JsonSerializable(typeof(Person))]
    public partial class BenchJsonContext : JsonSerializerContext
    {
    }

    internal sealed class NonSeekableStream : Stream
    {
        private readonly MemoryStream _inner;

        public NonSeekableStream(byte[] content)
        {
            _inner = new MemoryStream(content, writable: false);
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            return _inner.Read(buffer);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return _inner.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _inner.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [MemoryDiagnoser]
    public class PeekBenchmarks
    {
        private byte[] _body = [];
        private HttpRequest _warmRequest = null!;

        [Params(256, 32 * 1024, 1024 * 1024)]
        public int BodySize;

        [GlobalSetup]
        public void Setup()
        {
            const string prefix = "{\"name\":\"";
            const string suffix = "\",\"surName\":\"alp\"}";
            var padding = BodySize - prefix.Length - suffix.Length;
            _body = Encoding.UTF8.GetBytes(prefix + new string('x', padding) + suffix);
            _warmRequest = CreateRequest();
        }

        [GlobalCleanup]
        public async Task Cleanup()
        {
            await _warmRequest.Body.DisposeAsync();
        }

        [Benchmark(Baseline = true)]
        public HttpRequest CreateRequestOnly()
        {
            return CreateRequest();
        }

        [Benchmark]
        public async Task<string> PeekString()
        {
            var request = CreateRequest();
            var result = await request.PeekBodyAsync();
            await request.Body.DisposeAsync();
            return result;
        }

        [Benchmark]
        public async Task<ReadOnlyMemory<byte>> PeekBytes()
        {
            var request = CreateRequest();
            var result = await request.PeekBodyBytesAsync();
            await request.Body.DisposeAsync();
            return result;
        }

        [Benchmark]
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Benchmark project is not trimmed.")]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Benchmark project is not AOT compiled.")]
        public async Task<Person?> PeekJsonReflection()
        {
            var request = CreateRequest();
            var result = await request.PeekBodyAsync<Person>();
            await request.Body.DisposeAsync();
            return result;
        }

        [Benchmark]
        public async Task<Person?> PeekJsonTypeInfo()
        {
            var request = CreateRequest();
            var result = await request.PeekBodyAsync(BenchJsonContext.Default.Person);
            await request.Body.DisposeAsync();
            return result;
        }

        [Benchmark]
        public Task<string> PeekStringWarm()
        {
            return _warmRequest.PeekBodyAsync();
        }

        private HttpRequest CreateRequest()
        {
            var request = new DefaultHttpContext().Request;
            request.Body = new NonSeekableStream(_body);
            request.ContentLength = null;
            return request;
        }
    }
}
