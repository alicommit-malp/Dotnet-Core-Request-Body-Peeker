using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using NUnit.Framework;
using Request.Body.Peeker.Test.Helpers;

namespace Request.Body.Peeker.Test.Integration
{
    public class MiddlewarePeekTests
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        private static readonly JsonSerializerOptions UnescapedJson = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private TestHostFixture _host = null!;

        [OneTimeSetUp]
        public async Task StartHost()
        {
            _host = await TestHostFixture.StartAsync();
        }

        [OneTimeTearDown]
        public async Task StopHost()
        {
            await _host.DisposeAsync();
        }

        private static string PersonJson(Person person)
        {
            return JsonSerializer.Serialize(new { name = person.Name, surName = person.SurName }, UnescapedJson);
        }

        private static HttpContent WithContentLength(string body, string contentType = "application/json")
        {
            var content = new ByteArrayContent(Utf8NoBom.GetBytes(body));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            return content;
        }

        private static HttpContent Chunked(string body)
        {
            var content = new StreamContent(new NonSeekableStream(Utf8NoBom.GetBytes(body)));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
            content.Headers.ContentLength = null;
            return content;
        }

        private async Task<HttpResponseMessage> PostAsync(string path, string peekMode, HttpContent content)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Add(TestHostFixture.PeekModeHeader, peekMode);
            request.Content = content;
            var response = await _host.Client.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
            return response;
        }

        private async Task<PeekResponse> PostForPeekResponseAsync(string path, string peekMode, HttpContent content)
        {
            using var response = await PostAsync(path, peekMode, content);
            var result = await response.Content.ReadFromJsonAsync<PeekResponse>();
            Assert.That(result, Is.Not.Null);
            return result!;
        }

        private static Person? ParsePeekedPerson(string? peeked)
        {
            Assert.That(peeked, Is.Not.Null);
            return JsonSerializer.Deserialize(peeked!, TestJsonContext.Default.Person);
        }

        [Test]
        public async Task Bind_WithContentLength_PeekedStringAndBoundPersonMatch()
        {
            var person = new Person("ali", "alp");
            var body = PersonJson(person);

            var result = await PostForPeekResponseAsync("/bind", "string", WithContentLength(body));

            Assert.That(result.Peeked, Is.EqualTo(body));
            Assert.That(result.Bound, Is.EqualTo(person));
            Assert.That(result.ContentLength, Is.EqualTo(Utf8NoBom.GetByteCount(body)));
        }

        [Test]
        public async Task Bind_Chunked_PeekedStringAndBoundPersonMatch_AndContentLengthIsNull()
        {
            var person = new Person("ali", "alp");
            var body = PersonJson(person);

            var result = await PostForPeekResponseAsync("/bind", "string", Chunked(body));

            Assert.That(result.ContentLength, Is.Null);
            Assert.That(result.Peeked, Is.EqualTo(body));
            Assert.That(result.Bound, Is.EqualTo(person));
        }

        [Test]
        public async Task Bind_WithBytesPeek_PeekedBytesMatchBody()
        {
            var person = new Person("ali", "alp");
            var body = PersonJson(person);

            var result = await PostForPeekResponseAsync("/bind", "bytes", Chunked(body));

            Assert.That(result.Peeked, Is.EqualTo(body));
            Assert.That(result.Bound, Is.EqualTo(person));
        }

        [Test]
        public async Task Read_Chunked_PeekedJsonAndEndpointReadPersonMatch()
        {
            var person = new Person("ali", "alp");

            var result = await PostForPeekResponseAsync("/read", "json", Chunked(PersonJson(person)));

            Assert.That(ParsePeekedPerson(result.Peeked), Is.EqualTo(person));
            Assert.That(result.Bound, Is.EqualTo(person));
        }

        [Test]
        public async Task Reader_AfterPeek_PipeReaderSeesWholeBody()
        {
            var body = PersonJson(new Person("ali", "alp"));

            using var response = await PostAsync("/reader", "string", Chunked(body));
            var text = await response.Content.ReadAsStringAsync();

            Assert.That(text, Is.EqualTo(body));
        }

        [Test]
        public async Task Twice_MiddlewareAndEndpointPeeksBothReturnBody()
        {
            var body = PersonJson(new Person("ali", "alp"));

            var result = await PostForPeekResponseAsync("/twice", "string", Chunked(body));

            Assert.That(result.Peeked, Is.EqualTo(body));
            Assert.That(result.Second, Is.EqualTo(body));
        }

        [Test]
        public async Task Bind_NonAsciiBody_RoundTrips()
        {
            var person = new Person("Ålí", "Alp");
            var body = PersonJson(person);
            Assert.That(body, Does.Contain("Ålí"));
            Assert.That(Encoding.UTF8.GetByteCount(body), Is.GreaterThan(body.Length));

            var result = await PostForPeekResponseAsync("/bind", "string",
                WithContentLength(body, "application/json; charset=utf-8"));

            Assert.That(result.Peeked, Is.EqualTo(body));
            Assert.That(result.Bound, Is.EqualTo(person));
        }

        [Test]
        public async Task Bind_WithJsonTypeInfoPeek_PeekedAndBoundPersonMatch()
        {
            var person = new Person("ali", "alp");
            var body = JsonSerializer.Serialize(person, TestJsonContext.Default.Person);

            var result = await PostForPeekResponseAsync("/bind", "json-typeinfo", WithContentLength(body));

            Assert.That(ParsePeekedPerson(result.Peeked), Is.EqualTo(person));
            Assert.That(result.Bound, Is.EqualTo(person));
        }

        [Test]
        public async Task Bind_BodyAboveBufferThreshold_RoundTrips()
        {
            var person = new Person(new string('a', 200 * 1024), "alp");
            var body = PersonJson(person);
            Assert.That(Utf8NoBom.GetByteCount(body), Is.GreaterThan(new PeekOptions().BufferThreshold));

            var result = await PostForPeekResponseAsync("/bind", "string", Chunked(body));

            Assert.That(result.Peeked, Has.Length.EqualTo(body.Length));
            Assert.That(result.Peeked, Is.EqualTo(body));
            Assert.That(result.Bound, Is.EqualTo(person));
        }

        [Test]
        public async Task Read_Utf16Json_PeekedAndEndpointReadPersonMatch()
        {
            var person = new Person("ali", "alpé");
            var body = PersonJson(person);
            Assert.That(body, Does.Contain("alpé"));
            var content = new ByteArrayContent(Encoding.Unicode.GetBytes(body));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=utf-16");

            var result = await PostForPeekResponseAsync("/read", "json", content);

            Assert.That(ParsePeekedPerson(result.Peeked), Is.EqualTo(person));
            Assert.That(result.Bound, Is.EqualTo(person));
        }
    }
}
