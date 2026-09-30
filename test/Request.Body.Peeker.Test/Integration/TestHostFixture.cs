using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Request.Body.Peeker.Test.Helpers;

namespace Request.Body.Peeker.Test.Integration
{
    public sealed record PeekResponse(string? Peeked, Person? Bound, long? ContentLength, string? Second);

    /// <summary>
    /// A real ASP.NET Core pipeline on <see cref="TestServer"/> whose middleware peeks the body before the endpoints read it.
    /// </summary>
    public sealed class TestHostFixture : IAsyncDisposable
    {
        public const string PeekModeHeader = "X-Peek";

        private const string PeekedKey = "peeked";
        private const string ContentLengthKey = "contentLength";

        private readonly WebApplication _app;

        private TestHostFixture(WebApplication app)
        {
            _app = app;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public static async Task<TestHostFixture> StartAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();

            var app = builder.Build();

            app.Use(async (context, next) =>
            {
                context.Items[ContentLengthKey] = context.Request.ContentLength;
                context.Items[PeekedKey] = await PeekAsync(context.Request);
                await next(context);
            });
            app.UseRouting();

            app.MapPost("/bind", (Person body, HttpContext context) => Respond(context, body));

            app.MapPost("/read", async (HttpContext context) =>
            {
                var body = await context.Request.ReadFromJsonAsync<Person>();
                return Respond(context, body);
            });

            app.MapPost("/reader", async (HttpContext context) =>
            {
                var text = await ReadToEndAsync(context.Request);
                return Results.Text(text, "text/plain", Encoding.UTF8);
            });

            app.MapPost("/twice", async (HttpContext context) =>
            {
                var second = await context.Request.PeekBodyAsync();
                return Respond(context, null, second);
            });

            await app.StartAsync();
            return new TestHostFixture(app);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }

        private static async Task<string> PeekAsync(HttpRequest request)
        {
            var mode = request.Headers[PeekModeHeader].ToString();
            return mode switch
            {
                "string" => await request.PeekBodyAsync(),
                "bytes" => Encoding.UTF8.GetString((await request.PeekBodyBytesAsync()).Span),
                "json" => RenderPerson(await request.PeekBodyAsync<Person>()),
                "json-typeinfo" => RenderPerson(await request.PeekBodyAsync(TestJsonContext.Default.Person)),
                _ => throw new InvalidOperationException($"Unknown {PeekModeHeader} value '{mode}'.")
            };
        }

        private static string RenderPerson(Person? person)
        {
            if (person is null)
            {
                throw new InvalidOperationException("The peeked JSON body deserialized to null.");
            }

            return JsonSerializer.Serialize(person, TestJsonContext.Default.Person);
        }

        private static IResult Respond(HttpContext context, Person? bound, string? second = null)
        {
            return Results.Json(new PeekResponse(
                (string?)context.Items[PeekedKey],
                bound,
                (long?)context.Items[ContentLengthKey],
                second));
        }

        private static async Task<string> ReadToEndAsync(HttpRequest request)
        {
            var reader = request.BodyReader;
            while (true)
            {
                var result = await reader.ReadAsync();
                if (result.IsCompleted)
                {
                    var text = Encoding.UTF8.GetString(result.Buffer.ToArray());
                    reader.AdvanceTo(result.Buffer.End);
                    return text;
                }

                reader.AdvanceTo(result.Buffer.Start, result.Buffer.End);
            }
        }
    }
}
