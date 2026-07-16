using System.Text.Json;
using AstroDesk.Core.Entities;
using AstroDesk.Core.Enums;
using AstroDesk.Infrastructure.Storage;

namespace AstroDesk.Core.Tests;

public sealed class SessionAssetServiceIntegrationTests
{
    [Fact]
    public async Task SessionAssetService_WritesPortableAndExplicitExports()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            $"AstroDesk-session-export-{Guid.NewGuid():N}");

        try
        {
            AppPaths paths = new(root);
            paths.EnsureCreated();
            SessionAssetService assets = new(paths);
            ShootingSession session = new(ShootingSessionTests.CreateRequest());
            DateTimeOffset start = session.CreatedAt.AddMinutes(1);
            session.Start(start);
            session.RecordFrames(3, start.AddMinutes(2));
            session.AddNote(
                "Focus rechecked before the final frames.",
                SessionNoteKind.Observation,
                start.AddMinutes(3));
            session.End(
                start.AddMinutes(5),
                batteryPercentageAtEnd: 84,
                storageBytesAtEnd: 900);

            await assets.WritePortableFilesAsync(session);
            string exportJson = await assets.ExportJsonAsync(session);
            string exportMarkdown = await assets.ExportMarkdownAsync(session);
            string folder = assets.GetOrCreateSessionFolder(session);

            Assert.True(File.Exists(Path.Combine(folder, "session.json")));
            Assert.True(File.Exists(Path.Combine(folder, "notes.txt")));
            Assert.True(Directory.Exists(Path.Combine(folder, "screenshots")));
            Assert.True(Directory.Exists(Path.Combine(folder, "export")));
            Assert.Equal(
                Path.Combine(folder, "export", "session.json"),
                exportJson);
            Assert.Equal(
                Path.Combine(folder, "export", "summary.md"),
                exportMarkdown);
            Assert.Contains(
                session.Id.ToString("N")[..8],
                Path.GetFileName(folder),
                StringComparison.OrdinalIgnoreCase);

            using JsonDocument json = JsonDocument.Parse(
                await File.ReadAllTextAsync(exportJson));
            Assert.Equal(
                "Milky Way core",
                json.RootElement.GetProperty("targetName").GetString());
            Assert.Equal(
                3,
                json.RootElement.GetProperty("frameCount").GetInt32());
            Assert.Single(
                json.RootElement.GetProperty("notes").EnumerateArray());

            string notes = await File.ReadAllTextAsync(
                Path.Combine(folder, "notes.txt"));
            Assert.Contains("Focus rechecked", notes, StringComparison.Ordinal);
            string markdown = await File.ReadAllTextAsync(exportMarkdown);
            Assert.Contains("# Milky Way core", markdown, StringComparison.Ordinal);
            Assert.Contains("Frames: 3 / 120", markdown, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
