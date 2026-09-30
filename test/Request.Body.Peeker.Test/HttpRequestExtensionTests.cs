using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Request.Body.Peeker.Test.Helpers;

namespace Request.Body.Peeker.Test
{
    public class HttpRequestExtensionTests
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        [TearDown]
        public Task DisposeRequests()
        {
            return RequestFactory.DisposeCreatedAsync();
        }

        private static byte[] Utf8(string value)
        {
            return Utf8NoBom.GetBytes(value);
        }

        private static async Task AssertRereadable(HttpRequest request, byte[] expected)
        {
            Assert.That(request.Body.Position, Is.Zero);
            using var copy = new MemoryStream();
            await request.Body.CopyToAsync(copy);
            Assert.That(copy.ToArray(), Is.EqualTo(expected));
        }

        [Test]
        public async Task PeekBodyAsync_ReturnsBody_WithContentLength()
        {
            var bytes = Utf8("{\"name\":\"ali\"}");
            var context = RequestFactory.Create(bytes);

            var result = await context.Request.PeekBodyAsync();

            Assert.That(result, Is.EqualTo("{\"name\":\"ali\"}"));
            await AssertRereadable(context.Request, bytes);
        }

        [Test]
        public async Task PeekBodyAsync_ReturnsBody_WhenContentLengthIsNull()
        {
            var bytes = Utf8("chunked body");
            var context = RequestFactory.Create(bytes, contentLength: ContentLengthMode.Missing);

            var result = await context.Request.PeekBodyAsync();

            Assert.That(result, Is.EqualTo("chunked body"));
            await AssertRereadable(context.Request, bytes);
        }

        [Test]
        public async Task PeekBodyAsync_ReturnsActualBytes_WhenContentLengthIsTooLarge()
        {
            var bytes = Utf8("short body");
            var context = RequestFactory.Create(bytes, contentLength: ContentLengthMode.TooLarge);

            var result = await context.Request.PeekBodyAsync();

            Assert.That(result, Is.EqualTo("short body"));
            await AssertRereadable(context.Request, bytes);
        }

        [Test]
        public async Task PeekBodyAsync_ReturnsActualBytes_WhenContentLengthIsTooSmall()
        {
            var bytes = Utf8("a body longer than one byte");
            var context = RequestFactory.Create(bytes, contentLength: ContentLengthMode.TooSmall);

            var result = await context.Request.PeekBodyAsync();

            Assert.That(result, Is.EqualTo("a body longer than one byte"));
            await AssertRereadable(context.Request, bytes);
        }

        [Test]
        public async Task PeekBodyAsync_ReturnsEmpty_ForEmptyBody()
        {
            var context = RequestFactory.Create([]);

            var result = await context.Request.PeekBodyAsync();

            Assert.That(result, Is.EqualTo(string.Empty));
            await AssertRereadable(context.Request, []);
        }

        [Test]
        public async Task PeekBodyAsyncT_ReturnsDefault_ForEmptyBody()
        {
            var classContext = RequestFactory.Create([], "application/json");
            var structContext = RequestFactory.Create([], "application/json");

            var person = await classContext.Request.PeekBodyAsync<Person>();
            var point = await structContext.Request.PeekBodyAsync<Point>();

            Assert.That(person, Is.Null);
            Assert.That(point, Is.EqualTo(default(Point)));
        }

        [Test]
        public async Task PeekBodyAsync_DecodesNonAscii_Utf8()
        {
            const string text = "Grüße, 世界 🌍";
            var bytes = Utf8(text);
            var context = RequestFactory.Create(bytes, "text/plain; charset=utf-8");

            var result = await context.Request.PeekBodyAsync();

            Assert.That(result, Is.EqualTo(text));
            await AssertRereadable(context.Request, bytes);
        }

        [Test]
        public async Task PeekBodyAsync_UsesCharsetFromContentType()
        {
            var bytes = Encoding.Latin1.GetBytes("café");
            var context = RequestFactory.Create(bytes, "text/plain; charset=iso-8859-1");

            var result = await context.Request.PeekBodyAsync();

            Assert.That(result, Is.EqualTo("café"));
        }

