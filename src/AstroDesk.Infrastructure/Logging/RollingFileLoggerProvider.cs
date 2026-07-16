using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AstroDesk.Infrastructure.Logging;

public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly RollingFileWriter _writer;
    private readonly ConcurrentDictionary<string, RollingFileLogger> _loggers = new(StringComparer.Ordinal);

    public RollingFileLoggerProvider(IOptions<RollingFileLoggerOptions> options)
    {
        _writer = new RollingFileWriter(options.Value);
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new RollingFileLogger(name, _writer));

    public void Dispose()
    {
        _loggers.Clear();
        _writer.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class RollingFileLogger(string category, RollingFileWriter writer) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string message = formatter(state, exception);
            writer.Write(DateTimeOffset.Now, logLevel, category, eventId, message, exception);
        }
    }
}
