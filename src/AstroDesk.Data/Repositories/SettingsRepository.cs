using AstroDesk.Core.Entities;
using AstroDesk.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AstroDesk.Data.Repositories;

public sealed class SettingsRepository(AstroDeskDbContext dbContext)
    : ISettingsRepository
{
    private readonly AstroDeskDbContext _dbContext =
        dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    public Task<AppSetting?> GetAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        string normalizedKey = NormalizeKey(key);

        return _dbContext.AppSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                setting => setting.Key == normalizedKey,
                cancellationToken);
    }

    public async Task<IReadOnlyList<AppSetting>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.AppSettings
            .AsNoTracking()
            .OrderBy(setting => setting.Category)
            .ThenBy(setting => setting.Key)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SetAsync(
        AppSetting setting,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setting);
        string normalizedKey = NormalizeKey(setting.Key);

        try
        {
            AppSetting? existing = await _dbContext.AppSettings
                .SingleOrDefaultAsync(
                    candidate => candidate.Key == normalizedKey,
                    cancellationToken)
                .ConfigureAwait(false);

            if (existing is null)
            {
                await _dbContext.AppSettings
                    .AddAsync(setting, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (!ReferenceEquals(existing, setting))
            {
                existing.SetValue(
                    setting.Value,
                    setting.ValueType,
                    setting.Category);
            }

            await _dbContext.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }

    public async Task RemoveAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        string normalizedKey = NormalizeKey(key);

        await _dbContext.AppSettings
            .Where(setting => setting.Key == normalizedKey)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static string NormalizeKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return key.Trim();
    }
}