        [Test]
        public async Task PeekBodyAsync_ExplicitEncodingOverridesCharset()
        {
            var bytes = Utf8("café");
            var context = RequestFactory.Create(bytes, "text/plain; charset=iso-8859-1");

            var result = await context.Request.PeekBodyAsync(Encoding.UTF8);

            Assert.That(result, Is.EqualTo("café"));
        }

        [TestCase("text/plain; charset=not-a-real-charset")]
        [TestCase("this is not a media type")]
        public async Task PeekBodyAsync_FallsBackToUtf8_ForUnknownCharsetOrMalformedContentType(string contentType)
        {
            var bytes = Utf8("café");
            var context = RequestFactory.Create(bytes, contentType);

            var result = await context.Request.PeekBodyAsync();

            Assert.That(result, Is.EqualTo("café"));
            await AssertRereadable(context.Request, bytes);
        }

        [Test]
        public async Task PeekBodyAsyncT_TranscodesNonUtf8Json()
        {
            var bytes = Encoding.Unicode.GetBytes("{\"name\":\"ali\",\"surName\":\"alpé\"}");
            var context = RequestFactory.Create(bytes, "application/json; charset=utf-16");

            var result = await context.Request.PeekBodyAsync<Person>();

            Assert.That(result, Is.EqualTo(new Person("ali", "alpé")));
        }

        [Test]
        public async Task PeekBodyAsyncT_UsesWebDefaults_CamelCase()
        {
            var bytes = Utf8("{\"name\":\"ali\",\"surName\":\"alp\"}");
            var context = RequestFactory.Create(bytes, "application/json");

            var result = await context.Request.PeekBodyAsync<Person>();

            Assert.That(result, Is.EqualTo(new Person("ali", "alp")));
            await AssertRereadable(context.Request, bytes);
        }

        [Test]
        public async Task PeekBodyAsyncT_WorksForStruct()
        {
            var context = RequestFactory.Create(Utf8("{\"x\":3,\"y\":4}"), "application/json");

            var result = await context.Request.PeekBodyAsync<Point>();

            Assert.That(result, Is.EqualTo(new Point(3, 4)));
        }

        [Test]
        public async Task PeekBodyAsyncT_WithJsonTypeInfo()
        {
            var bytes = Utf8("{\"Name\":\"ali\",\"SurName\":\"alp\"}");
            var context = RequestFactory.Create(bytes, "application/json");

            var result = await context.Request.PeekBodyAsync(TestJsonContext.Default.Person);

            Assert.That(result, Is.EqualTo(new Person("ali", "alp")));
            await AssertRereadable(context.Request, bytes);
        }

        [Test]
        public async Task PeekBodyAsyncT_HonoursIOptionsJsonOptions()
        {
            using var services = new ServiceCollection()
                .ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower)
                .BuildServiceProvider();
            var context = RequestFactory.Create(Utf8("{\"sur_name\":\"alp\",\"name\":\"ali\"}"), "application/json");
            context.RequestServices = services;

            var result = await context.Request.PeekBodyAsync<Person>();

