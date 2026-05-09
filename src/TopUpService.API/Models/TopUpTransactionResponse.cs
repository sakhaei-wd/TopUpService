using TopUpService.Domain.Entities;
using TopUpService.Domain.Enums;

namespace TopUpService.API.Models;

/// <summary>
/// API response DTO. Decouples the HTTP contract from the domain entity.
/// </summary>
public sealed class TopUpTransactionResponse
{
    public Guid Id { get; init; }
    public string IdempotencyKey { get; init; } = default!;
    public string SwitchReferenceNumber { get; init; } = default!;
    public string PhoneNumber { get; init; } = default!;
    public long AmountRials { get; init; }
    public string Status { get; init; } = default!;
    public int AttemptCount { get; init; }
    public string? MciReferenceNumber { get; init; }
    public string? FailureReason { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; init; }

    public static TopUpTransactionResponse From(TopUpTransaction t) => new()
    {
        Id = t.Id,
        IdempotencyKey = t.IdempotencyKey,
        SwitchReferenceNumber = t.SwitchReferenceNumber,
        PhoneNumber = t.PhoneNumber,
        AmountRials = t.AmountRials,
        Status = t.Status.ToString(),
        AttemptCount = t.AttemptCount,
        MciReferenceNumber = t.MciReferenceNumber,
        FailureReason = t.FailureReason,
        CreatedAt = t.CreatedAt,
        ProcessedAt = t.ProcessedAt
    };
}
