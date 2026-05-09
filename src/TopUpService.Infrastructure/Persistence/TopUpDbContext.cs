using Microsoft.EntityFrameworkCore;
using TopUpService.Domain.Entities;

namespace TopUpService.Infrastructure.Persistence;

/// <summary>
/// Entity Framework Core context backed by SQLite for demo purposes.
/// In production this would target SQL Server or PostgreSQL.
/// </summary>
public sealed class TopUpDbContext : DbContext
{
    public TopUpDbContext(DbContextOptions<TopUpDbContext> options) : base(options) { }

    public DbSet<TopUpTransaction> TopUpTransactions => Set<TopUpTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TopUpDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
