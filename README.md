# Peeking at HttpContext.Request.Body, without consuming it

[![NuGet](https://img.shields.io/nuget/v/Request.Body.Peeker)](https://www.nuget.org/packages/Request.Body.Peeker/)
[![CI](https://github.com/alicommit-malp/Dotnet-Core-Request-Body-Peeker/actions/workflows/ci.yml/badge.svg)](https://github.com/alicommit-malp/Dotnet-Core-Request-Body-Peeker/actions/workflows/ci.yml)

Request.Body.Peeker adds async extension methods to `HttpRequest` that read the request body in middleware or filters and leave it intact for the rest of the pipeline. It reads the body as a string, as raw bytes, or deserialized from JSON with System.Text.Json (or any serializer you plug in). It works with chunked requests, honors cancellation, and is trimming and AOT compatible when you use the `JsonTypeInfo<T>` overload or the string and bytes methods (the reflection-based generic overload is annotated as not trim-safe).

- NuGet: [Request.Body.Peeker](https://www.nuget.org/packages/Request.Body.Peeker/)
- Source: [GitHub](https://github.com/alicommit-malp/Dotnet-Core-Request-Body-Peeker)

## Install

```bash
dotnet add package Request.Body.Peeker
```

## Usage

The problem: `HttpContext.Request.Body` is a forward-only stream. If middleware or a filter reads it, the MVC model binder later sees an empty stream. The extension methods enable buffering, read the body, and rewind it so the next reader starts at the beginning.

All methods live in the `Request.Body.Peeker` namespace and accept a `CancellationToken`. Pass `HttpContext.RequestAborted` so the read stops when the client disconnects.

### Middleware

```csharp
using Request.Body.Peeker;

app.Use(async (context, next) =>
{
    // Raw string (charset from Content-Type, UTF-8 fallback)
    string text = await context.Request.PeekBodyAsync(cancellationToken: context.RequestAborted);

    // Raw bytes
    ReadOnlyMemory<byte> bytes = await context.Request.PeekBodyBytesAsync(cancellationToken: context.RequestAborted);

    // Deserialized JSON
    LoginRequest? login = await context.Request.PeekBodyAsync<LoginRequest>(cancellationToken: context.RequestAborted);

    if (login is null)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    await next(context);
});

public sealed record LoginRequest(string UserName, string Password);
```

### Action filter

```csharp
using Microsoft.AspNetCore.Mvc.Filters;
using Request.Body.Peeker;

public sealed class ApiKeyInBodyFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        var token = context.HttpContext.RequestAborted;

        LoginRequest? login = await request.PeekBodyAsync<LoginRequest>(cancellationToken: token);
        string text = await request.PeekBodyAsync(cancellationToken: token);
        ReadOnlyMemory<byte> bytes = await request.PeekBodyBytesAsync(cancellationToken: token);

        await next();
    }
}
```

An empty body returns `""` for the string overload, an empty memory for the bytes overload, and `default` for the typed overloads.

## How it works

Every peek method follows the same four steps:

1. It calls `EnableBuffering` before reading anything, so the body can be replayed.
2. It never sizes a buffer from `Content-Length`. The header is never trusted: chunked requests, requests with no header, and requests with a wrong header all read correctly.
3. It drains the whole body asynchronously into the request buffer before anything is handed to a deserializer. Later reads, including synchronous ones inside a serializer, come from the buffer and never touch the transport synchronously.
4. It leaves `request.Body.Position` at 0 on success and on failure. If the peek fails, the original exception is rethrown after the rewind; if the rewind itself also fails, both are surfaced in an `AggregateException`. When the `IOException` for an exceeded `BufferLimit` is thrown, ASP.NET Core has already discarded the chunk it was reading, so the request body can no longer be read by later middleware or the endpoint even though `Position` is 0. The middleware should short-circuit (typically respond 413) instead of calling `next`.

## Options

`PeekOptions` controls buffering. Every method takes it as an optional parameter.

```csharp
var options = new PeekOptions
{
    BufferThreshold = 64 * 1024,
    BufferLimit = 1024 * 1024
};

string text = await request.PeekBodyAsync(options: options, cancellationToken: token);
```

| Property | Default | Meaning |
| --- | --- | --- |
| `BufferThreshold` | 30 KB | Largest body kept in memory. Above this, ASP.NET Core spills the buffer to a temporary file. |
| `BufferLimit` | `null` | Largest body that may be buffered. When exceeded, the peek throws `IOException`. `null` means only the server's own request size limit applies. |

Set `BufferLimit` on public endpoints. Without it, a client can make the server buffer a very large body in memory or on disk.

Raise `BufferThreshold` if your typical bodies are larger than 30 KB. Every body above the threshold is written to and read back from a temporary file on each request; a threshold slightly above your usual body size keeps peeks entirely in memory while `BufferLimit` still caps the worst case.

When the `BufferLimit` `IOException` is thrown, the request body can no longer be read by later middleware or the endpoint, so the middleware should short-circuit (typically respond 413 Payload Too Large) instead of calling `next`. An `IOException` is also thrown when the transport fails while reading the body, for example when the client resets the connection or Kestrel's `MaxRequestBodySize` is exceeded (`BadHttpRequestException` derives from `IOException`).

The options apply only when the body is first buffered. They are ignored if buffering was already enabled for the request (by an earlier peek or by other middleware calling `EnableBuffering`), or if `request.Body` is already seekable.

## JSON

`PeekBodyAsync<T>()` uses System.Text.Json:

- By default it uses the application's `Microsoft.AspNetCore.Http.Json.JsonOptions` when registered, so it picks up `ConfigureHttpJsonOptions`. If none are registered it uses the web defaults (camelCase, case-insensitive).
- MVC's `AddControllers().AddJsonOptions(...)` configures a different type (`Microsoft.AspNetCore.Mvc.JsonOptions`) and is not read. If your settings live there, pass a `JsonSerializerOptions` explicitly.
- Pass a `JsonSerializerOptions` to override:

```csharp
var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = false };
LoginRequest? login = await request.PeekBodyAsync<LoginRequest>(jsonOptions, cancellationToken: token);
```

- Malformed JSON throws `JsonException` from System.Text.Json, and `Position` is still reset.

### Trimming and native AOT

The reflection-based overload is marked `RequiresUnreferencedCode` and `RequiresDynamicCode`. In trimmed or AOT applications, use the `JsonTypeInfo<T>` overload with a source-generated context:

```csharp
using System.Text.Json.Serialization;

[JsonSerializable(typeof(LoginRequest))]
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
internal partial class AppJsonContext : JsonSerializerContext
{
}

LoginRequest? login = await request.PeekBodyAsync(AppJsonContext.Default.LoginRequest, cancellationToken: token);
```

### Custom serializers

Implement `IBodySerializer` to use anything else. It receives a UTF-8 stream that is already fully buffered, so a synchronous read inside the adapter is safe. Do not dispose the stream; the caller owns it.

Newtonsoft.Json adapter (add the `Newtonsoft.Json` package to your own project):

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Request.Body.Peeker;

public sealed class NewtonsoftBodySerializer : IBodySerializer
{
    private static readonly JsonSerializer Serializer = JsonSerializer.CreateDefault();

    public Task<T?> DeserializeAsync<T>(Stream utf8Body, CancellationToken cancellationToken)
    {
        using var reader = new JsonTextReader(new StreamReader(utf8Body, leaveOpen: true));
        return Task.FromResult(Serializer.Deserialize<T>(reader));
    }
}

LoginRequest? login = await request.PeekBodyAsync<LoginRequest>(new NewtonsoftBodySerializer(), cancellationToken: token);
```

Exceptions thrown by the serializer propagate unchanged, after the stream is rewound.

## Encoding

- `PeekBodyAsync()` decodes with the charset from the `Content-Type` header. When there is no charset or it is not a supported encoding, UTF-8 is used.
- Pass an `Encoding` to override: `await request.PeekBodyAsync(Encoding.Latin1, cancellationToken: token)`.
- The typed overloads and `IBodySerializer` always receive UTF-8. A non-UTF-8 charset in `Content-Type` is transcoded to UTF-8 first.

## Migrating from 1.x

| 1.x | 2.x |
| --- | --- |
| `PeekBody()` | `await PeekBodyAsync()` |
| `PeekBody<T>()` | `await PeekBodyAsync<T>()` |
| `PeekBodyAsync()` / `PeekBodyAsync<T>()` | Same names; now take optional `Encoding`, `PeekOptions` and `CancellationToken` |
| `ISerializer` / `DefaultSerializer` | `IBodySerializer`; the default is System.Text.Json |
| Newtonsoft.Json dependency | Removed; see the adapter above to keep using it |
| Raw bytes | New: `await PeekBodyBytesAsync()` |

- The synchronous overloads are removed. ASP.NET Core disallows synchronous IO by default, so `PeekBody()` throws on Kestrel unless `AllowSynchronousIO` is enabled.
- 1.x allocated its buffer from `Content-Length`. Chunked requests have no `Content-Length`, so the buffer had length zero and the peek returned an empty body. 2.x drains the stream and never reads the header.
- `PeekBodyAsync<T>()` no longer requires `T : class`, so value types work.
- The library now uses the shared framework (`Microsoft.AspNetCore.App`) instead of the legacy ASP.NET Core packages.

## Supported frameworks

net8.0, net9.0, net10.0

## Benchmark

Environment: BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat), Intel Core i9-14900K 3.19GHz (32 logical / 16 physical cores), .NET SDK 10.0.112, .NET 10.0.12 X64 RyuJIT x86-64-v3, ShortRun job (3 warmup, 3 iterations, 1 launch).

Command: `DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet run -c Release --project Request.Body.Peeker.BenchMark -- --job short --filter '*'`

`CreateRequestOnly` is the baseline: it measures only building the fake request, so subtract it from the other rows to get the cost of peeking. The other cold rows also dispose the buffered body afterwards, as the server does at the end of a request, which deletes the temporary file above the threshold. `PeekStringWarm` peeks an already-buffered request repeatedly, which is the cost of a second filter peeking the same request. Above the 30 KB `BufferThreshold` the buffered body lives in a temporary file, so the "warm" path still reads it back from disk instead of memory; the 1 MB warm row is in the same range as the cold ones. The ShortRun job has wide error margins; treat the numbers as indicative.

```
| Method             | BodySize | Mean           | Error           | StdDev       | Ratio     | RatioSD | Gen0     | Gen1     | Gen2     | Allocated  | Alloc Ratio |
|------------------- |--------- |---------------:|----------------:|-------------:|----------:|--------:|---------:|---------:|---------:|-----------:|------------:|
| CreateRequestOnly  | 256      |       139.2 ns |         8.57 ns |      0.47 ns |      1.00 |    0.00 |   0.0594 |        - |        - |    1.09 KB |        1.00 |
| PeekString         | 256      |       617.1 ns |       133.91 ns |      7.34 ns |      4.43 |    0.05 |   0.3300 |   0.0038 |        - |    6.08 KB |        5.56 |
| PeekBytes          | 256      |       378.0 ns |       205.49 ns |     11.26 ns |      2.71 |    0.07 |   0.1006 |   0.0005 |        - |    1.85 KB |        1.69 |
| PeekJsonReflection | 256      |       721.3 ns |       259.98 ns |     14.25 ns |      5.18 |    0.09 |   0.1287 |        - |        - |    2.37 KB |        2.16 |
| PeekJsonTypeInfo   | 256      |       714.5 ns |       229.86 ns |     12.60 ns |      5.13 |    0.08 |   0.1259 |        - |        - |    2.32 KB |        2.12 |
| PeekStringWarm     | 256      |       359.1 ns |       193.79 ns |     10.62 ns |      2.58 |    0.07 |   0.2584 |   0.0029 |        - |    4.75 KB |        4.34 |
|                    |          |                |                 |              |           |         |          |          |          |            |             |
| CreateRequestOnly  | 32768    |       140.2 ns |        30.99 ns |      1.70 ns |      1.00 |    0.01 |   0.0594 |        - |        - |    1.09 KB |        1.00 |
| PeekString         | 32768    |   299,718.9 ns |   259,722.16 ns | 14,236.25 ns |  2,137.35 |   90.73 |   9.7656 |   1.9531 |        - |  166.53 KB |      152.26 |
| PeekBytes          | 32768    |   240,856.9 ns |   137,657.19 ns |  7,545.46 ns |  1,717.59 |   49.95 |   3.4180 |   0.4883 |        - |   51.24 KB |       46.85 |
| PeekJsonReflection | 32768    |   267,305.2 ns |   286,747.93 ns | 15,717.62 ns |  1,906.20 |   99.11 |   4.8828 |   0.9766 |        - |   84.72 KB |       77.46 |
| PeekJsonTypeInfo   | 32768    |   266,281.5 ns |   128,764.91 ns |  7,058.04 ns |  1,898.90 |   47.92 |   4.8828 |   0.9766 |        - |   84.67 KB |       77.41 |
| PeekStringWarm     | 32768    |    66,088.1 ns |    29,563.36 ns |  1,620.47 ns |    471.29 |   11.16 |   8.6670 |   2.5635 |        - |  148.31 KB |      135.59 |
|                    |          |                |                 |              |           |         |          |          |          |            |             |
| CreateRequestOnly  | 1048576  |       144.3 ns |        74.68 ns |      4.09 ns |      1.00 |    0.03 |   0.0594 |        - |        - |    1.09 KB |        1.00 |
| PeekString         | 1048576  | 2,904,968.6 ns |   718,597.22 ns | 39,388.74 ns | 20,140.99 |  549.46 | 492.1875 | 468.7500 | 351.5625 | 4165.97 KB |    3,808.88 |
| PeekBytes          | 1048576  | 1,224,106.2 ns | 1,584,846.90 ns | 86,870.81 ns |  8,487.08 |  562.04 | 248.0469 | 248.0469 | 248.0469 | 1046.51 KB |      956.81 |
| PeekJsonReflection | 1048576  | 1,616,343.8 ns | 1,533,727.06 ns | 84,068.76 ns | 11,206.58 |  575.40 | 162.1094 | 162.1094 | 162.1094 | 2072.08 KB |    1,894.47 |
| PeekJsonTypeInfo   | 1048576  | 1,657,444.7 ns |   321,832.07 ns | 17,640.70 ns | 11,491.54 |  302.14 | 193.3594 | 193.3594 | 193.3594 | 2072.13 KB |    1,894.52 |
| PeekStringWarm     | 1048576  | 1,720,822.4 ns |   778,564.65 ns | 42,675.76 ns | 11,930.96 |  389.87 | 488.2813 | 470.7031 | 349.6094 | 4146.25 KB |    3,790.86 |
```

## License

MIT
