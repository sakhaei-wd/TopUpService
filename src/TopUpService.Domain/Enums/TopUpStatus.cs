namespace TopUpService.Domain.Enums;

/// <summary>
/// Represents the lifecycle status of a TopUp transaction.
/// </summary>
public enum TopUpStatus
{
    /// <summary>Request received from Switch, awaiting processing.</summary>
    Pending = 0,

    /// <summary>Request sent to MCI API, awaiting response.</summary>
    Processing = 1,

    /// <summary>MCI confirmed the top-up was applied successfully.</summary>
    Succeeded = 2,

    /// <summary>MCI returned a failure response; no charge applied.</summary>
    Failed = 3,

    /// <summary>
    /// MCI was unreachable after all retry attempts; a reversal is required
    /// to be published back to Switch.
    /// </summary>
    ReversalRequired = 4
}
