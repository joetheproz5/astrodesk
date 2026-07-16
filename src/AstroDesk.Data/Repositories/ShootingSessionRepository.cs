using AstroDesk.Core.Entities;
using AstroDesk.Core.Enums;
using AstroDesk.Core.Interfaces;
using AstroDesk.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace AstroDesk.Data.Repositories;

public sealed class ShootingSessionRepository(AstroDeskDbContext dbContext)
    : Repository<ShootingSession>(dbContext), IShootingSessionRepository
{
    public override Task<ShootingSession?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ValidateId(id);

        return CompleteQuery()
            .SingleOrDefaultAsync(session => session.Id == id, cancellationToken);
    }

    public override async Task<IReadOnlyList<ShootingSession>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        return await CompleteQuery()
            .OrderByDescending(session => session.StartTime ?? session.CreatedAt)
            .ThenByDescending(session => session.CreatedAt)
            .ThenByDescending(session => session.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public override async Task UpdateAsync(
        ShootingSession entity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ValidateId(entity.Id);
        DbContext.ChangeTracker.Clear();

        await using var transaction = await DbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            bool sessionExists = await DbContext.ShootingSessions
                .AsNoTracking()
                .AnyAsync(
                    session => session.Id == entity.Id,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!sessionExists)
            {
                throw new KeyNotFoundException(
                    $"Shooting session '{entity.Id}' was not found.");
            }

            List<Guid> existingNoteIdList = await DbContext.SessionNotes
                .AsNoTracking()
                .Where(note => note.ShootingSessionId == entity.Id)
                .Select(note => note.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            var existingNoteIds = new HashSet<Guid>(existingNoteIdList);

            List<Guid> existingScreenshotIdList =
                await DbContext.SessionScreenshots
                    .AsNoTracking()
                    .Where(
                        screenshot =>
                            screenshot.ShootingSessionId == entity.Id)
                    .Select(screenshot => screenshot.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
            var existingScreenshotIds =
                new HashSet<Guid>(existingScreenshotIdList);

            Guid? existingWeatherId = await DbContext.SessionWeatherSnapshots
                .AsNoTracking()
                .Where(snapshot => snapshot.ShootingSessionId == entity.Id)
                .Select(snapshot => (Guid?)snapshot.Id)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            Guid? existingAstronomyId =
                await DbContext.SessionAstronomySnapshots
                    .AsNoTracking()
                    .Where(
                        snapshot =>
                            snapshot.ShootingSessionId == entity.Id)
                    .Select(snapshot => (Guid?)snapshot.Id)
                    .SingleOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

            if (existingWeatherId is { } oldWeatherId &&
                entity.WeatherSnapshot?.Id != oldWeatherId)
            {
                await DbContext.SessionWeatherSnapshots
                    .Where(snapshot => snapshot.Id == oldWeatherId)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
                existingWeatherId = null;
            }

            if (existingAstronomyId is { } oldAstronomyId &&
                entity.AstronomySnapshot?.Id != oldAstronomyId)
            {
                await DbContext.SessionAstronomySnapshots
                    .Where(snapshot => snapshot.Id == oldAstronomyId)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
                existingAstronomyId = null;
            }

            Entities.Update(entity);

            if (entity.EquipmentProfile is not null)
            {
                DbContext.Entry(entity.EquipmentProfile).State =
                    EntityState.Unchanged;
            }

            foreach (SessionNote note in entity.Notes)
            {
                DbContext.Entry(note).State = existingNoteIds.Contains(note.Id)
                    ? EntityState.Modified
                    : EntityState.Added;
            }

            foreach (SessionScreenshot screenshot in entity.Screenshots)
            {
                DbContext.Entry(screenshot).State =
                    existingScreenshotIds.Contains(screenshot.Id)
                        ? EntityState.Modified
                        : EntityState.Added;
            }

            if (entity.WeatherSnapshot is not null)
            {
                DbContext.Entry(entity.WeatherSnapshot).State =
                    existingWeatherId == entity.WeatherSnapshot.Id
                        ? EntityState.Modified
                        : EntityState.Added;
            }

            if (entity.AstronomySnapshot is not null)
            {
                DbContext.Entry(entity.AstronomySnapshot).State =
                    existingAstronomyId == entity.AstronomySnapshot.Id
                        ? EntityState.Modified
                        : EntityState.Added;
            }

            await DbContext.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            DbContext.ChangeTracker.Clear();
        }
    }

    public Task<ShootingSession?> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        return CompleteQuery()
            .Where(
                session =>
                    session.Status == SessionStatus.Active ||
                    session.Status == SessionStatus.Paused)
            .OrderByDescending(session => session.StartTime)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ShootingSession>> SearchAsync(
        SessionSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        if (criteria.Skip < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(criteria),
                "Search skip cannot be negative.");
        }

        if (criteria.Take is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(criteria),
                "Search take must be between 1 and 500.");
        }

        IQueryable<ShootingSession> query = CompleteQuery();

        if (!string.IsNullOrWhiteSpace(criteria.SearchText))
        {
            string pattern = CreateLikePattern(criteria.SearchText);
            query = query.Where(
                session =>
                    EF.Functions.Like(session.TargetName, pattern, "\\") ||
                    EF.Functions.Like(session.LocationName, pattern, "\\") ||
                    (session.Problems != null &&
                     EF.Functions.Like(session.Problems, pattern, "\\")) ||
                    session.Notes.Any(
                        note => EF.Functions.Like(note.Content, pattern, "\\")));
        }

        if (criteria.From is { } from)
        {
            query = query.Where(
                session => (session.StartTime ?? session.CreatedAt) >= from);
        }

        if (criteria.To is { } to)
        {
            query = query.Where(
                session => (session.StartTime ?? session.CreatedAt) <= to);
        }

        if (!string.IsNullOrWhiteSpace(criteria.TargetName))
        {
            string targetName = criteria.TargetName.Trim();
            query = query.Where(session => session.TargetName == targetName);
        }

        if (!string.IsNullOrWhiteSpace(criteria.LocationName))
        {
            string locationName = criteria.LocationName.Trim();
            query = query.Where(session => session.LocationName == locationName);
        }

        if (criteria.SessionType is { } sessionType)
        {
            query = query.Where(session => session.SessionType == sessionType);
        }

        if (criteria.MinimumRating is { } minimumRating)
        {
            if (minimumRating is < 1 or > 5)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(criteria),
                    "Minimum rating must be between 1 and 5.");
            }

            query = query.Where(session => session.Rating >= minimumRating);
        }

        return await query
            .OrderByDescending(session => session.StartTime ?? session.CreatedAt)
            .ThenByDescending(session => session.CreatedAt)
            .ThenByDescending(session => session.Id)
            .Skip(criteria.Skip)
            .Take(criteria.Take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private IQueryable<ShootingSession> CompleteQuery()
    {
        return Entities
            .AsNoTrackingWithIdentityResolution()
            .Include(session => session.EquipmentProfile)
            .Include(session => session.WeatherSnapshot)
            .Include(session => session.AstronomySnapshot)
            .Include(session => session.Screenshots)
            .Include(session => session.Notes)
            .AsSplitQuery();
    }

    private static string CreateLikePattern(string searchText)
    {
        string escaped = searchText
            .Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

        return $"%{escaped}%";
    }
}
