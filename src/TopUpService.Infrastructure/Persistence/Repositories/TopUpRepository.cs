using Microsoft.EntityFrameworkCore;
using TopUpService.Application.Interfaces;
using TopUpService.Domain.Entities;

namespace TopUpService.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ITopUpRepository"/>.
/// </summary>
public sealed class TopUpRepository : ITopUpRepository
{
    private readonly TopUpDbContext _context;

    public TopUpRepository(TopUpDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(TopUpTransaction transaction, CancellationToken ct = default)
        => await _context.TopUpTransactions.AddAsync(transaction, ct);

    public Task<TopUpTransaction?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _context.TopUpTransactions.FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<TopUpTransaction?> GetByIdempotencyKeyAsync(string key, CancellationToken ct = default)
        => _context.TopUpTransactions.FirstOrDefaultAsync(t => t.IdempotencyKey == key, ct);

    public Task UpdateAsync(TopUpTransaction transaction, CancellationToken ct = default)
    {
        _context.TopUpTransactions.Update(transaction);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => _context.SaveChangesAsync(ct);
}
