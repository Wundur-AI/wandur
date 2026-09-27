using System.Threading.Channels;

namespace Wandur.Core.Tests;

/// <summary>
/// An in-memory connection for <see cref="Wandur.Core.Sessions.TelnetSession"/>: the test plays the server by
/// queuing reads with <see cref="Send"/> and reads what the client wrote with <see cref="ReadWrittenAsync"/>.
/// No socket, no timing assumptions beyond the caller's timeout.
/// </summary>
internal sealed class FakeServerStream : Stream
{
    private readonly Channel<byte[]> _incoming = Channel.CreateUnbounded<byte[]>();
    private readonly List<byte> _written = [];
    private readonly SemaphoreSlim _writtenSignal = new(0);
    private byte[] _current = [];
    private int _offset;
    private int _readCursor;

    /// <summary>Called before every client write, with the bytes about to be written.</summary>
    public Func<byte[], Task>? BeforeWrite { get; set; }

    /// <summary>Queues one server read. Each call arrives as its own read, so a test controls every split.</summary>
    public void Send(params byte[] bytes) => _incoming.Writer.TryWrite(bytes);

    public void EndOfStream() => _incoming.Writer.TryComplete();

    /// <summary>Everything the client has written so far.</summary>
    public byte[] Written { get { lock (_written) return [.. _written]; } }

    /// <summary>Waits for the next <paramref name="count"/> bytes the client writes after the last call.</summary>
    public async Task<byte[]> ReadWrittenAsync(int count, CancellationToken token)
    {
        while (true)
        {
            lock (_written)
            {
                if (_written.Count - _readCursor >= count)
                {
                    var result = _written.GetRange(_readCursor, count).ToArray();
                    _readCursor += count;
                    return result;
                }
            }
            await _writtenSignal.WaitAsync(token);
        }
    }

    /// <summary>How many written bytes <see cref="ReadWrittenAsync"/> has not returned yet.</summary>
    public int Unread { get { lock (_written) return _written.Count - _readCursor; } }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_offset >= _current.Length)
        {
            if (!await _incoming.Reader.WaitToReadAsync(cancellationToken)) return 0;
            _current = await _incoming.Reader.ReadAsync(cancellationToken);
            _offset = 0;
        }
        var count = Math.Min(buffer.Length, _current.Length - _offset);
        _current.AsMemory(_offset, count).CopyTo(buffer);
        _offset += count;
        return count;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var bytes = buffer.ToArray();
        if (BeforeWrite is { } hook) await hook(bytes);
        lock (_written) _written.AddRange(bytes);
        _writtenSignal.Release();
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count).GetAwaiter().GetResult();
    public override void Flush() { }
    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) EndOfStream();
        base.Dispose(disposing);
    }
}
