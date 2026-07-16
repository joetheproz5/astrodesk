using AstroDesk.Core.Entities;
using AstroDesk.Core.Interfaces;
using AstroDesk.Core.Services;

namespace AstroDesk.Core.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task SetThenGet_RoundTripsTypedValue()
    {
        var repository = new InMemorySettingsRepository();
        var service = new SettingsService(repository);

        await service.SetAsync("scrcpy.maxFps", 60, "scrcpy");
        var value = await service.GetAsync("scrcpy.maxFps", 30);

        Assert.Equal(60, value);
        Assert.Equal("scrcpy", (await repository.GetAsync("scrcpy.maxFps"))?.Category);
    }

    [Fact]
    public async Task SetExistingSetting_UpdatesInsteadOfDuplicating()
    {
        var repository = new InMemorySettingsRepository();
        var service = new SettingsService(repository);

        await service.SetAsync("display.redMode", false);
        var originalId = (await repository.GetAsync("display.redMode"))!.Id;
        await service.SetAsync("display.redMode", true);

        Assert.Single(await repository.GetAllAsync());
        Assert.Equal(originalId, (await repository.GetAsync("display.redMode"))!.Id);
        Assert.True(await service.GetAsync("display.redMode", false));
    }

    [Fact]
    public async Task GetMissingSetting_ReturnsDefault()
    {
        var service = new SettingsService(new InMemorySettingsRepository());

        var value = await service.GetAsync("missing", "fallback");

        Assert.Equal("fallback", value);
    }

    private sealed class InMemorySettingsRepository : ISettingsRepository
    {
        private readonly Dictionary<string, AppSetting> _settings =
            new(StringComparer.OrdinalIgnoreCase);

        public Task<AppSetting?> GetAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            _settings.TryGetValue(key, out var value);
            return Task.FromResult(value);
        }

        public Task<IReadOnlyList<AppSetting>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AppSetting>>(_settings.Values.ToList());

        public Task SetAsync(
            AppSetting setting,
            CancellationToken cancellationToken = default)
        {
            _settings[setting.Key] = setting;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _settings.Remove(key);
            return Task.CompletedTask;
        }
    }
}
