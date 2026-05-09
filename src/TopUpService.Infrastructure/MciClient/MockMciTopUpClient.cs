using Microsoft.Extensions.Logging;
using TopUpService.Application.Interfaces;

namespace TopUpService.Infrastructure.MciClient;

/// <summary>
/// Simulates MCI's TopUp API for development and demo purposes.
/// Returns success ~80% of the time; simulates timeouts and business errors otherwise.
///
/// Replace with the real HTTP client implementation before going to production.
/// </summary>
public sealed class MockMciTopUpClient : IMciTopUpClient
{
    private readonly ILogger<MockMciTopUpClient> _logger;
    private static readonly Random _random = new();

    public MockMciTopUpClient(ILogger<MockMciTopUpClient> logger)
    {
        _logger = logger;
    }

    public async Task<MciTopUpResult> ChargeAsync(
        string phoneNumber,
        long amountRials,
        string requestId,
        CancellationToken ct = default)
    {
        // Simulate network latency (50–300 ms)
        await Task.Delay(_random.Next(50, 300), ct);

        var roll = _random.NextDouble();

        // 80% success
        if (roll < 0.80)
        {
            var mciRef = $"MCI-{Guid.NewGuid():N}"[..16];
            _logger.LogDebug("MockMCI: SUCCESS for requestId={Id} phone={Phone}", requestId, phoneNumber);
            return new MciTopUpResult(true, mciRef, null, null);
        }

        // 10% transient error (network timeout) — will be retried
        if (roll < 0.90)
        {
            _logger.LogDebug("MockMCI: TIMEOUT for requestId={Id}", requestId);
            throw new HttpRequestException("Simulated MCI connection timeout.");
        }

        // 10% business error (e.g. invalid phone number) — not retried
        _logger.LogDebug("MockMCI: BUSINESS_ERROR for requestId={Id}", requestId);
        return new MciTopUpResult(false, null, "ERR_INVALID_MSISDN", "Phone number not found in MCI system.");
    }
}
