using System.Buffers.Binary;

namespace AstroDesk.Stacking;

/// <summary>
/// Pulls the embedded JPEG preview out of a DNG.
/// </summary>
/// <remarks>
/// <para>
/// Windows has no imaging codec for these files, so the live preview and the
/// frame thumbnails cannot decode a DNG directly. Every DNG does however carry a
/// ready-made JPEG rendition alongside the sensor data, which is exactly what a
/// preview needs.
/// </para>
/// <para>
/// On an S23 Ultra the raw image itself is JPEG XL compressed (Compression 52546)
/// and unreadable by anything in the box, while SubIFD[0] holds a full
/// 4000x3000 baseline JPEG (Compression 7). Scanning the file for a JPEG
/// start-of-image marker is not sufficient: the first end-of-image byte pair
/// appears about 42 KB into a 2 MB stream, so a naive scan returns a truncated
/// blob that fails to decode. The TIFF directory has to be parsed to get the real
/// extent.
/// </para>
/// </remarks>
public static class DngPreviewExtractor
{
    private const ushort CompressionJpeg = 7;

    /// <summary>
    /// Samsung's JPEG XL compression for the sensor data. Neither Windows nor
    /// Siril's libraw can decode it.
    /// </summary>
    public const ushort CompressionJpegXl = 52546;

    // TIFF tags.
    private const ushort TagSubIfds = 330;
    private const ushort TagCompression = 259;
    private const ushort TagStripOffsets = 273;
    private const ushort TagStripByteCounts = 279;
    private const ushort TagJpegOffset = 513;
    private const ushort TagJpegLength = 514;

