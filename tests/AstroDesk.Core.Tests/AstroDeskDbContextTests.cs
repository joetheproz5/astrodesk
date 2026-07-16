using AstroDesk.Core.Entities;
using AstroDesk.Core.Enums;
using AstroDesk.Core.Models;
using AstroDesk.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AstroDesk.Core.Tests;

public sealed class AstroDeskDbContextTests
{
    [Fact]
    public async Task EnsureCreated_CreatesSchemaAndLebanonExampleLocations()
    {
        await using var fixture = await SqliteFixture.CreateAsync();

        var locations = await fixture.Context.SavedLocations
            .OrderBy(location => location.Name)
            .ToListAsync();

        Assert.Equal(6, locations.Count);
        Assert.Contains(locations, location => location.Name == "Faraya" && location.IsDefault);
        Assert.Contains(locations, location => location.Name == "Beirut");
    }

    [Fact]
    public async Task SessionGraph_RoundTripsThroughSqlite()
    {
        await using var fixture = await SqliteFixture.CreateAsync();
        var session = new ShootingSession(ShootingSessionTests.CreateRequest());
        var start = session.CreatedAt.AddSeconds(1);
        session.Start(start);
        session.RecordFrames(2, start.AddSeconds(1));
        session.AddNote("Thin clouds arrived.", SessionNoteKind.Observation, start.AddSeconds(2));
        session.AddScreenshot(
            @"sessions\faraya\screenshots\preview.png",
            includesOverlays: true,
            start.AddSeconds(3));

        var weather = new WeatherConditions(
            TemperatureCelsius: 8,
            HumidityPercent: 72,
            WindSpeedKilometersPerHour: 5,
            CloudCoverPercent: 15,
            VisibilityKilometers: 20,
            DewPointCelsius: 3.2,
            DewRisk: DewRisk.Moderate,
            ProviderName: "Test",
            ObservedAt: start);
        session.SetWeatherSnapshot(
            new SessionWeatherSnapshot(weather, start),
            start.AddSeconds(4));

        fixture.Context.ShootingSessions.Add(session);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var loaded = await fixture.Context.ShootingSessions
            .Include(value => value.Notes)
            .Include(value => value.Screenshots)
            .Include(value => value.WeatherSnapshot)
            .SingleAsync(value => value.Id == session.Id);

        Assert.Equal(2, loaded.FrameCount);
        Assert.Single(loaded.Notes);
        Assert.Single(loaded.Screenshots);
        Assert.Equal(72, loaded.WeatherSnapshot?.HumidityPercent);
    }

    private sealed class SqliteFixture : IAsyncDisposable
    {
        private SqliteFixture(SqliteConnection connection, AstroDeskDbContext context)
        {
            Connection = connection;
            Context = context;
        }

        public SqliteConnection Connection { get; }

        public AstroDeskDbContext Context { get; }

        public static async Task<SqliteFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AstroDeskDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new AstroDeskDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new SqliteFixture(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
