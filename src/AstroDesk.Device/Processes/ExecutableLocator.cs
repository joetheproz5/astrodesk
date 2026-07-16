using System.Collections.ObjectModel;

namespace AstroDesk.Device.Processes;

public sealed record ExecutableRequest(
    string FileName,
    string? ConfiguredPath = null,
    string? EnvironmentVariable = null,
    IReadOnlyList<string>? AdditionalSearchDirectories = null);

public interface IExecutableLocator
{
    string? Find(ExecutableRequest request);

    string Resolve(ExecutableRequest request);
}

public sealed class ExecutableNotFoundException : FileNotFoundException
{
    public ExecutableNotFoundException(string executableName, IReadOnlyList<string> searchedLocations)
        : base(CreateMessage(executableName, searchedLocations))
    {
        ExecutableName = executableName;
        SearchedLocations = searchedLocations;
    }

    public string ExecutableName { get; }

    public IReadOnlyList<string> SearchedLocations { get; }

    private static string CreateMessage(string executableName, IReadOnlyList<string> searchedLocations)
    {
        var searched = searchedLocations.Count == 0
            ? "No valid search locations were available."
            : $"Searched: {string.Join(", ", searchedLocations)}.";
        return $"Could not find {executableName}. Configure its executable path or add it to PATH. {searched}";
    }
}

/// <summary>
/// Resolves an executable from a saved path, an environment variable, app-local
/// directories, or PATH. It deliberately contains no machine-specific install path.
/// </summary>
public sealed class ExecutableLocator : IExecutableLocator
{
    public string? Find(ExecutableRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        foreach (var candidate in GetCandidates(request, searchedLocations: null))
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    public string Resolve(ExecutableRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var searched = new List<string>();
        foreach (var candidate in GetCandidates(request, searched))
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new ExecutableNotFoundException(request.FileName, new ReadOnlyCollection<string>(searched));
    }

    private static void Validate(ExecutableRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new ArgumentException("An executable file name is required.", nameof(request));
        }
    }

    private static IEnumerable<string> GetCandidates(
        ExecutableRequest request,
        ICollection<string>? searchedLocations)
    {
        var names = GetExecutableNames(request.FileName);
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var configured in EnumerateConfiguredValues(request))
        {
            foreach (var candidate in ExpandPath(configured, names))
            {
                if (TryYield(candidate, yielded, searchedLocations, out var value))
                {
                    yield return value;
                }
            }
        }

        var directories = new List<string>
        {
            AppContext.BaseDirectory,
            Environment.CurrentDirectory,
        };

        if (request.AdditionalSearchDirectories is not null)
        {
            directories.AddRange(request.AdditionalSearchDirectories.Where(static value => !string.IsNullOrWhiteSpace(value)));
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            directories.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        foreach (var directory in directories)
        {
            foreach (var name in names)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(directory, name);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (TryYield(candidate, yielded, searchedLocations, out var value))
                {
                    yield return value;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateConfiguredValues(ExecutableRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ConfiguredPath))
        {
            yield return Environment.ExpandEnvironmentVariables(request.ConfiguredPath.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.EnvironmentVariable))
        {
            var value = Environment.GetEnvironmentVariable(request.EnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return Environment.ExpandEnvironmentVariables(value.Trim());
            }
        }
    }

    private static IEnumerable<string> ExpandPath(string configuredValue, IReadOnlyList<string> names)
    {
        if (Directory.Exists(configuredValue) || EndsWithDirectorySeparator(configuredValue))
        {
            foreach (var name in names)
            {
                yield return Path.Combine(configuredValue, name);
            }

            yield break;
        }

        yield return configuredValue;
    }

    private static IReadOnlyList<string> GetExecutableNames(string fileName)
    {
        if (Path.HasExtension(fileName))
        {
            return [fileName];
        }

        var extensions = Environment.GetEnvironmentVariable("PATHEXT")
            ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [".EXE", ".CMD", ".BAT"];

        return OperatingSystem.IsWindows()
            ? new[] { fileName }.Concat(extensions.Select(extension => fileName + extension.ToLowerInvariant())).ToArray()
            : [fileName];
    }

    private static bool TryYield(
        string candidate,
        ISet<string> yielded,
        ICollection<string>? searchedLocations,
        out string value)
    {
        value = candidate;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(candidate);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (!yielded.Add(fullPath))
        {
            return false;
        }

        searchedLocations?.Add(fullPath);
        value = fullPath;
        return true;
    }

    private static bool EndsWithDirectorySeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar);
}
