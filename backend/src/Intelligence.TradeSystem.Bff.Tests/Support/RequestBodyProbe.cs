using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Intelligence.TradeSystem.Bff.Tests.Support;

/// <summary>
/// Наблюдает за чтением request body на стороне BFF. Client-side <see cref="HttpContent"/>
/// для этого не подходит: TestServer сериализует его независимо от того, читает ли body приложение.
/// </summary>
internal sealed class RequestBodyProbe
{
    private readonly ConcurrentQueue<byte[]> readBuffers = new();
    private int readCount;
    private int bytesRead;

    public bool CaptureBuffers { get; set; }

    public IReadOnlyList<byte[]> ReadBuffers => [.. readBuffers];

    public long? ContentLength { get; private set; }

    public int BytesRead => Volatile.Read(ref bytesRead);

    public int MaxReadSize { get; set; } = int.MaxValue;

    public Action? AfterFirstRead { get; set; }

    public Action? AfterRequest { get; set; }

    public bool WaitForCancellationAfterFirstRead { get; set; }

    public int ReadCount => Volatile.Read(ref readCount);

    /// <summary>
    /// Exception, которое бросает каждое чтение после первого, чтобы сбой происходил при уже
    /// частично заполненном буфере.
    /// </summary>
    public Exception? FailureAfterFirstRead { get; set; }

    public Stream Wrap(Stream inner, long? contentLength)
    {
        ContentLength = contentLength;
        return new ProbeStream(inner, this);
    }

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

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            probe.RegisterRead();
            if (probe.CaptureBuffers && MemoryMarshal.TryGetArray<byte>(buffer, out var segment))
            {
                probe.readBuffers.Enqueue(segment.Array!);
            }

            if (probe.WaitForCancellationAfterFirstRead && probe.ReadCount > 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            var read = await inner.ReadAsync(buffer[..Math.Min(buffer.Length, probe.MaxReadSize)], cancellationToken);
            Interlocked.Add(ref probe.bytesRead, read);
            if (probe.ReadCount == 1)
            {
                probe.AfterFirstRead?.Invoke();
            }

            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
