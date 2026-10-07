namespace Intelligence.TradeSystem.Bff.Tests.Support;

/// <summary>
/// Наблюдает за чтением request body на стороне BFF. Client-side <see cref="HttpContent"/>
/// для этого не подходит: TestServer сериализует его независимо от того, читает ли body приложение.
/// </summary>
internal sealed class RequestBodyProbe
{
    private int readCount;

    public int ReadCount => Volatile.Read(ref readCount);

    /// <summary>
    /// Exception, которое бросает каждое чтение после первого, чтобы сбой происходил при уже
    /// частично заполненном буфере.
    /// </summary>
    public Exception? FailureAfterFirstRead { get; set; }

    public Stream Wrap(Stream inner) => new ProbeStream(inner, this);

    private void RegisterRead()
    {
        if (Interlocked.Increment(ref readCount) > 1 && FailureAfterFirstRead is { } failure)
        {
            throw failure;
        }
    }

    private sealed class ProbeStream(Stream inner, RequestBodyProbe probe) : Stream
    {
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
            probe.RegisterRead();
            return inner.Read(buffer, offset, count);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            probe.RegisterRead();
            return inner.ReadAsync(buffer, cancellationToken);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
