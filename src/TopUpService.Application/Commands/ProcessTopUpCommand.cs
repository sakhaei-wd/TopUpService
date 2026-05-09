namespace TopUpService.Application.Commands;

/// <summary>
/// Deserialized form of the message that Switch.Net publishes
/// to the <c>topup.request</c> RabbitMQ queue.
/// Property names are PascalCase to match the JSON contract agreed with Switch.
/// </summary>
public sealed record ProcessTopUpCommand(
    /// <summary>Globally unique key; used for idempotency.</summary>
    string IdempotencyKey,

    /// <summary>Switch internal reference number (for tracing).</summary>
    string SwitchReferenceNumber,

    /// <summary>MSISDN of the subscriber to be charged.</summary>
    string PhoneNumber,

    /// <summary>Amount in Iranian Rials.</summary>
    long AmountRials
);