            Assert.That(result, Is.EqualTo(new Person("ali", "alp")));
        }

        [Test]
        public async Task PeekBodyAsyncT_ExplicitOptionsOverrideIOptions()
        {
            using var services = new ServiceCollection()
                .ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower)
                .BuildServiceProvider();
            var context = RequestFactory.Create(Utf8("{\"Name\":\"ali\",\"SurName\":\"alp\"}"), "application/json");
            context.RequestServices = services;

            var result = await context.Request.PeekBodyAsync<Person>(new JsonSerializerOptions());

            Assert.That(result, Is.EqualTo(new Person("ali", "alp")));
        }

        [Test]
        public async Task PeekBodyAsyncT_WithCustomSerializer_ReceivesFullyBufferedUtf8Stream()
        {
            const string text = "{\"name\":\"ali\"}";
            var bytes = Utf8(text);
            var context = RequestFactory.Create(bytes, "application/json", ContentLengthMode.Missing);

            var result = await context.Request.PeekBodyAsync<string>(new SynchronousReadingSerializer());

            Assert.That(result, Is.EqualTo(text));
            await AssertRereadable(context.Request, bytes);
        }

        [Test]
        public void PeekBodyAsyncT_ThrowsArgumentNull_ForNullSerializer()
        {
            var context = RequestFactory.Create(Utf8("{}"), "application/json");

            Assert.ThrowsAsync<ArgumentNullException>(() => context.Request.PeekBodyAsync<Person>((IBodySerializer)null!));
        }

        [Test]
        public async Task PeekBodyBytesAsync_ReturnsExactBytes()
        {
            var bytes = new byte[1024 * 1024];
            new Random(42).NextBytes(bytes);
            var context = RequestFactory.Create(bytes, "application/octet-stream", ContentLengthMode.Missing);

            var result = await context.Request.PeekBodyBytesAsync(new PeekOptions { BufferThreshold = 1024 });

            Assert.That(result.ToArray(), Is.EqualTo(bytes));
            await AssertRereadable(context.Request, bytes);
        }

        [Test]
        public async Task PeekBodyAsync_CanBeCalledTwice_SameResult()
        {
            var bytes = Utf8("peek me twice");
            var context = RequestFactory.Create(bytes);

            var first = await context.Request.PeekBodyAsync();
            var second = await context.Request.PeekBodyAsync();

            Assert.That(first, Is.EqualTo("peek me twice"));
            Assert.That(second, Is.EqualTo(first));
            await AssertRereadable(context.Request, bytes);
        }

        [Test]
        public async Task PeekBodyAsync_AfterPartialRead_ReturnsWholeBody()
        {
            var bytes = Utf8("partially read body");
            var context = RequestFactory.Create(bytes);

            await context.Request.PeekBodyAsync();
            var partial = new byte[3];
            await context.Request.Body.ReadExactlyAsync(partial);
            var result = await context.Request.PeekBodyAsync();

            Assert.That(result, Is.EqualTo("partially read body"));
            await AssertRereadable(context.Request, bytes);
        }

        [Test]
        public void PeekBodyAsync_ThrowsIOException_WhenBufferLimitExceeded()
        {
            var context = RequestFactory.Create(new byte[64]);

            Assert.ThrowsAsync<IOException>(() => context.Request.PeekBodyAsync(options: new PeekOptions { BufferLimit = 16 }));
            Assert.That(context.Request.Body.Position, Is.Zero);
        }

        [Test]
        public void PeekBodyAsync_Throws_WhenCancelled()
        {
            var context = RequestFactory.Create(Utf8("cancel me"));
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(() => context.Request.PeekBodyAsync(cancellationToken: cts.Token));
        }

        [Test]
        public async Task PeekBodyAsyncT_ThrowsJsonException_ForMalformedJson_AndRewinds()
        {
            var bytes = Utf8("{\"name\":");
            var context = RequestFactory.Create(bytes, "application/json");

            Assert.CatchAsync<JsonException>(() => context.Request.PeekBodyAsync<Person>());
            await AssertRereadable(context.Request, bytes);
        }

        [Test]
        public void PeekBodyAsync_ThrowsArgumentNull_ForNullRequest()
        {
            HttpRequest request = null!;

            Assert.ThrowsAsync<ArgumentNullException>(() => request.PeekBodyAsync());
            Assert.ThrowsAsync<ArgumentNullException>(() => request.PeekBodyBytesAsync());
            Assert.ThrowsAsync<ArgumentNullException>(() => request.PeekBodyAsync<Person>());
            Assert.ThrowsAsync<ArgumentNullException>(() => request.PeekBodyAsync(TestJsonContext.Default.Person));
            Assert.ThrowsAsync<ArgumentNullException>(() => request.PeekBodyAsync<Person>(new SynchronousReadingSerializer()));
        }

        [Test]
        public void PeekBodyAsync_NeverPerformsSynchronousIo()
        {
            var json = Utf8("{\"name\":\"ali\",\"surName\":\"alp\"}");

            Assert.DoesNotThrowAsync(() => RequestFactory.Create(json, disallowSynchronousIo: true).Request.PeekBodyAsync());
            Assert.DoesNotThrowAsync(() => RequestFactory.Create(json, disallowSynchronousIo: true).Request.PeekBodyBytesAsync());
            Assert.DoesNotThrowAsync(() => RequestFactory.Create(json, "application/json", disallowSynchronousIo: true).Request.PeekBodyAsync<Person>());
        }

        [Test]
        public async Task PeekBodyAsyncT_WithCustomSerializer_TranscodesUtf16ToUtf8()
        {
            var bytes = Encoding.Unicode.GetBytes("{\"name\":\"ali\",\"surName\":\"alpé\"}");
            var context = RequestFactory.Create(bytes, "application/json; charset=utf-16");

            var result = await context.Request.PeekBodyAsync<Person>(new SynchronousUtf8JsonSerializer());

            Assert.That(result, Is.EqualTo(new Person("ali", "alpé")));
            await AssertRereadable(context.Request, bytes);
        }

        [Test]
        public void PeekBodyAsync_ThrowsAggregateOfReadAndRewindErrors_WhenBothFail()
        {
            var readError = new IOException("read failed");
            var rewindError = new IOException("rewind failed");
            var context = new DefaultHttpContext();
            context.Request.Body = new FaultingSeekableStream(Utf8("body"), readError, rewindError, failingPositionSet: null);

            var thrown = Assert.ThrowsAsync<AggregateException>(() => context.Request.PeekBodyAsync());

            Assert.That(thrown!.InnerExceptions, Is.EqualTo(new Exception[] { readError, rewindError }));
        }

        [Test]
        public void PeekBodyAsync_PropagatesRewindError_WhenReadSucceeds()
        {
            var rewindError = new IOException("rewind failed");
            var context = new DefaultHttpContext();
            context.Request.Body = new FaultingSeekableStream(Utf8("body"), readError: null, rewindError, failingPositionSet: 3);

            var thrown = Assert.ThrowsAsync<IOException>(() => context.Request.PeekBodyAsync());

            Assert.That(thrown, Is.SameAs(rewindError));
        }

        private sealed class SynchronousReadingSerializer : IBodySerializer
        {
            public Task<T?> DeserializeAsync<T>(Stream utf8Body, CancellationToken cancellationToken)
            {
                using var reader = new StreamReader(utf8Body, Utf8NoBom, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                return Task.FromResult((T?)(object?)reader.ReadToEnd());
            }
        }

        private sealed class SynchronousUtf8JsonSerializer : IBodySerializer
        {
            private static readonly JsonSerializerOptions WebOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

            public Task<T?> DeserializeAsync<T>(Stream utf8Body, CancellationToken cancellationToken)
            {
                using var reader = new StreamReader(utf8Body, Utf8NoBom, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                return Task.FromResult(JsonSerializer.Deserialize<T>(reader.ReadToEnd(), WebOptions));
            }
        }

        /// <summary>
        /// Seekable stream (so <c>EnableBuffering</c> keeps it) that can fail reads and fail a <c>Position</c> set:
        /// the first set after a failed read, or else the Nth set.
        /// </summary>
        private sealed class FaultingSeekableStream : Stream
        {
            private readonly MemoryStream _inner;
            private readonly Exception? _readError;
            private readonly Exception _positionError;
            private readonly int? _failingPositionSet;
            private int _positionSets;
            private bool _readFailed;

            public FaultingSeekableStream(byte[] content, Exception? readError, Exception positionError, int? failingPositionSet)
            {
                _inner = new MemoryStream(content, writable: false);
                _readError = readError;
                _positionError = positionError;
                _failingPositionSet = failingPositionSet;
            }

            public override bool CanRead => true;

            public override bool CanSeek => true;

            public override bool CanWrite => false;

            public override long Length => _inner.Length;

            public override long Position
            {
                get => _inner.Position;
                set
                {
                    _positionSets++;
                    if (_readFailed || _positionSets == _failingPositionSet)
                    {
                        throw _positionError;
                    }

                    _inner.Position = value;
                }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                ThrowIfReadFails();
                return _inner.Read(buffer, offset, count);
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                ThrowIfReadFails();
                return _inner.ReadAsync(buffer, offset, count, cancellationToken);
            }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                ThrowIfReadFails();
                return _inner.ReadAsync(buffer, cancellationToken);
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _inner.Dispose();
                }

                base.Dispose(disposing);
            }

            private void ThrowIfReadFails()
            {
                if (_readError is not null)
                {
                    _readFailed = true;
                    throw _readError;
                }
            }
        }
    }
}
