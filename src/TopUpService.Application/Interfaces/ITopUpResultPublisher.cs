using TopUpService.Application.Events;

namespace TopUpService.Application.Interfaces;

/// <summary>
/// Publishes the final outcome of a TopUp transaction back to
/// the result queue so Switch.Net can finalize or reverse the
/// original purchase transaction.
/// </summary>
public interface ITopUpResultPublisher
{
    Task PublishAsync(TopUpResultEvent result, CancellationToken ct = default);
}
