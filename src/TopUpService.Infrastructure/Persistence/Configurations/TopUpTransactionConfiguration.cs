using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TopUpService.Domain.Entities;

namespace TopUpService.Infrastructure.Persistence.Configurations;

/// <summary>
/// Fluent configuration for <see cref="TopUpTransaction"/>.
/// Keeps mapping concerns out of the domain entity.
/// </summary>
internal sealed class TopUpTransactionConfiguration : IEntityTypeConfiguration<TopUpTransaction>
{
    public void Configure(EntityTypeBuilder<TopUpTransaction> builder)
    {
        builder.ToTable("TopUpTransactions");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.IdempotencyKey)
               .IsRequired()
               .HasMaxLength(128);

        builder.HasIndex(t => t.IdempotencyKey)
               .IsUnique();

        builder.Property(t => t.SwitchReferenceNumber)
               .IsRequired()
               .HasMaxLength(64);

        builder.Property(t => t.PhoneNumber)
               .IsRequired()
               .HasMaxLength(15);

        builder.Property(t => t.AmountRials)
               .IsRequired();

        builder.Property(t => t.Status)
               .IsRequired()
               .HasConversion<int>();

        builder.Property(t => t.AttemptCount)
               .IsRequired();

        builder.Property(t => t.MciReferenceNumber)
               .HasMaxLength(64);

        builder.Property(t => t.FailureReason)
               .HasMaxLength(512);

        builder.Property(t => t.CreatedAt)
               .IsRequired();

        builder.Property(t => t.ProcessedAt);
    }
}
