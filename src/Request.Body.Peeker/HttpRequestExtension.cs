using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using static System.String;

namespace Request.Body.Peeker
{
    public static class HttpRequestExtension
    {
        /// <summary>
        /// Peek at the Http request stream without consuming it
        /// </summary>
        /// <param name="request">Http Request object</param>
        /// <param name="encoding">User's desired encoding</param>
        /// <returns>String representation of the request body</returns>
        public static string PeekBody(this HttpRequest request, Encoding? encoding = null)
        {
            try
            {
                encoding ??= new UTF8Encoding();
                request.EnableBuffering();
                var buffer = new byte[Convert.ToInt32(request.ContentLength)];
                if (buffer.Length == 0) return Empty;

                request.Body.ReadExactly(buffer, 0, buffer.Length);
                return encoding.GetString(buffer);
            }
            finally
            {
                request.Body.Position = 0L;
            }
        }

        /// <summary>
        /// Asynchronous Peek at the Http request stream without consuming it
        /// </summary>
        /// <param name="request">Http Request object</param>
        /// <param name="encoding">User's desired encoding</param>
        /// <returns>String representation of the request body</returns>
        public static async Task<string> PeekBodyAsync(this HttpRequest request, Encoding? encoding = null)
        {
            try
            {
                encoding ??= new UTF8Encoding();
                request.EnableBuffering();
                var buffer = new byte[Convert.ToInt32(request.ContentLength)];
                if (buffer.Length == 0) return Empty;

                await request.Body.ReadExactlyAsync(buffer, 0, buffer.Length);
                return encoding.GetString(buffer);
            }
            finally
            {
                request.Body.Position = 0L;
            }
        }

        /// <summary>
        /// Peek at the Http request stream without consuming it
        /// </summary>
        /// <typeparam name="T">Type of object to deserialize</typeparam>
        /// <param name="request">Http Request object</param>
        /// <param name="encoding">User's desired encoding</param>
        /// <param name="serializer">Instance of implementation of ISerializer</param>
        /// <returns>Deserialized object of type T</returns>
        public static T PeekBody<T>(this HttpRequest request, Encoding? encoding = null, ISerializer? serializer = null)
            where T : class
        {
            try
            {
                encoding ??= new UTF8Encoding();
                serializer ??= new DefaultSerializer();
                request.EnableBuffering();
                var buffer = new byte[Convert.ToInt32(request.ContentLength)];
                request.Body.ReadExactly(buffer, 0, buffer.Length);
                var bodyAsText = encoding.GetString(buffer);
                return serializer.DeserializeObject<T>(bodyAsText);
            }
            finally
            {
                request.Body.Position = 0L;
            }
        }

        /// <summary>
        /// Peek asynchronously at the Http request stream without consuming it
        /// </summary>
        /// <typeparam name="T">The type of object to deserialize the request body to</typeparam>
        /// <param name="request">Http Request object</param>
        /// <param name="encoding">User's desired encoding, defaults to UTF8Encoding if not provided</param>
        /// <param name="serializer">The serializer to use for deserializing the request body, defaults to DefaultSerializer if not provided</param>
        /// <returns>T type which is provided at invocation, deserialized from the request body</returns>
        public static async Task<T> PeekBodyAsync<T>(this HttpRequest request, Encoding? encoding = null,
            ISerializer? serializer = null) where T : class
        {
            try
            {
                encoding ??= new UTF8Encoding();
                serializer ??= new DefaultSerializer();
                request.EnableBuffering();
                var buffer = new byte[Convert.ToInt32(request.ContentLength)];
                await request.Body.ReadExactlyAsync(buffer, 0, buffer.Length);
                var bodyAsText = encoding.GetString(buffer);
                return serializer.DeserializeObject<T>(bodyAsText);
            }
            finally
            {
                request.Body.Position = 0L;
            }
        }
    }
}