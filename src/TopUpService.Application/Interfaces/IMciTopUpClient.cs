namespace TopUpService.Application.Interfaces;

/// <summary>
/// Contract for communicating with MCI's TopUp (Instant Charge) API.
/// The mock / real implementation lives in Infrastructure.
/// </summary>
public interface IMciTopUpClient
{
    /// <summary>
    /// Sends a TopUp request to MCI.
    /// </summary>
    /// <param name="phoneNumber">Target MSISDN.</param>
    /// <param name="amountRials">Charge amount in IRR.</param>
    /// <param name="requestId">Unique request correlation ID for idempotency on MCI side.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="MciTopUpResult"/> — always returns (never throws on business failure).
    /// Throws only on unrecoverable infrastructure errors (network timeout, etc.).
    /// </returns>
    Task<MciTopUpResult> ChargeAsync(
        string phoneNumber,
        long amountRials,
        string requestId,
        CancellationToken ct = default);
}

/// <summary>Result of a single MCI API call attempt.</summary>
public sealed record MciTopUpResult(
    bool IsSuccess,
    string? ReferenceNumber,
    string? ErrorCode,
    string? ErrorMessage
);
