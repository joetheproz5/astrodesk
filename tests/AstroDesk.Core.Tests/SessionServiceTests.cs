using AstroDesk.Core.Entities;
using AstroDesk.Core.Enums;
using AstroDesk.Core.Interfaces;
using AstroDesk.Core.Models;
using AstroDesk.Core.Services;

namespace AstroDesk.Core.Tests;

public sealed class SessionServiceTests
{
    [Fact]
    public async Task CreateAsync_PersistsAndOptionallyStartsSession()
    {
        var repository = new InMemorySessionRepository();
        var service = new SessionService(repository);

        var session = await service.CreateAsync(
            ShootingSessionTests.CreateRequest(),
            startImmediately: true);

        Assert.Equal(SessionStatus.Active, session.Status);
        Assert.Same(session, await repository.GetByIdAsync(session.Id));
    }

    [Fact]
    public async Task Mutations_ArePersistedThroughRepository()
    {
        var repository = new InMemorySessionRepository();
        var service = new SessionService(repository);
        var session = await service.CreateAsync(ShootingSessionTests.CreateRequest(), startImmediately: true);

        await service.AddFrameAsync(session.Id, 2);
        await service.RemoveFrameAsync(session.Id);
        await service.EndAsync(session.Id, 50, 500);

        Assert.Equal(1, session.FrameCount);
        Assert.Equal(SessionStatus.Completed, session.Status);
        Assert.Equal(3, repository.UpdateCount);
    }

    private sealed class InMemorySessionRepository : IShootingSessionRepository
    {
        private readonly Dictionary<Guid, ShootingSession> _sessions = [];

        public int UpdateCount { get; private set; }

        public Task<ShootingSession?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            _sessions.TryGetValue(id, out var session);
            return Task.FromResult(session);
        }

        public Task<IReadOnlyList<ShootingSession>> ListAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ShootingSession>>(_sessions.Values.ToList());

        public Task AddAsync(
            ShootingSession entity,
            CancellationToken cancellationToken = default)
        {
            _sessions.Add(entity.Id, entity);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(
            ShootingSession entity,
            CancellationToken cancellationToken = default)
        {
            UpdateCount++;
            _sessions[entity.Id] = entity;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            ShootingSession entity,
            CancellationToken cancellationToken = default)
        {
            _sessions.Remove(entity.Id);
            return Task.CompletedTask;
        }

        public Task<ShootingSession?> GetCurrentAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _sessions.Values.FirstOrDefault(
                    session => session.Status is SessionStatus.Active or SessionStatus.Paused));

        public Task<IReadOnlyList<ShootingSession>> SearchAsync(
            SessionSearchCriteria criteria,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ShootingSession>>(_sessions.Values.ToList());
    }
}