    /// <summary>
    /// Returns the embedded JPEG bytes, or null when the file is not a DNG or
    /// carries no usable preview.
    /// </summary>
    public static byte[]? TryExtract(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            byte[] data = File.ReadAllBytes(path);
            return TryExtract(data);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// True when a stacking engine can be expected to read the sensor data.
    /// </summary>
    /// <remarks>
    /// Siril reports "Error in libraw Unsupported file format or not RAW file"
    /// for these files and silently converts nothing, so a run that prefers RAW
    /// ends up stacking only whatever else happened to be in the folder — or
    /// aborting with a sequence that never contained the frames.
    /// </remarks>
    public static bool IsRawDataReadable(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            byte[] data = File.ReadAllBytes(path);
            return TryReadRawCompression(data) is not CompressionJpegXl;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Compression tag of the main image in IFD0, or null when unreadable.
    /// </summary>
    public static ushort? TryReadRawCompression(ReadOnlySpan<byte> data)
    {
        if (!TryReadHeader(data, out bool littleEndian, out uint firstIfd) ||
            !TryReadEntryCount(data, littleEndian, firstIfd, out ushort count))
        {
            return null;
        }

        for (int index = 0; index < count; index++)
        {
            int entry = (int)firstIfd + 2 + (index * 12);
            if (entry + 12 > data.Length)
            {
                break;
            }

            if (ReadUInt16(data[entry..], littleEndian) == TagCompression)
            {
                return ReadUInt16(data[(entry + 8)..], littleEndian);
            }
        }

        return null;
    }

    public static byte[]? TryExtract(ReadOnlySpan<byte> data)
    {
        if (!TryReadHeader(data, out bool littleEndian, out uint firstIfd))
        {
            return null;
        }

        // Prefer the largest JPEG rendition found: DNGs often carry both a small
        // thumbnail and a full-size preview, and only the latter is worth stacking.
        (uint Offset, uint Length) best = default;

        foreach (uint ifdOffset in EnumerateIfds(data, littleEndian, firstIfd))
        {
            (uint Offset, uint Length) candidate = FindJpegIn(data, littleEndian, ifdOffset);
            if (candidate.Length > best.Length)
            {
                best = candidate;
            }
        }

        if (best.Length == 0 ||
            best.Offset + best.Length > (uint)data.Length)
        {
            return null;
        }

        ReadOnlySpan<byte> blob = data.Slice((int)best.Offset, (int)best.Length);
        return StartsWithJpegMarker(blob) ? blob.ToArray() : null;
    }

    private static bool StartsWithJpegMarker(ReadOnlySpan<byte> blob) =>
        blob.Length > 3 && blob[0] == 0xFF && blob[1] == 0xD8 && blob[2] == 0xFF;

    private static bool TryReadHeader(
        ReadOnlySpan<byte> data,
        out bool littleEndian,
        out uint firstIfd)
    {
        littleEndian = true;
        firstIfd = 0;

        if (data.Length < 8)
        {
            return false;
        }

        if (data[0] == 'I' && data[1] == 'I')
        {
            littleEndian = true;
        }
        else if (data[0] == 'M' && data[1] == 'M')
        {
            littleEndian = false;
        }
        else
        {
            return false;
        }

        if (ReadUInt16(data[2..], littleEndian) != 42)
        {
            return false;
        }

        firstIfd = ReadUInt32(data[4..], littleEndian);
        return firstIfd > 0 && firstIfd < data.Length;
    }

    /// <summary>
    /// Yields the top-level IFD and any SubIFDs it points at. The preview lives
    /// in a SubIFD, not the root directory.
    /// </summary>
    private static List<uint> EnumerateIfds(
        ReadOnlySpan<byte> data,
        bool littleEndian,
        uint firstIfd)
    {
        List<uint> offsets = [firstIfd];

        foreach (uint sub in ReadSubIfdOffsets(data, littleEndian, firstIfd))
        {
            if (sub > 0 && sub < data.Length)
            {
                offsets.Add(sub);
            }
        }

        return offsets;
    }

    private static List<uint> ReadSubIfdOffsets(
        ReadOnlySpan<byte> data,
        bool littleEndian,
        uint ifdOffset)
    {
        List<uint> result = [];
        if (!TryReadEntryCount(data, littleEndian, ifdOffset, out ushort count))
        {
            return result;
        }

        for (int index = 0; index < count; index++)
        {
            int entry = (int)ifdOffset + 2 + (index * 12);
            if (entry + 12 > data.Length)
            {
                break;
            }

            ushort tag = ReadUInt16(data[entry..], littleEndian);
            if (tag != TagSubIfds)
            {
                continue;
            }

            uint valueCount = ReadUInt32(data[(entry + 4)..], littleEndian);
            uint inlineOrPointer = ReadUInt32(data[(entry + 8)..], littleEndian);

            if (valueCount == 1)
            {
                result.Add(inlineOrPointer);
                continue;
            }

            // More than one SubIFD: the field holds a pointer to the array.
            for (uint item = 0; item < valueCount; item++)
            {
                int position = (int)(inlineOrPointer + (item * 4));
                if (position + 4 <= data.Length)
                {
                    result.Add(ReadUInt32(data[position..], littleEndian));
                }
            }
        }

        return result;
    }

    private static (uint Offset, uint Length) FindJpegIn(
        ReadOnlySpan<byte> data,
        bool littleEndian,
        uint ifdOffset)
    {
        if (!TryReadEntryCount(data, littleEndian, ifdOffset, out ushort count))
        {
            return default;
        }

        ushort compression = 0;
        uint stripOffset = 0;
        uint stripLength = 0;
        uint jpegOffset = 0;
        uint jpegLength = 0;

        for (int index = 0; index < count; index++)
        {
            int entry = (int)ifdOffset + 2 + (index * 12);
            if (entry + 12 > data.Length)
            {
                break;
            }

            ushort tag = ReadUInt16(data[entry..], littleEndian);
            ushort type = ReadUInt16(data[(entry + 2)..], littleEndian);
            uint value = type == 3
                ? ReadUInt16(data[(entry + 8)..], littleEndian)
                : ReadUInt32(data[(entry + 8)..], littleEndian);

            switch (tag)
            {
                case TagCompression: compression = (ushort)value; break;
                case TagStripOffsets: stripOffset = value; break;
                case TagStripByteCounts: stripLength = value; break;
                case TagJpegOffset: jpegOffset = value; break;
                case TagJpegLength: jpegLength = value; break;
                default: break;
            }
        }

        // Older files use the JPEGInterchangeFormat pair; Samsung uses a
        // JPEG-compressed strip, which is why compression must be checked.
        if (jpegOffset > 0 && jpegLength > 0)
        {
            return (jpegOffset, jpegLength);
        }

        return compression == CompressionJpeg && stripOffset > 0 && stripLength > 0
            ? (stripOffset, stripLength)
            : default;
    }

    private static bool TryReadEntryCount(
        ReadOnlySpan<byte> data,
        bool littleEndian,
        uint ifdOffset,
        out ushort count)
    {
        count = 0;
        if (ifdOffset + 2 > data.Length)
        {
            return false;
        }

        count = ReadUInt16(data[(int)ifdOffset..], littleEndian);
        return count > 0 && ifdOffset + 2 + (count * 12u) <= data.Length;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, bool littleEndian) =>
        littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(data)
            : BinaryPrimitives.ReadUInt16BigEndian(data);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, bool littleEndian) =>
        littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(data)
            : BinaryPrimitives.ReadUInt32BigEndian(data);
}
