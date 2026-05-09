namespace TopUpService.Application.Events;

/// <summary>
/// Published to the <c>topup.result</c> RabbitMQ queue after processing.
/// Switch.Net consumes this to finalize or reverse the original purchase.
/// </summary>
public sealed record TopUpResultEvent(
    string IdempotencyKey,
    string SwitchReferenceNumber,
    bool IsSuccess,

    /// <summary>Set when <see cref="IsSuccess"/> is true.</summary>
    string? MciReferenceNumber,

    /// <summary>
    /// When true, Switch must issue a Reversal to Shaparak
    /// because MCI could not be reached after all retries.
    /// </summary>
    bool RequiresReversal,

    /// <summary>Human-readable failure description for Switch diagnostics.</summary>
    string? FailureReason,

    DateTimeOffset ProcessedAt
);
