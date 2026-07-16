namespace AstroDesk.Device.Processes;

public enum ProcessOutputStream
{
    StandardOutput,
    StandardError,
}

public sealed record ProcessOutputLine(ProcessOutputStream Stream, string Text);

public sealed record ProcessInvocation(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string?>? EnvironmentVariables = null,
    IReadOnlyCollection<string>? SensitiveValues = null,
    bool CreateNoWindow = true,
    Action<ProcessOutputLine>? OutputReceived = null);

public sealed record ProcessExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration)
{
    public bool Succeeded => ExitCode == 0;
}

public interface IChildProcess : IAsyncDisposable
{
    int Id { get; }

    bool HasExited { get; }

    int? ExitCode { get; }

    Task<int> WaitForExitAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

public interface IProcessRunner
{
    Task<ProcessExecutionResult> RunAsync(
        ProcessInvocation invocation,
        CancellationToken cancellationToken = default);

    Task<IChildProcess> StartAsync(
        ProcessInvocation invocation,
        CancellationToken cancellationToken = default);
}

public sealed class ToolProcessStartException : InvalidOperationException
{
    public ToolProcessStartException(string executable, Exception innerException)
        : base($"Failed to start external tool '{Path.GetFileName(executable)}'. Verify the configured path and permissions.", innerException)
    {
        Executable = executable;
    }

    public string Executable { get; }
}

public static class SensitiveDataRedactor
{
    public const string RedactionToken = "<redacted>";

    public static string Redact(string value, IReadOnlyCollection<string>? sensitiveValues)
    {
        if (string.IsNullOrEmpty(value) || sensitiveValues is null || sensitiveValues.Count == 0)
        {
            return value;
        }

        var redacted = value;
        foreach (var sensitiveValue in sensitiveValues
                     .Where(static item => !string.IsNullOrWhiteSpace(item))
                     .OrderByDescending(static item => item.Length))
        {
            redacted = redacted.Replace(sensitiveValue, RedactionToken, StringComparison.Ordinal);
        }

        return redacted;
    }
}
