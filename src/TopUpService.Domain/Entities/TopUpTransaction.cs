using TopUpService.Domain.Enums;
using TopUpService.Domain.Exceptions;

namespace TopUpService.Domain.Entities;

/// <summary>
/// Aggregate root for a TopUp transaction.
/// Encapsulates all state transitions and business rules.
/// </summary>
public sealed class TopUpTransaction
{
    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>Internal database identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Idempotency key provided by Switch.Net to prevent duplicate processing.
    /// Unique per Switch request.
    /// </summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>Reference number assigned by Switch.Net for tracing.</summary>
    public string SwitchReferenceNumber { get; private set; }

    // ── Business data ─────────────────────────────────────────────────────────

    /// <summary>MSISDN (mobile number) to be recharged.</summary>
    public string PhoneNumber { get; private set; }

    /// <summary>Charge amount in Iranian Rials (IRR).</summary>
    public long AmountRials { get; private set; }

    // ── Status ────────────────────────────────────────────────────────────────

    public TopUpStatus Status { get; private set; }

    /// <summary>Number of MCI call attempts made so far.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>Reference number returned by MCI on success.</summary>
    public string? MciReferenceNumber { get; private set; }

    /// <summary>Human-readable failure reason (for logging / diagnostics).</summary>
    public string? FailureReason { get; private set; }

    // ── Timestamps ────────────────────────────────────────────────────────────

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    // ── Constructor (EF Core needs a parameterless one internally) ────────────
    private TopUpTransaction() 
    { 
        IdempotencyKey = string.Empty;
        SwitchReferenceNumber = string.Empty;
        PhoneNumber = string.Empty;
    }

    /// <summary>
    /// Factory method — creates a new pending TopUp transaction.
    /// This is the only valid way to start a transaction from outside the domain.
    /// </summary>
    public static TopUpTransaction Create(
        string idempotencyKey,
        string switchReferenceNumber,
        string phoneNumber,
        long amountRials)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new TopUpDomainException("Idempotency key cannot be empty.");

        if (string.IsNullOrWhiteSpace(switchReferenceNumber))
            throw new TopUpDomainException("Switch reference number cannot be empty.");

        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new TopUpDomainException("Phone number cannot be empty.");

        if (amountRials <= 0)
            throw new TopUpDomainException($"Amount must be positive. Got: {amountRials}");

        return new TopUpTransaction
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = idempotencyKey,
            SwitchReferenceNumber = switchReferenceNumber,
            PhoneNumber = phoneNumber,
            AmountRials = amountRials,
            Status = TopUpStatus.Pending,
            AttemptCount = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    // ── State transitions ─────────────────────────────────────────────────────

    /// <summary>Marks the transaction as in-flight toward MCI.</summary>
    public void MarkProcessing()
    {
        EnsureStatus(TopUpStatus.Pending, nameof(MarkProcessing));
        Status = TopUpStatus.Processing;
        AttemptCount++;
    }

    /// <summary>
    /// Records a retry attempt. Called when a transient MCI error occurs
    /// and we are going to retry (AttemptCount &lt; MaxRetries).
    /// </summary>
    public void RecordRetryAttempt()
    {
        if (Status != TopUpStatus.Processing)
            throw new TopUpDomainException($"Cannot record retry on a transaction with status {Status}.");

        AttemptCount++;
        // Keep status as Processing so the worker can attempt again.
    }

    /// <summary>Transitions the transaction to Succeeded.</summary>
    public void MarkSucceeded(string mciReferenceNumber)
    {
        EnsureStatus(TopUpStatus.Processing, nameof(MarkSucceeded));

        if (string.IsNullOrWhiteSpace(mciReferenceNumber))
            throw new TopUpDomainException("MCI reference number cannot be empty on success.");

        Status = TopUpStatus.Succeeded;
        MciReferenceNumber = mciReferenceNumber;
        ProcessedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Transitions the transaction to Failed (business failure, not transient).</summary>
    public void MarkFailed(string reason)
    {
        EnsureStatus(TopUpStatus.Processing, nameof(MarkFailed));
        Status = TopUpStatus.Failed;
        FailureReason = reason;
        ProcessedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Transitions to ReversalRequired after all retry attempts are exhausted.
    /// Switch.Net will receive a reversal notification through the result queue.
    /// </summary>
    public void MarkReversalRequired(string reason)
    {
        EnsureStatus(TopUpStatus.Processing, nameof(MarkReversalRequired));
        Status = TopUpStatus.ReversalRequired;
        FailureReason = reason;
        ProcessedAt = DateTimeOffset.UtcNow;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void EnsureStatus(TopUpStatus expected, string operation)
    {
        if (Status != expected)
            throw new TopUpDomainException(
                $"Cannot perform '{operation}' when status is '{Status}'. Expected '{expected}'.");
    }
}
