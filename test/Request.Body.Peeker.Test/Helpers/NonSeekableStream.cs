namespace Request.Body.Peeker.Test.Helpers
{
    /// <summary>
    /// Read-only, forward-only stream that mimics a server request body.
    /// </summary>
    public sealed class NonSeekableStream : Stream
    {
        private readonly MemoryStream _inner;
        private readonly bool _disallowSynchronousIo;

        public NonSeekableStream(byte[] content, bool disallowSynchronousIo = false)
        {
            _inner = new MemoryStream(content, writable: false);
            _disallowSynchronousIo = disallowSynchronousIo;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ThrowIfSynchronousIoDisallowed();
            return _inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            ThrowIfSynchronousIoDisallowed();
            return _inner.Read(buffer);
        }

        public override int ReadByte()
        {
            ThrowIfSynchronousIoDisallowed();
            return _inner.ReadByte();
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return _inner.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
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

        private void ThrowIfSynchronousIoDisallowed()
        {
            if (_disallowSynchronousIo)
            {
                throw new InvalidOperationException("Synchronous operations are disallowed.");
            }
        }
    }
}
