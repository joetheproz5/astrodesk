using System.Globalization;
using System.Text.RegularExpressions;
using AstroDesk.Device.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AstroDesk.Stacking;

/// <summary>
/// Drives <c>siril-cli</c> to register and stack a session's frames.
/// </summary>
/// <remarks>
/// Siril is an external tool configured by path, exactly like adb and scrcpy.
/// It is not bundled: it is GPL software with a large native dependency chain,
/// and shipping it would bloat the installer and complicate licensing.
/// </remarks>
public sealed partial class SirilStackEngine : IStackEngine
{
    private const string ScriptFileName = "astrodesk-stack.ssf";

    private readonly IProcessRunner _processRunner;
    private readonly ILogger<SirilStackEngine> _logger;

    public SirilStackEngine(
        IProcessRunner processRunner,
        ILogger<SirilStackEngine>? logger = null)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _logger = logger ?? NullLogger<SirilStackEngine>.Instance;
    }

    public string ExecutablePath { get; set; } = string.Empty;

    public bool IsAvailable =>
        ExecutablePath.Length > 0 && File.Exists(ExecutablePath);

    public async Task<StackResult> StackAsync(
        StackRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsAvailable)
        {
            return new StackResult(
                false,
                null,
                0,
                "Siril was not found. Set the siril-cli path in Settings to enable stacking.",
                string.Empty);
        }

        if (!Directory.Exists(request.WorkingDirectory))
        {
            return new StackResult(
                false,
                null,
                0,
                $"The stack folder '{request.WorkingDirectory}' does not exist.",
                string.Empty);
        }

        string extension = request.FrameExtension.TrimStart('.').ToLowerInvariant();
        int frameCount = Directory
            .EnumerateFiles(request.WorkingDirectory, $"*.{extension}", SearchOption.TopDirectoryOnly)
            .Count();

        if (frameCount < 2)
        {
            return new StackResult(
                false,
                null,
                frameCount,
                $"Stacking needs at least two .{extension} frames; found {frameCount}.",
                string.Empty);
        }

        string script = SirilScriptBuilder.Build(request, frameCount);
        string scriptPath = Path.Combine(request.WorkingDirectory, ScriptFileName);
        await File.WriteAllTextAsync(scriptPath, script, cancellationToken).ConfigureAwait(false);

        progress?.Report($"Registering and stacking {frameCount} frames…");

        ProcessExecutionResult result;
        try
        {
            result = await _processRunner
                .RunAsync(
                    new ProcessInvocation(
                        ExecutablePath,
                        ["-d", request.WorkingDirectory, "-s", scriptPath],
                        WorkingDirectory: request.WorkingDirectory,
                        OutputReceived: line => ReportLine(line, progress)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ToolProcessStartException exception)
        {
            _logger.LogWarning(exception, "Could not start Siril.");
            return new StackResult(false, null, 0, exception.Message, string.Empty);
        }

        string log = string.Concat(result.StandardOutput, Environment.NewLine, result.StandardError);
        int stacked = ParseStackedFrameCount(log, frameCount);
        string? output = FindOutput(request);

        // siril-cli exits 0 even when the script aborts: a failed registration
        // reports "Script execution failed" in the log and still returns success
        // to the shell. Trusting the exit code alone reports a stack that never
        // happened, so the log has to be consulted as well.
        if (ReportsFailure(log) || !result.Succeeded || output is null)
        {
            _logger.LogWarning(
                "Siril stacking failed with exit code {ExitCode}.",
                result.ExitCode);
            return new StackResult(
                false,
                null,
                stacked,
                DescribeFailure(result.ExitCode, log),
                log);
        }

        // Registration silently drops frames it cannot solve. Surfacing that is
        // the difference between "it worked" and "it worked on half your data".
        string message = stacked < frameCount
            ? $"Stacked {stacked} of {frameCount} frames. {frameCount - stacked} could not be registered."
            : $"Stacked {stacked} frames.";

        return new StackResult(true, output, stacked, message, log, FindPreview(request));
    }

    private void ReportLine(ProcessOutputLine line, IProgress<string>? progress)
    {
        if (line.Text.Length == 0)
        {
            return;
        }

        _logger.LogDebug("siril: {Line}", line.Text);
        if (line.Text.Contains("Registration", StringComparison.OrdinalIgnoreCase) ||
            line.Text.Contains("Stacking", StringComparison.OrdinalIgnoreCase) ||
            line.Text.Contains("Integration", StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report(line.Text);
        }
    }

    /// <summary>
    /// Where a run's products are written, newest layout first.
    /// </summary>
    /// <remarks>
    /// Results now live in the process folder rather than beside the captures,
    /// so that a later run does not convert the previous stack along with the
    /// frames. The run folder is still searched afterwards, so a stack made
    /// before that change is still found and displayed.
    /// </remarks>
    private static IEnumerable<string> OutputFolders(StackRequest request)
    {
        yield return Path.Combine(
            request.WorkingDirectory,
            SirilScriptBuilder.ProcessFolderName);
        yield return request.WorkingDirectory;
    }

    /// <summary>
    /// The stretched TIFF written beside the master, or null when Siril did not
    /// produce one.
    /// </summary>
    private static string? FindPreview(StackRequest request) =>
        OutputFolders(request)
            .Select(folder => Path.Combine(folder, $"{request.OutputName}_preview.tif"))
            .FirstOrDefault(File.Exists);

    private static string? FindOutput(StackRequest request)
    {
        // Siril's output extension depends on the configured FITS extension, so
        // match on the base name rather than assuming .fit.
        string[] candidates =
        [
            $"{request.OutputName}.fit",
            $"{request.OutputName}.fits",
            $"{request.OutputName}.fts",
            $"{request.OutputName}_preview.tif",
        ];

        foreach (string folder in OutputFolders(request))
        {
            foreach (string candidate in candidates)
            {
                string path = Path.Combine(folder, candidate);
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Reads how many frames survived registration out of Siril's log.
    /// </summary>
    public static int ParseStackedFrameCount(string log, int fallback)
    {
        ArgumentNullException.ThrowIfNull(log);

        Match match = StackedCountPattern().Match(log);
        if (match.Success &&
            int.TryParse(
                match.Groups[1].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsed))
        {
            return parsed;
        }

        return fallback;
    }

    /// <summary>
    /// Detects an aborted run from the log, because the exit code does not.
    /// </summary>
    public static bool ReportsFailure(string log)
    {
        ArgumentNullException.ThrowIfNull(log);

        return log.Contains("Script execution failed", StringComparison.OrdinalIgnoreCase)
               || log.Contains("Registration aborted", StringComparison.OrdinalIgnoreCase)
               || log.Contains("No image was registered", StringComparison.OrdinalIgnoreCase)
               || log.Contains("Stacking failed", StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeFailure(int exitCode, string log)
    {
        // Registration failing on most frames is the common real-world failure,
        // and it is actionable: it means too few stars were detected to solve.
        if (log.Contains("Cannot perform star matching", StringComparison.OrdinalIgnoreCase) ||
            log.Contains("No image was registered", StringComparison.OrdinalIgnoreCase))
        {
            return "Registration failed: not enough stars could be matched between frames. " +
                   "Try longer exposures, a wider or faster lens, or a darker site.";
        }

        if (log.Contains("not enough stars", StringComparison.OrdinalIgnoreCase))
        {
            return "Registration failed: too few stars detected. Try longer exposures, " +
                   "a wider lens, or a darker site.";
        }

        if (log.Contains("cannot open", StringComparison.OrdinalIgnoreCase) ||
            log.Contains("no such file", StringComparison.OrdinalIgnoreCase))
        {
            return "Siril could not read the captured frames. Check the stack folder.";
        }

        return $"Siril exited with code {exitCode}. See the log for details.";
    }

    [GeneratedRegex(
        @"[Ss]tacking\s+(\d+)\s+images?|(\d+)\s+images?\s+(?:were\s+)?stacked",
        RegexOptions.CultureInvariant)]
    private static partial Regex StackedCountPattern();
}
