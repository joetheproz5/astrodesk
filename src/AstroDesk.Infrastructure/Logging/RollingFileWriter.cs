using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AstroDesk.Infrastructure.Logging;

internal sealed class RollingFileWriter : IDisposable
{
    private readonly object _gate = new();
    private readonly RollingFileLoggerOptions _options;
    private StreamWriter? _writer;
    private DateOnly _currentDate;
    private int _sequence;
    private long _currentLength;
    private bool _disposed;

    public RollingFileWriter(RollingFileLoggerOptions options)
    {
        _options = options;
        Directory.CreateDirectory(_options.DirectoryPath);
        RemoveExpiredFiles();
    }

    public void Write(
        DateTimeOffset timestamp,
        LogLevel level,
        string category,
        EventId eventId,
        string message,
        Exception? exception)
    {
        string line = FormatLine(timestamp, level, category, eventId, message, exception);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureWriter(timestamp, Encoding.UTF8.GetByteCount(line));
            _writer!.Write(line);
            _writer.Flush();
            _currentLength += Encoding.UTF8.GetByteCount(line);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }

    private static string FormatLine(
        DateTimeOffset timestamp,
        LogLevel level,
        string category,
        EventId eventId,
        string message,
        Exception? exception)
    {
        StringBuilder builder = new();
        builder.Append(timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture));
        builder.Append(" [");
        builder.Append(level.ToString().ToUpperInvariant());
        builder.Append("] ");
        builder.Append(category);

        if (eventId.Id != 0)
        {
            builder.Append(" (");
            builder.Append(eventId.Id.ToString(CultureInfo.InvariantCulture));
            builder.Append(')');
        }

        builder.Append(": ");
        builder.AppendLine(message);

        if (exception is not null)
        {
            builder.AppendLine(exception.ToString());
        }

        return builder.ToString();
    }

    private void EnsureWriter(DateTimeOffset timestamp, int incomingBytes)
    {
        DateOnly date = DateOnly.FromDateTime(timestamp.LocalDateTime);
        bool dateChanged = date != _currentDate;
        bool sizeExceeded = _writer is not null &&
                            _currentLength + incomingBytes > _options.MaximumFileSizeBytes;

        if (_writer is not null && !dateChanged && !sizeExceeded)
        {
            return;
        }

        _writer?.Dispose();
        _writer = null;

        if (dateChanged)
        {
            _sequence = 0;
            _currentDate = date;
        }
        else if (sizeExceeded)
        {
            _sequence++;
        }

        string suffix = _sequence == 0 ? string.Empty : $"-{_sequence:D2}";
        string fileName = $"{_options.FilePrefix}-{date:yyyyMMdd}{suffix}.log";
        string path = Path.Combine(_options.DirectoryPath, fileName);
        FileStream stream = new(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _currentLength = stream.Length;
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        RemoveExpiredFiles();
    }

    private void RemoveExpiredFiles()
    {
        try
        {
            DirectoryInfo directory = new(_options.DirectoryPath);
            FileInfo[] files = directory
                .EnumerateFiles($"{_options.FilePrefix}-*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToArray();

            foreach (FileInfo file in files.Skip(Math.Max(1, _options.RetainedFileCount)))
            {
                file.Delete();
            }
        }
        catch (IOException)
        {
            // Logging must never take down the application.
        }
        catch (UnauthorizedAccessException)
        {
            // Logging must never take down the application.
        }
    }
}
