using AstroDesk.Core.Enums;
using AstroDesk.Core.Models;
using AstroDesk.Core.Services;
using AstroDesk.Data;
using AstroDesk.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AstroDesk.Core.Tests;

public sealed class RepositoryIntegrationTests
{
    [Fact]
    public async Task SettingsRepository_PersistsTypedSettingsAcrossContexts()
    {
        await using var database = await SqliteDatabase.CreateAsync();

        await using (var writeContext = database.CreateContext())
        {
            var service = new SettingsService(new SettingsRepository(writeContext));
            await service.SetAsync("capture.defaultExposure", TimeSpan.FromSeconds(20), "capture");
            await service.SetAsync("capture.defaultFrames", 120, "capture");
        }

        await using (var readContext = database.CreateContext())
        {
            var service = new SettingsService(new SettingsRepository(readContext));

            Assert.Equal(
                TimeSpan.FromSeconds(20),
                await service.GetAsync("capture.defaultExposure", TimeSpan.Zero));
            Assert.Equal(120, await service.GetAsync("capture.defaultFrames", 0));
        }
    }

    [Fact]
    public async Task SessionService_CreatesAndLoadsSessionThroughSqliteRepository()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        Guid sessionId;

        await using (var writeContext = database.CreateContext())
        {
            var service = new SessionService(new ShootingSessionRepository(writeContext));
            var session = await service.CreateAsync(
                ShootingSessionTests.CreateRequest(),
                startImmediately: true);
            sessionId = session.Id;
            await service.AddFrameAsync(sessionId, 3);
        }

        await using (var readContext = database.CreateContext())
        {
            var repository = new ShootingSessionRepository(readContext);
            var loaded = await repository.GetByIdAsync(sessionId);

            Assert.NotNull(loaded);
            Assert.Equal(SessionStatus.Active, loaded.Status);
            Assert.Equal(3, loaded.FrameCount);
            Assert.Equal("Faraya", loaded.LocationName);
            Assert.Equal(TimeSpan.FromMinutes(1), loaded.TotalIntegrationTime);
            Assert.Equal(sessionId, (await repository.GetCurrentAsync())?.Id);
        }
    }

    [Fact]
    public async Task SearchAsync_FiltersBySessionTypeAndLocation()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var repository = new ShootingSessionRepository(context);
        await repository.AddAsync(new(ShootingSessionTests.CreateRequest()));
        await repository.AddAsync(
            new(
                ShootingSessionTests.CreateRequest() with
                {
                    TargetName = "Moon",
                    SessionType = SessionType.Moon,
                    LocationName = "Beirut",
                }));

        var results = await repository.SearchAsync(
            new SessionSearchCriteria(
                LocationName: "Faraya",
                SessionType: SessionType.MilkyWay));

        var result = Assert.Single(results);
        Assert.Equal("Milky Way core", result.TargetName);
    }

    [Fact]
    public async Task UpdateAsync_PersistsChildrenAddedToDetachedSessionGraph()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        Guid sessionId;

        await using (var createContext = database.CreateContext())
        {
            var repository = new ShootingSessionRepository(createContext);
            var session = new AstroDesk.Core.Entities.ShootingSession(
                ShootingSessionTests.CreateRequest());
            sessionId = session.Id;
            await repository.AddAsync(session);
        }

        await using (var updateContext = database.CreateContext())
        {
            var repository = new ShootingSessionRepository(updateContext);
            var detached = await repository.GetByIdAsync(sessionId);
            Assert.NotNull(detached);
            var noteTime = detached.UpdatedAt.AddSeconds(1);
            detached.AddNote("Focus rechecked.", SessionNoteKind.Observation, noteTime);
            detached.AddScreenshot(
                @"sessions\faraya\screenshots\focus-check.png",
                includesOverlays: false,
                noteTime.AddSeconds(1));

            await repository.UpdateAsync(detached);
        }

        await using (var verifyContext = database.CreateContext())
        {
            var repository = new ShootingSessionRepository(verifyContext);
            var reloaded = await repository.GetByIdAsync(sessionId);

            Assert.NotNull(reloaded);
            Assert.Equal("Focus rechecked.", Assert.Single(reloaded.Notes).Content);
            Assert.EndsWith(
                "focus-check.png",
                Assert.Single(reloaded.Screenshots).FilePath,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task DatabaseConstraint_AllowsOnlyOneCurrentSession()
    {
        await using var database = await SqliteDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var repository = new ShootingSessionRepository(context);
        var first = new AstroDesk.Core.Entities.ShootingSession(ShootingSessionTests.CreateRequest());
        first.Start(first.CreatedAt.AddMilliseconds(1));
        await repository.AddAsync(first);

        var second = new AstroDesk.Core.Entities.ShootingSession(
            ShootingSessionTests.CreateRequest() with { TargetName = "Second target" });
        second.Start(second.CreatedAt.AddMilliseconds(1));

        await Assert.ThrowsAsync<DbUpdateException>(() => repository.AddAsync(second));
    }

    private sealed class SqliteDatabase : IAsyncDisposable
    {
        private readonly DbContextOptions<AstroDeskDbContext> _options;

        private SqliteDatabase(
            SqliteConnection connection,
            DbContextOptions<AstroDeskDbContext> options)
        {
            Connection = connection;
            _options = options;
        }

        public SqliteConnection Connection { get; }

        public AstroDeskDbContext CreateContext() => new(_options);

        public static async Task<SqliteDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AstroDeskDbContext>()
                .UseSqlite(connection)
                .Options;
            await using var context = new AstroDeskDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new SqliteDatabase(connection, options);
        }

        public async ValueTask DisposeAsync()
        {
            await Connection.DisposeAsync();
        }
    }
}
