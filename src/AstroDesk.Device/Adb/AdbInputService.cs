using System.Globalization;
using System.Text;
using AstroDesk.Device.Processes;

namespace AstroDesk.Device.Adb;

public enum AndroidKeyCode
{
    Home = 3,
    Back = 4,
    DpadUp = 19,
    DpadDown = 20,
    DpadLeft = 21,
    DpadRight = 22,
    VolumeUp = 24,
    VolumeDown = 25,
    Power = 26,
    Escape = 111,
    AppSwitch = 187,
}

public interface IAdbInputService
{
    Task TapAsync(string serial, int x, int y, CancellationToken cancellationToken = default);

    Task SwipeAsync(
        string serial,
        int startX,
        int startY,
        int endX,
        int endY,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    Task SendKeyAsync(
        string serial,
        AndroidKeyCode keyCode,
        CancellationToken cancellationToken = default);

    Task SendTextAsync(
        string serial,
        string text,
        CancellationToken cancellationToken = default);

    Task SetKeepAwakeAsync(
        string serial,
        bool enabled,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implements low-frequency ADB input as a fallback when forwarding to scrcpy fails.
/// </summary>
public sealed class AdbInputService : IAdbInputService
{
    private readonly IAdbCommandExecutor _executor;

    public AdbInputService(IAdbCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public Task TapAsync(
        string serial,
        int x,
        int y,
        CancellationToken cancellationToken = default)
    {
        ValidateSerialAndPoint(serial, x, y);
        return ExecuteRequiredAsync(
            serial,
            ["shell", "input", "tap", Invariant(x), Invariant(y)],
            "input tap",
            cancellationToken);
    }

    public Task SwipeAsync(
        string serial,
        int startX,
        int startY,
        int endX,
        int endY,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ValidateSerialAndPoint(serial, startX, startY);
        ValidatePoint(endX, endY);
        if (duration < TimeSpan.Zero || duration > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Swipe duration must be between zero and one minute.");
        }

        var milliseconds = Math.Max(0, (int)Math.Round(duration.TotalMilliseconds));
        return ExecuteRequiredAsync(
            serial,
            [
                "shell",
                "input",
                "swipe",
                Invariant(startX),
                Invariant(startY),
                Invariant(endX),
                Invariant(endY),
                Invariant(milliseconds),
            ],
            "input swipe",
            cancellationToken);
    }

    public Task SendKeyAsync(
        string serial,
        AndroidKeyCode keyCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        return ExecuteRequiredAsync(
            serial,
            ["shell", "input", "keyevent", Invariant((int)keyCode)],
            "input keyevent",
            cancellationToken);
    }

    public Task SendTextAsync(
        string serial,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return Task.CompletedTask;
        }

        return ExecuteRequiredAsync(
            serial,
            ["shell", "input", "text", AdbTextEncoder.Encode(text)],
            "input text",
            cancellationToken);
    }

    public Task SetKeepAwakeAsync(
        string serial,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        return ExecuteRequiredAsync(
            serial,
            ["shell", "svc", "power", "stayon", enabled ? "usb" : "false"],
            "svc power stayon",
            cancellationToken);
    }

    private async Task ExecuteRequiredAsync(
        string serial,
        IReadOnlyList<string> arguments,
        string commandName,
        CancellationToken cancellationToken)
    {
        var result = await _executor.ExecuteAsync(serial, arguments, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            var error = string.IsNullOrWhiteSpace(result.StandardError)
                ? "No diagnostic output was returned."
                : SensitiveDataRedactor.Redact(result.StandardError.Trim(), [serial]);
            throw new AdbCommandException(commandName, result.ExitCode, error);
        }
    }

    private static void ValidateSerialAndPoint(string serial, int x, int y)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ValidatePoint(x, y);
    }

    private static void ValidatePoint(int x, int y)
    {
        if (x < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if (y < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }
    }

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}

public static class AdbTextEncoder
{
    private const string ShellMetacharacters = "\\\"'&|;<>()$`!";

    public static string Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character == ' ')
            {
                builder.Append("%s");
            }
            else
            {
                if (ShellMetacharacters.Contains(character, StringComparison.Ordinal))
                {
                    builder.Append('\\');
                }

                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
