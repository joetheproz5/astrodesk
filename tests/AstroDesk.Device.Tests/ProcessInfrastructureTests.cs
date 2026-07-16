using AstroDesk.Device.Processes;

namespace AstroDesk.Device.Tests;

public sealed class ProcessInfrastructureTests
{
    [Fact]
    public void Redact_RemovesEveryOccurrenceOfSensitiveSerial()
    {
        var result = SensitiveDataRedactor.Redact(
            "device SERIAL-123 disconnected; retry SERIAL-123",
            ["SERIAL-123"]);

        Assert.Equal(
            "device <redacted> disconnected; retry <redacted>",
            result);
    }

    [Fact]
    public void ExecutableLocator_UsesConfiguredDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"AstroDesk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "adb.exe");
        File.WriteAllBytes(executable, []);

        try
        {
            var locator = new ExecutableLocator();
            var result = locator.Resolve(new ExecutableRequest("adb.exe", directory));

            Assert.Equal(Path.GetFullPath(executable), result);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ExecutableLocator_ProvidesUsefulMissingToolError()
    {
        var locator = new ExecutableLocator();
        var name = $"missing-{Guid.NewGuid():N}.exe";

        var exception = Assert.Throws<ExecutableNotFoundException>(
            () => locator.Resolve(new ExecutableRequest(name)));

        Assert.Contains(name, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Configure", exception.Message, StringComparison.Ordinal);
        Assert.NotEmpty(exception.SearchedLocations);
    }
}
