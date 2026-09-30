# Changelog

All notable changes to this project are documented in this file. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [2.0.0] - Unreleased

### Breaking changes

- The synchronous `PeekBody()` and `PeekBody<T>()` methods are removed. ASP.NET Core disallows synchronous IO on the request body by default, so they threw on every real server. Use the `Async` methods.
- `ISerializer` and `DefaultSerializer` are removed. Custom deserializers implement `IBodySerializer`, which receives a fully buffered UTF-8 stream and a `CancellationToken`.
- The Newtonsoft.Json dependency is removed. JSON is handled by System.Text.Json. A Newtonsoft adapter example is in the README.
- The `where T : class` constraint is gone; structs and record structs work as `T`.
- The library now targets net8.0, net9.0 and net10.0 and references the `Microsoft.AspNetCore.App` shared framework instead of the deprecated `Microsoft.AspNetCore.Http` 2.2 package. It can no longer be used from .NET Framework or .NET Core 2.x.

### Added

- `PeekBodyBytesAsync` returns the raw body as `ReadOnlyMemory<byte>`.
- `PeekBodyAsync<T>(JsonTypeInfo<T>)` for source-generated, trimming and Native AOT safe deserialization.
- `PeekBodyAsync<T>(IBodySerializer)` for custom deserializers.
- `PeekOptions` with `BufferThreshold` (default 30 KB) and `BufferLimit` (throws `IOException` when exceeded).
- `CancellationToken` on every method.
- Encoding is taken from the `Content-Type` charset with UTF-8 fallback; non-UTF-8 JSON bodies are transcoded before deserialization.
- `PeekBodyAsync<T>` uses the application's `ConfigureHttpJsonOptions` settings when no explicit `JsonSerializerOptions` is passed.
- XML documentation, Source Link and a symbol package (`.snupkg`) ship with the package.
- GitHub Actions CI: build, test on net8.0 and net10.0, vulnerability audit, pack, and NuGet publishing on `v*` tags.

### Fixed

- Requests without a `Content-Length` header (chunked HTTP/1.1, most HTTP/2 and HTTP/3 bodies) returned an empty string or threw. The body is now read to the end regardless of the header.
- A `Content-Length` that did not match the actual body caused an `EndOfStreamException` or a truncated result.
- The stream position is reset even when the peek fails, so a malformed body no longer breaks later readers.
- The benchmark measured a `null` body because of a constructor ordering bug.
- The 1.x package pulled in a transitive `System.Text.Encodings.Web` version with a known critical vulnerability through the legacy 2.2 package reference.

## [1.3.0] - 2024

Last 1.x release. Synchronous and asynchronous `PeekBody` on .NET 8 with Newtonsoft.Json.
