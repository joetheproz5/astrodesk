using System.Buffers;

namespace AstroDesk.Capture.Frames;

public sealed class CaptureFrame : IDisposable
{
    private byte[]? _buffer;

    internal CaptureFrame(
        byte[] buffer,
        int length,
        int width,
        int height,
        int stride,
        DateTimeOffset capturedAt)
    {
        _buffer = buffer;
        Length = length;
        Width = width;
        Height = height;
        Stride = stride;
        CapturedAt = capturedAt;
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public int Length { get; }

    public DateTimeOffset CapturedAt { get; }

    public ReadOnlyMemory<byte> BgraPixels =>
        _buffer is null
            ? throw new ObjectDisposedException(nameof(CaptureFrame))
            : _buffer.AsMemory(0, Length);

    public byte[] CopyPixels() => BgraPixels.ToArray();

    public void Dispose()
    {
        byte[]? buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        GC.SuppressFinalize(this);
    }
}
