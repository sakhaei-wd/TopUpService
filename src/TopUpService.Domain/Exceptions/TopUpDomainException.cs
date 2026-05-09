namespace TopUpService.Domain.Exceptions;

/// <summary>
/// Represents a business rule violation inside the TopUp domain.
/// Infrastructure and Application layers should NOT catch this silently —
/// it indicates a programming error or an invalid state machine transition.
/// </summary>
public sealed class TopUpDomainException : Exception
{
    public TopUpDomainException(string message) : base(message) { }

    public TopUpDomainException(string message, Exception inner) : base(message, inner) { }
}
