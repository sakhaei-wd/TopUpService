using TopUpService.Domain.Entities;

namespace TopUpService.Application.Interfaces;

/// <summary>
/// Persistence contract for <see cref="TopUpTransaction"/>.
/// Implemented in the Infrastructure layer; never referenced by Domain.
/// </summary>
public interface ITopUpRepository
{
    /// <summary>Persists a new transaction.</summary>
    Task AddAsync(TopUpTransaction transaction, CancellationToken ct = default);

    /// <summary>Retrieves a transaction by its primary key.</summary>
    Task<TopUpTransaction?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Retrieves a transaction by its idempotency key.</summary>
    Task<TopUpTransaction?> GetByIdempotencyKeyAsync(string key, CancellationToken ct = default);

    /// <summary>Persists state changes on an existing transaction.</summary>
    Task UpdateAsync(TopUpTransaction transaction, CancellationToken ct = default);

    /// <summary>Commits any pending unit-of-work changes.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}
