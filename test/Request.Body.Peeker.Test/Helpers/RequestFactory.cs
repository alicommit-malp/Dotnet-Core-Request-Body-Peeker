using Microsoft.AspNetCore.Http;

namespace Request.Body.Peeker.Test.Helpers
{
    public enum ContentLengthMode
    {
        Exact,
        Missing,
        TooLarge,
        TooSmall
    }

    public static class RequestFactory
    {
        private static readonly List<HttpContext> Created = [];

        public static DefaultHttpContext Create(
            byte[] body,
            string? contentType = null,
            ContentLengthMode contentLength = ContentLengthMode.Exact,
            bool disallowSynchronousIo = true)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Body = new NonSeekableStream(body, disallowSynchronousIo);
            context.Request.ContentType = contentType;
            context.Request.ContentLength = contentLength switch
            {
                ContentLengthMode.Exact => body.Length,
                ContentLengthMode.Missing => null,
                ContentLengthMode.TooLarge => body.Length + 100,
                ContentLengthMode.TooSmall => 1,
                _ => throw new ArgumentOutOfRangeException(nameof(contentLength), contentLength, null)
            };
            Created.Add(context);
            return context;
        }

        /// <summary>
        /// Disposes the bodies of every context created since the last call. A bare <see cref="DefaultHttpContext"/>
        /// never runs the response's registered disposals, so the buffering temp files would otherwise leak.
        /// </summary>
        public static async Task DisposeCreatedAsync()
        {
            foreach (var context in Created)
            {
                await context.Request.Body.DisposeAsync();
            }

            Created.Clear();
        }
    }
}
