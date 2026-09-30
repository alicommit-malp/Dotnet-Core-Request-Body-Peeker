namespace Request.Body.Peeker
{
    /// <summary>
    /// Controls how the request body is buffered while it is peeked.
    /// </summary>
    /// <remarks>
    /// The options take effect only when the body is first buffered. If buffering was already enabled for the
    /// request, by an earlier peek or by other middleware calling <c>EnableBuffering</c>, they are ignored. They are
    /// also ignored when <c>request.Body</c> is already seekable, because <c>EnableBuffering</c> then leaves it unchanged.
    /// </remarks>
    public sealed class PeekOptions
    {
        /// <summary>
        /// The maximum body size, in bytes, kept in memory before the buffer spills to a temporary file.
        /// Defaults to 30 KB.
        /// </summary>
        public int BufferThreshold { get; init; } = 30 * 1024;

        /// <summary>
        /// The maximum body size, in bytes, that may be buffered. When the body is larger, peeking throws an
        /// <see cref="System.IO.IOException"/>. <see langword="null"/> means no limit beyond the server's own request size limit.
        /// </summary>
        public long? BufferLimit { get; init; }
    }
}
