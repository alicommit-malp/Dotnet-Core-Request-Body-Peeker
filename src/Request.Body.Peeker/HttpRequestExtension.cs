using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Request.Body.Peeker
{
    /// <summary>
    /// Extension methods that read the <see cref="HttpRequest"/> body without consuming it.
    /// </summary>
    /// <remarks>
    /// Every method enables request buffering, reads the whole body asynchronously regardless of the
    /// <c>Content-Length</c> header (chunked requests and wrong headers are handled), and leaves
    /// <c>request.Body.Position</c> at 0 afterwards, whether the peek succeeds or fails, so the body can be read again.
    /// </remarks>
    public static class HttpRequestExtension
    {
        private const string ReflectionJsonMessage =
            "JSON serialization and deserialization might require types that cannot be statically analyzed. Use the overload that takes a JsonTypeInfo<T> for trimmed or AOT applications.";

        // FileBufferingReadStream reports Length 0 before it is drained, so the default CopyToAsync size would be tiny.
        private const int DrainBufferSize = 81920;

#if !NET9_0_OR_GREATER
        private static readonly JsonSerializerOptions WebJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
#endif

        /// <summary>
        /// Reads the request body as a string without consuming it.
        /// </summary>
        /// <param name="request">The HTTP request.</param>
        /// <param name="encoding">
        /// The encoding used to decode the body. When <see langword="null"/>, the charset of the <c>Content-Type</c>
        /// header is used, falling back to UTF-8.
        /// </param>
        /// <param name="options">Buffering options; <see langword="null"/> uses the defaults.</param>
        /// <param name="cancellationToken">A token to cancel reading the body.</param>
        /// <returns>The body as a string, or <see cref="string.Empty"/> when the body is empty.</returns>
        /// <remarks>
        /// The whole body is read regardless of the <c>Content-Length</c> header, and <c>request.Body.Position</c>
        /// is reset to 0 on success and on failure.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
        /// <exception cref="IOException">
        /// The body is larger than <see cref="PeekOptions.BufferLimit"/>, or the transport failed while reading the body.
        /// When this is thrown the request body can no longer be read by later middleware or the endpoint, so the
        /// caller should short-circuit (typically respond 413) instead of calling the next middleware.
        /// </exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        public static Task<string> PeekBodyAsync(this HttpRequest request, Encoding? encoding = null,
            PeekOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            var resolvedEncoding = encoding ?? ResolveEncoding(request);

            return PeekCoreAsync(request, options, async (body, token) =>
            {
                using var reader = new StreamReader(body, resolvedEncoding, detectEncodingFromByteOrderMarks: false,
                    leaveOpen: true);
                return await reader.ReadToEndAsync(token).ConfigureAwait(false);
            }, cancellationToken);
        }

        /// <summary>
        /// Reads the raw request body bytes without consuming it.
        /// </summary>
        /// <param name="request">The HTTP request.</param>
        /// <param name="options">Buffering options; <see langword="null"/> uses the defaults.</param>
        /// <param name="cancellationToken">A token to cancel reading the body.</param>
        /// <returns>A copy of the body, or an empty memory when the body is empty.</returns>
        /// <remarks>
        /// The whole body is read regardless of the <c>Content-Length</c> header, and <c>request.Body.Position</c>
        /// is reset to 0 on success and on failure.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
        /// <exception cref="IOException">
        /// The body is larger than <see cref="PeekOptions.BufferLimit"/>, or the transport failed while reading the body.
        /// When this is thrown the request body can no longer be read by later middleware or the endpoint, so the
        /// caller should short-circuit (typically respond 413) instead of calling the next middleware.
        /// </exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        /// <exception cref="InvalidOperationException">The body is larger than <see cref="int.MaxValue"/> bytes.</exception>
        public static Task<ReadOnlyMemory<byte>> PeekBodyBytesAsync(this HttpRequest request,
            PeekOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            return PeekCoreAsync(request, options, async (body, token) =>
            {
                if (body.Length == 0)
                {
                    return ReadOnlyMemory<byte>.Empty;
                }

                if (body.Length > int.MaxValue)
                {
                    throw new InvalidOperationException(
                        $"The request body is {body.Length} bytes, which exceeds the maximum of {int.MaxValue} bytes that can be returned as a byte buffer.");
                }

                var buffer = new byte[body.Length];
                await body.ReadExactlyAsync(buffer, token).ConfigureAwait(false);
                return new ReadOnlyMemory<byte>(buffer);
            }, cancellationToken);
        }

        /// <summary>
        /// Deserializes the JSON request body without consuming it, using reflection-based System.Text.Json.
        /// </summary>
        /// <typeparam name="T">The type to deserialize the body into.</typeparam>
        /// <param name="request">The HTTP request.</param>
        /// <param name="jsonOptions">
        /// The serializer options. When <see langword="null"/>, the application's
        /// <see cref="Microsoft.AspNetCore.Http.Json.JsonOptions"/> are used if registered, otherwise the web defaults
        /// (camelCase, case-insensitive).
        /// </param>
        /// <param name="options">Buffering options; <see langword="null"/> uses the defaults.</param>
        /// <param name="cancellationToken">A token to cancel reading the body.</param>
        /// <returns>The deserialized value, or <see langword="default"/> when the body is empty.</returns>
        /// <remarks>
        /// The whole body is read regardless of the <c>Content-Length</c> header, and <c>request.Body.Position</c>
        /// is reset to 0 on success and on failure. A non-UTF-8 charset in the <c>Content-Type</c> header is
        /// transcoded to UTF-8.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
        /// <exception cref="IOException">
        /// The body is larger than <see cref="PeekOptions.BufferLimit"/>, or the transport failed while reading the body.
        /// When this is thrown the request body can no longer be read by later middleware or the endpoint, so the
        /// caller should short-circuit (typically respond 413) instead of calling the next middleware.
        /// </exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        /// <exception cref="JsonException">The body is not valid JSON for <typeparamref name="T"/>.</exception>
        [RequiresUnreferencedCode(ReflectionJsonMessage)]
        [RequiresDynamicCode(ReflectionJsonMessage)]
        public static Task<T?> PeekBodyAsync<T>(this HttpRequest request, JsonSerializerOptions? jsonOptions = null,
            PeekOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            var resolvedJsonOptions = jsonOptions ?? ResolveJsonOptions(request);

            return PeekJsonCoreAsync(request, options,
                (stream, token) => JsonSerializer.DeserializeAsync<T>(stream, resolvedJsonOptions, token),
                cancellationToken);
        }

        /// <summary>
        /// Deserializes the JSON request body without consuming it, using source-generated System.Text.Json metadata.
        /// Safe for trimmed and native AOT applications.
        /// </summary>
        /// <typeparam name="T">The type to deserialize the body into.</typeparam>
        /// <param name="request">The HTTP request.</param>
        /// <param name="jsonTypeInfo">The metadata for <typeparamref name="T"/>, typically from a <c>JsonSerializerContext</c>.</param>
        /// <param name="options">Buffering options; <see langword="null"/> uses the defaults.</param>
        /// <param name="cancellationToken">A token to cancel reading the body.</param>
        /// <returns>The deserialized value, or <see langword="default"/> when the body is empty.</returns>
        /// <remarks>
        /// The whole body is read regardless of the <c>Content-Length</c> header, and <c>request.Body.Position</c>
        /// is reset to 0 on success and on failure. A non-UTF-8 charset in the <c>Content-Type</c> header is
        /// transcoded to UTF-8.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="request"/> or <paramref name="jsonTypeInfo"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="IOException">
        /// The body is larger than <see cref="PeekOptions.BufferLimit"/>, or the transport failed while reading the body.
        /// When this is thrown the request body can no longer be read by later middleware or the endpoint, so the
        /// caller should short-circuit (typically respond 413) instead of calling the next middleware.
        /// </exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        /// <exception cref="JsonException">The body is not valid JSON for <typeparamref name="T"/>.</exception>
        public static Task<T?> PeekBodyAsync<T>(this HttpRequest request, JsonTypeInfo<T> jsonTypeInfo,
            PeekOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(jsonTypeInfo);

            return PeekJsonCoreAsync(request, options,
                (stream, token) => JsonSerializer.DeserializeAsync(stream, jsonTypeInfo, token),
                cancellationToken);
        }

        /// <summary>
        /// Deserializes the request body without consuming it, using a custom <see cref="IBodySerializer"/>.
        /// </summary>
        /// <typeparam name="T">The type to deserialize the body into.</typeparam>
        /// <param name="request">The HTTP request.</param>
        /// <param name="serializer">The serializer that receives the fully buffered UTF-8 body.</param>
        /// <param name="options">Buffering options; <see langword="null"/> uses the defaults.</param>
        /// <param name="cancellationToken">A token to cancel reading the body.</param>
        /// <returns>The deserialized value, or <see langword="default"/> when the body is empty.</returns>
        /// <remarks>
        /// The whole body is read regardless of the <c>Content-Length</c> header, and <c>request.Body.Position</c>
        /// is reset to 0 on success and on failure. A non-UTF-8 charset in the <c>Content-Type</c> header is
        /// transcoded to UTF-8. Exceptions thrown by <paramref name="serializer"/> propagate unchanged.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="request"/> or <paramref name="serializer"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="IOException">
        /// The body is larger than <see cref="PeekOptions.BufferLimit"/>, or the transport failed while reading the body.
        /// When this is thrown the request body can no longer be read by later middleware or the endpoint, so the
        /// caller should short-circuit (typically respond 413) instead of calling the next middleware.
        /// </exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
        /// <exception cref="JsonException">A JSON-based <paramref name="serializer"/> rejected malformed JSON.</exception>
        public static Task<T?> PeekBodyAsync<T>(this HttpRequest request, IBodySerializer serializer,
            PeekOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(serializer);

            return PeekJsonCoreAsync(request, options,
                (stream, token) => new ValueTask<T?>(serializer.DeserializeAsync<T>(stream, token)),
                cancellationToken);
        }

        private static Task<T?> PeekJsonCoreAsync<T>(HttpRequest request, PeekOptions? options,
            Func<Stream, CancellationToken, ValueTask<T?>> deserialize, CancellationToken cancellationToken)
        {
            var encoding = ResolveEncoding(request);

            return PeekCoreAsync(request, options, async (body, token) =>
            {
                if (body.Length == 0)
                {
                    return default;
                }

                if (encoding.CodePage == Encoding.UTF8.CodePage)
                {
                    return await deserialize(body, token).ConfigureAwait(false);
                }

                var utf8Stream = Encoding.CreateTranscodingStream(body, encoding, new UTF8Encoding(false), leaveOpen: true);
                await using (utf8Stream.ConfigureAwait(false))
                {
                    return await deserialize(utf8Stream, token).ConfigureAwait(false);
                }
            }, cancellationToken);
        }

        private static async Task<TResult> PeekCoreAsync<TResult>(HttpRequest request, PeekOptions? options,
            Func<Stream, CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
        {
            options ??= new PeekOptions();
            if (options.BufferLimit is null)
            {
                request.EnableBuffering(options.BufferThreshold);
            }
            else
            {
                request.EnableBuffering(options.BufferThreshold, options.BufferLimit.Value);
            }

            var body = request.Body;
            TResult result;
            try
            {
                body.Position = 0;
                await body.CopyToAsync(Stream.Null, DrainBufferSize, cancellationToken).ConfigureAwait(false);
                body.Position = 0;
                result = await operation(body, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                try
                {
                    Rewind(body);
                }
                catch (Exception rewindError)
                {
                    throw new AggregateException(failure, rewindError);
                }

                throw;
            }

            Rewind(body);
            return result;
        }

        private static void Rewind(Stream body)
        {
            if (body.CanSeek)
            {
                body.Position = 0;
            }
        }

        private static Encoding ResolveEncoding(HttpRequest request)
        {
            return request.GetTypedHeaders().ContentType?.Encoding ?? Encoding.UTF8;
        }

        [RequiresUnreferencedCode(ReflectionJsonMessage)]
        [RequiresDynamicCode(ReflectionJsonMessage)]
        private static JsonSerializerOptions ResolveJsonOptions(HttpRequest request)
        {
            var configured = request.HttpContext.Features.Get<IServiceProvidersFeature>()?.RequestServices
                ?.GetService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()?.Value.SerializerOptions;
            if (configured is not null)
            {
                return configured;
            }

#if NET9_0_OR_GREATER
            return JsonSerializerOptions.Web;
#else
            return WebJsonOptions;
#endif
        }
    }
}
