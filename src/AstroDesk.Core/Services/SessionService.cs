using AstroDesk.Core.Entities;
using AstroDesk.Core.Interfaces;
using AstroDesk.Core.Models;

namespace AstroDesk.Core.Services;

public sealed class SessionService : ISessionService
{
    private readonly IShootingSessionRepository _repository;
    private readonly TimeProvider _timeProvider;

    public SessionService(
        IShootingSessionRepository repository,
        TimeProvider? timeProvider = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ShootingSession> CreateAsync(
        CreateSessionRequest request,
        bool startImmediately = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = new ShootingSession(request);
        if (startImmediately)
        {
            session.Start(_timeProvider.GetUtcNow());
        }

        await _repository.AddAsync(session, cancellationToken).ConfigureAwait(false);
        return session;
    }

    public Task<ShootingSession> StartAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        MutateAsync(sessionId, session => session.Start(_timeProvider.GetUtcNow()), cancellationToken);

    public Task<ShootingSession> PauseAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        MutateAsync(sessionId, session => session.Pause(_timeProvider.GetUtcNow()), cancellationToken);

    public Task<ShootingSession> ResumeAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        MutateAsync(sessionId, session => session.Resume(_timeProvider.GetUtcNow()), cancellationToken);

    public Task<ShootingSession> EndAsync(
        Guid sessionId,
        int? batteryPercentageAtEnd = null,
        long? storageBytesAtEnd = null,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            sessionId,
            session => session.End(
                _timeProvider.GetUtcNow(),
                batteryPercentageAtEnd,
                storageBytesAtEnd),
            cancellationToken);

    public Task<ShootingSession> AddFrameAsync(
        Guid sessionId,
        int count = 1,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            sessionId,
            session => session.RecordFrames(count, _timeProvider.GetUtcNow()),
            cancellationToken);

    public Task<ShootingSession> RemoveFrameAsync(
        Guid sessionId,
        int count = 1,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            sessionId,
            session => session.CorrectFrames(count, _timeProvider.GetUtcNow()),
            cancellationToken);

    private async Task<ShootingSession> MutateAsync(
        Guid sessionId,
        Action<ShootingSession> mutation,
        CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session ID is required.", nameof(sessionId));
        }

        var session = await _repository.GetByIdAsync(sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Shooting session '{sessionId}' was not found.");
        mutation(session);
        await _repository.UpdateAsync(session, cancellationToken).ConfigureAwait(false);
        return session;
    }
}
