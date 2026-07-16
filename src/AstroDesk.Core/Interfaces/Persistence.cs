using AstroDesk.Core.Entities;
using AstroDesk.Core.Models;

namespace AstroDesk.Core.Interfaces;

public interface IRepository<TEntity>
    where TEntity : EntityBase
{
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TEntity>> ListAsync(CancellationToken cancellationToken = default);

    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

    Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default);
}

public interface IShootingSessionRepository : IRepository<ShootingSession>
{
    Task<ShootingSession?> GetCurrentAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShootingSession>> SearchAsync(
        SessionSearchCriteria criteria,
        CancellationToken cancellationToken = default);
}

public interface ISettingsRepository
{
    Task<AppSetting?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppSetting>> GetAllAsync(CancellationToken cancellationToken = default);

    Task SetAsync(AppSetting setting, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}

public interface ISettingsService
{
    Task<T?> GetAsync<T>(
        string key,
        T? defaultValue = default,
        CancellationToken cancellationToken = default);

    Task SetAsync<T>(
        string key,
        T value,
        string? category = null,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}

public interface ISessionService
{
    Task<ShootingSession> CreateAsync(
        CreateSessionRequest request,
        bool startImmediately = false,
        CancellationToken cancellationToken = default);

    Task<ShootingSession> StartAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<ShootingSession> PauseAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<ShootingSession> ResumeAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<ShootingSession> EndAsync(
        Guid sessionId,
        int? batteryPercentageAtEnd = null,
        long? storageBytesAtEnd = null,
        CancellationToken cancellationToken = default);

    Task<ShootingSession> AddFrameAsync(
        Guid sessionId,
        int count = 1,
        CancellationToken cancellationToken = default);

    Task<ShootingSession> RemoveFrameAsync(
        Guid sessionId,
        int count = 1,
        CancellationToken cancellationToken = default);
}
