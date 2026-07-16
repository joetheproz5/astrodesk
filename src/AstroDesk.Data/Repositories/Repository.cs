using AstroDesk.Core.Entities;
using AstroDesk.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AstroDesk.Data.Repositories;

public class Repository<TEntity>(AstroDeskDbContext dbContext)
    : IRepository<TEntity>
    where TEntity : EntityBase
{
    protected AstroDeskDbContext DbContext { get; } =
        dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    protected DbSet<TEntity> Entities => DbContext.Set<TEntity>();

    public virtual Task<TEntity?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ValidateId(id);

        return Entities
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);
    }

    public virtual async Task<IReadOnlyList<TEntity>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        return await Entities
            .AsNoTracking()
            .OrderByDescending(entity => entity.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public virtual async Task AddAsync(
        TEntity entity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        try
        {
            await Entities.AddAsync(entity, cancellationToken).ConfigureAwait(false);
            await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            DbContext.ChangeTracker.Clear();
        }
    }

    public virtual async Task UpdateAsync(
        TEntity entity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ValidateId(entity.Id);
        try
        {
            DetachDifferentLocalInstance(entity);
            Entities.Update(entity);
            await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            DbContext.ChangeTracker.Clear();
        }
    }

    public virtual async Task DeleteAsync(
        TEntity entity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ValidateId(entity.Id);
        try
        {
            DetachDifferentLocalInstance(entity);
            Entities.Remove(entity);
            await DbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            DbContext.ChangeTracker.Clear();
        }
    }

    protected static void ValidateId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Entity ID is required.", nameof(id));
        }
    }

    protected void DetachDifferentLocalInstance(TEntity entity)
    {
        TEntity? local = Entities.Local.FirstOrDefault(
            tracked => tracked.Id == entity.Id);

        if (local is not null && !ReferenceEquals(local, entity))
        {
            DbContext.Entry(local).State = EntityState.Detached;
        }
    }
}
