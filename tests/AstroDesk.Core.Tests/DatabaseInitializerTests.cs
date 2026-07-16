using AstroDesk.Data;
using AstroDesk.Data.Initialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AstroDesk.Core.Tests;

public sealed class DatabaseInitializerTests
{
    [Fact]
    public async Task InitializeAsync_AppliesMigrationsAndCreatesUsableDatabase()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "AstroDesk.Tests",
            Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directory, "astrodesk.db");

        try
        {
            var options = new DbContextOptionsBuilder<AstroDeskDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options;

            await using (var context = new AstroDeskDbContext(options))
            {
                var initializer = new DatabaseInitializer(
                    context,
                    NullLogger<DatabaseInitializer>.Instance);

                await initializer.InitializeAsync();

                Assert.Single(await context.Database.GetAppliedMigrationsAsync());
                Assert.Equal(6, await context.SavedLocations.CountAsync());
            }

            Assert.True(File.Exists(databasePath));
        }
        finally
        {
            DeleteIfPresent(databasePath);
            DeleteIfPresent(databasePath + "-wal");
            DeleteIfPresent(databasePath + "-shm");
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: false);
            }
        }
    }

    [Fact]
    public async Task InitializeAsync_WrapsSqliteFailureWithRecoveryGuidance()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "AstroDesk.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var options = new DbContextOptionsBuilder<AstroDeskDbContext>()
                .UseSqlite($"Data Source={directory};Pooling=False")
                .Options;
            await using var context = new AstroDeskDbContext(options);
            var initializer = new DatabaseInitializer(
                context,
                NullLogger<DatabaseInitializer>.Instance);

            var exception = await Assert.ThrowsAsync<DatabaseInitializationException>(
                () => initializer.InitializeAsync());

            Assert.Equal(DatabaseFailureKind.CannotOpen, exception.FailureKind);
            Assert.False(string.IsNullOrWhiteSpace(exception.RecoverySuggestion));
            Assert.Equal(Path.GetFullPath(directory), exception.DatabasePath);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: false);
            }
        }
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
