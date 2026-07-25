using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AstroDesk.Device.Processes;

/// <summary>
/// Runs redirected child processes without invoking a command shell.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private readonly ILogger<ProcessRunner> _logger;

    public ProcessRunner(ILogger<ProcessRunner>? logger = null)
    {
        _logger = logger ?? NullLogger<ProcessRunner>.Instance;
    }

    /// <summary>
    /// How long a one-shot tool may run before it is killed.
    /// </summary>
    /// <remarks>
    /// Every adb call used to wait on the app's lifetime token alone, so a
    /// device that stopped answering - an unreachable wireless phone above all -
    /// blocked forever. That wedged the UI rather than the tool: the command
    /// driving it stays disabled while its task runs, and scrcpy's lifecycle
    /// semaphore meant the stuck call also blocked stop and reconnect, leaving
    /// all three buttons greyed out with no way back except restarting the app.
    /// Generous enough for a slow pull over USB, short enough to fail visibly.
    /// </remarks>
    public static TimeSpan DefaultTimeout { get; } = TimeSpan.FromSeconds(30);

    public async Task<ProcessExecutionResult> RunAsync(
        ProcessInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();

        TimeSpan timeout = invocation.Timeout ?? DefaultTimeout;
        using CancellationTokenSource timeoutSource = new(timeout);
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        using var process = CreateProcess(invocation);
        var stopwatch = Stopwatch.StartNew();
        LogStart(invocation);

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The operating system declined to start the process.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ToolProcessStartException(invocation.FileName, exception);
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stdoutTask = PumpAsync(process.StandardOutput, ProcessOutputStream.StandardOutput, stdout, invocation);
        var stderrTask = PumpAsync(process.StandardError, ProcessOutputStream.StandardError, stderr, invocation);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await WaitAfterKillAsync(process).ConfigureAwait(false);
            await AwaitPumpsQuietlyAsync(stdoutTask, stderrTask).ConfigureAwait(false);

            // Only the timeout fired, so this is the tool wedging rather than
            // the user or the app shutting down. Saying so lets the caller show
            // something better than a bare cancellation.
            if (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "External tool {ToolName} exceeded {Timeout} and was stopped.",
                    Path.GetFileName(invocation.FileName),
                    timeout);
                throw new ToolProcessTimeoutException(invocation.FileName, timeout);
            }

            throw;
        }
        finally
        {
            stopwatch.Stop();
        }

        _logger.LogDebug(
            "External tool {ToolName} exited with code {ExitCode} after {ElapsedMilliseconds} ms.",
            Path.GetFileName(invocation.FileName),
            process.ExitCode,
            stopwatch.ElapsedMilliseconds);

        return new ProcessExecutionResult(
            process.ExitCode,
            stdout.ToString(),
            stderr.ToString(),
            stopwatch.Elapsed);
    }

    public Task<IChildProcess> StartAsync(
        ProcessInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();

        var process = CreateProcess(invocation);
        LogStart(invocation);
        try
        {
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException("The operating system declined to start the process.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            process.Dispose();
            throw new ToolProcessStartException(invocation.FileName, exception);
        }

        IChildProcess child = new ChildProcess(process, invocation, _logger);
        return Task.FromResult(child);
    }

    private static Process CreateProcess(ProcessInvocation invocation)
    {
        if (string.IsNullOrWhiteSpace(invocation.FileName))
        {
            throw new ArgumentException("A process executable is required.", nameof(invocation));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = invocation.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = invocation.CreateNoWindow,
            WorkingDirectory = string.IsNullOrWhiteSpace(invocation.WorkingDirectory)
                ? Path.GetDirectoryName(invocation.FileName) ?? Environment.CurrentDirectory
                : invocation.WorkingDirectory,
        };

        foreach (var argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (invocation.EnvironmentVariables is not null)
        {
            foreach (var (name, value) in invocation.EnvironmentVariables)
            {
                startInfo.Environment[name] = value;
            }
        }

        return new Process { StartInfo = startInfo, EnableRaisingEvents = true };
    }

    private async Task PumpAsync(
        StreamReader reader,
        ProcessOutputStream stream,
        StringBuilder destination,
        ProcessInvocation invocation)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            destination.AppendLine(line);
            PublishLine(stream, line, invocation);
        }
    }

    private void PublishLine(ProcessOutputStream stream, string line, ProcessInvocation invocation)
    {
        var safeLine = SensitiveDataRedactor.Redact(line, invocation.SensitiveValues);
        if (stream == ProcessOutputStream.StandardError)
        {
            _logger.LogWarning("{ToolName}: {ToolOutput}", Path.GetFileName(invocation.FileName), safeLine);
        }
        else
        {
            _logger.LogDebug("{ToolName}: {ToolOutput}", Path.GetFileName(invocation.FileName), safeLine);
        }

        try
        {
            invocation.OutputReceived?.Invoke(new ProcessOutputLine(stream, safeLine));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "A process output subscriber threw an exception.");
        }
    }

    private void LogStart(ProcessInvocation invocation)
    {
        var command = string.Join(
            " ",
            invocation.Arguments.Select(argument =>
                SensitiveDataRedactor.Redact(argument, invocation.SensitiveValues)));
        _logger.LogDebug(
            "Starting external tool {ToolName} with arguments {Arguments}.",
            Path.GetFileName(invocation.FileName),
            command);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static async Task WaitAfterKillAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async Task AwaitPumpsQuietlyAsync(params Task[] pumps)
    {
        try
        {
            await Task.WhenAll(pumps).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }
    }

    private sealed class ChildProcess : IChildProcess
    {
        private readonly Process _process;
        private readonly ILogger _logger;
        private readonly Task _stdoutTask;
        private readonly Task _stderrTask;
        private int _disposed;

        public ChildProcess(Process process, ProcessInvocation invocation, ILogger logger)
        {
            _process = process;
            _logger = logger;
            _stdoutTask = PumpChildAsync(process.StandardOutput, ProcessOutputStream.StandardOutput, invocation, logger);
            _stderrTask = PumpChildAsync(process.StandardError, ProcessOutputStream.StandardError, invocation, logger);
        }

        public int Id => _process.Id;

        public bool HasExited
        {
            get
            {
                try
                {
                    return _process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }
        }

        public int? ExitCode
        {
            get
            {
                try
                {
                    return _process.HasExited ? _process.ExitCode : null;
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }
        }

        public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
        {
            await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(_stdoutTask, _stderrTask).ConfigureAwait(false);
            return _process.ExitCode;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (!HasExited)
            {
                TryKill(_process);
            }

            await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(_stdoutTask, _stderrTask).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (!HasExited)
            {
                TryKill(_process);
            }

            try
            {
                await _process.WaitForExitAsync().ConfigureAwait(false);
                await Task.WhenAll(_stdoutTask, _stderrTask).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
            {
                _logger.LogDebug(exception, "Child process was already disposed.");
            }
            finally
            {
                _process.Dispose();
            }
        }

        private static async Task PumpChildAsync(
            StreamReader reader,
            ProcessOutputStream stream,
            ProcessInvocation invocation,
            ILogger logger)
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                var safeLine = SensitiveDataRedactor.Redact(line, invocation.SensitiveValues);
                if (stream == ProcessOutputStream.StandardError)
                {
                    logger.LogWarning("{ToolName}: {ToolOutput}", Path.GetFileName(invocation.FileName), safeLine);
                }
                else
                {
                    logger.LogDebug("{ToolName}: {ToolOutput}", Path.GetFileName(invocation.FileName), safeLine);
                }

                try
                {
                    invocation.OutputReceived?.Invoke(new ProcessOutputLine(stream, safeLine));
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "A process output subscriber threw an exception.");
                }
            }
        }
    }
}
