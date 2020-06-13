using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;

namespace Request.Body.Peeker
{
    public static class HttpRequestExtension
    {
        /// <summary>
        /// Peek at the Http request stream without consuming it
        /// </summary>
        /// <param name="request">Http Request object</param>
        /// <returns>string representation of the request body</returns>
        public static string PeekBody(this HttpRequest request)
        {
            try
            {
                request.EnableBuffering();
                var buffer = new byte[Convert.ToInt32(request.ContentLength)];
                request.Body.Read(buffer, 0, buffer.Length);
                return Encoding.UTF8.GetString(buffer);
            }
            finally
            {
                request.Body.Position = 0;
            }
        }

        /// <summary>
        /// Peek at the Http request stream without consuming it
        /// </summary>
        /// <param name="request">Http Request object</param>
        /// <returns>T type which provided at invocation</returns>
        public static T PeekBody<T>(this HttpRequest request) where T : class
        {
            try
            {
                request.EnableBuffering();
                var buffer = new byte[Convert.ToInt32(request.ContentLength)];
                request.Body.Read(buffer, 0, buffer.Length);
                var bodyAsText = Encoding.UTF8.GetString(buffer);
                return JsonConvert.DeserializeObject<T>(bodyAsText);
            }
            finally
            {
                request.Body.Position = 0;
            }
        }
        
        /// <summary>
        /// Peek asynchronously at the Http request stream without consuming it
        /// </summary>
        /// <param name="request">Http Request object</param>
        /// <returns>T type which provided at invocation</returns>
        public static async Task<T> PeekBodyAsync<T>(this HttpRequest request) where T : class
        {
            try
            {
                request.EnableBuffering();
                var buffer = new byte[Convert.ToInt32(request.ContentLength)];
                await request.Body.ReadAsync(buffer, 0, buffer.Length);
                var bodyAsText = Encoding.UTF8.GetString(buffer);
                return JsonConvert.DeserializeObject<T>(bodyAsText);
            }
            finally
            {
                request.Body.Position = 0;
            }
        }
    }
}