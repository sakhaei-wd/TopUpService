using TopUpService.Application.Interfaces;

namespace TopUpService.Infrastructure.Idempotency;

/// <summary>
/// Thread-safe, in-process idempotency store backed by a <see cref="HashSet{T}"/>.
/// Suitable for single-instance demo deployments.
///
/// Production note: replace with a distributed store (Redis, DB table, etc.)
/// when running multiple service instances so idempotency is cluster-wide.
/// </summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly HashSet<string> _processedKeys = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return _processedKeys.Contains(key);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task MarkAsync(string key, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _processedKeys.Add(key);
        }
        finally
        {
            _lock.Release();
        }
    }
}
