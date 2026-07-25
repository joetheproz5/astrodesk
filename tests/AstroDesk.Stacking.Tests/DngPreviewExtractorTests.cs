using System.Buffers.Binary;
using AstroDesk.Stacking;
using Xunit;

namespace AstroDesk.Stacking.Tests;

/// <summary>
/// Covers pulling the embedded JPEG rendition out of a DNG.
/// </summary>
/// <remarks>
/// Windows has no DNG codec, so without this the live preview and the frame
/// thumbnails render nothing at all for a RAW-only session — which is exactly
/// what happened: frames collected, live stack stayed empty.
/// </remarks>
public sealed class DngPreviewExtractorTests
{
    [Fact]
    public void ExtractsThePreviewFromASubIfd()
    {
        // Mirrors the real layout measured on an S23 Ultra: IFD0 describes the
        // sensor data in a compression Windows cannot read, and a SubIFD holds
        // the JPEG rendition.
        byte[] jpeg = FakeJpeg(4096);
        byte[] dng = BuildDng(jpeg, compression: 7);

        byte[]? extracted = DngPreviewExtractor.TryExtract(dng);

        Assert.NotNull(extracted);
        Assert.Equal(jpeg, extracted);
    }

    [Fact]
    public void ExtractsTheLargestRenditionWhenSeveralExist()
    {
        // DNGs commonly carry a small thumbnail beside a full-size preview, and
        // only the latter is worth stacking or displaying.
        byte[] small = FakeJpeg(512);
        byte[] large = FakeJpeg(9000);
        byte[] dng = BuildDng(large, compression: 7, extraPreview: small);

        byte[]? extracted = DngPreviewExtractor.TryExtract(dng);

        Assert.NotNull(extracted);
        Assert.Equal(large.Length, extracted!.Length);
    }

    [Fact]
    public void ReturnsNullWhenTheSubIfdIsNotJpegCompressed()
    {
        // Compression 52546 is JPEG XL, which nothing in the box decodes; there
        // is no point handing those bytes to the imaging stack.
        byte[] dng = BuildDng(FakeJpeg(4096), compression: 52546);

        Assert.Null(DngPreviewExtractor.TryExtract(dng));
    }

    [Fact]
    public void ReturnsNullForDataThatIsNotTiff() =>
        Assert.Null(DngPreviewExtractor.TryExtract(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));

    [Fact]
    public void ReturnsNullForATruncatedFile() =>
        Assert.Null(DngPreviewExtractor.TryExtract(new byte[] { (byte)'I', (byte)'I' }));

    [Fact]
    public void ReturnsNullWhenTheOffsetRunsPastTheFile()
    {
        byte[] dng = BuildDng(FakeJpeg(1024), compression: 7);

        // Truncate so the recorded strip extends beyond what is present.
        Assert.Null(DngPreviewExtractor.TryExtract(dng.AsSpan(0, dng.Length / 2)));
    }

    /// <summary>
    /// Bytes that begin with a JPEG marker, including an embedded end-of-image
    /// pair partway through.
    /// </summary>
    /// <remarks>
    /// The interior FFD9 is the point: scanning for the first end-of-image byte
    /// pair truncates a real 2 MB preview at about 42 KB, which then fails to
    /// decode. The directory has to be parsed for the true extent.
    /// </remarks>
    private static byte[] FakeJpeg(int length)
    {
        byte[] jpeg = new byte[length];
        jpeg[0] = 0xFF; jpeg[1] = 0xD8; jpeg[2] = 0xFF; jpeg[3] = 0xE0;
        for (int index = 4; index < length - 2; index++)
        {
            jpeg[index] = (byte)(index % 251);
        }

        if (length > 200)
        {
            jpeg[100] = 0xFF;
            jpeg[101] = 0xD9;
        }

        jpeg[length - 2] = 0xFF;
        jpeg[length - 1] = 0xD9;
        return jpeg;
    }

    private static byte[] BuildDng(byte[] preview, ushort compression, byte[]? extraPreview = null)
    {
        const int headerSize = 8;
        int subIfdCount = extraPreview is null ? 1 : 2;

        // Layout: header, IFD0, SubIFD pointer array, SubIFDs, then image data.
        int ifd0 = headerSize;
        int ifd0Size = 2 + (1 * 12) + 4;
        int subPointers = ifd0 + ifd0Size;
        int subPointerSize = subIfdCount * 4;
        int firstSub = subPointers + subPointerSize;
        int subSize = 2 + (3 * 12) + 4;
        int secondSub = firstSub + subSize;
        int dataStart = firstSub + (subSize * subIfdCount);

        int total = dataStart + preview.Length + (extraPreview?.Length ?? 0);
        byte[] buffer = new byte[total];

        buffer[0] = (byte)'I';
        buffer[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), (uint)ifd0);

        // IFD0: one entry, SubIFDs.
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(ifd0), 1);
        WriteEntry(buffer, ifd0 + 2, tag: 330, type: 4, count: (uint)subIfdCount,
            value: subIfdCount == 1 ? (uint)firstSub : (uint)subPointers);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(ifd0 + 2 + 12), 0);

        if (subIfdCount > 1)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(subPointers), (uint)firstSub);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(subPointers + 4), (uint)secondSub);
        }

        WriteSubIfd(buffer, firstSub, compression, (uint)dataStart, (uint)preview.Length);
        preview.CopyTo(buffer.AsSpan(dataStart));

        if (extraPreview is not null)
        {
            int extraStart = dataStart + preview.Length;
            WriteSubIfd(buffer, secondSub, 7, (uint)extraStart, (uint)extraPreview.Length);
            extraPreview.CopyTo(buffer.AsSpan(extraStart));
        }

        return buffer;
    }

    private static void WriteSubIfd(
        byte[] buffer,
        int offset,
        ushort compression,
        uint stripOffset,
        uint stripLength)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), 3);
        WriteEntry(buffer, offset + 2, tag: 259, type: 3, count: 1, value: compression);
        WriteEntry(buffer, offset + 14, tag: 273, type: 4, count: 1, value: stripOffset);
        WriteEntry(buffer, offset + 26, tag: 279, type: 4, count: 1, value: stripLength);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset + 38), 0);
    }

    private static void WriteEntry(byte[] buffer, int offset, ushort tag, ushort type, uint count, uint value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), tag);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset + 2), type);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset + 4), count);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset + 8), value);
    }
}
