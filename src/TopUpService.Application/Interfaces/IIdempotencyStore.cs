namespace TopUpService.Application.Interfaces;

/// <summary>
/// Lightweight store for tracking whether a given idempotency key
/// has already been processed. Prevents duplicate MCI charges when
/// the same message is redelivered by RabbitMQ.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>Returns true if the key has been marked as processed.</summary>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    /// <summary>Marks the key as processed atomically.</summary>
    Task MarkAsync(string key, CancellationToken ct = default);
}
