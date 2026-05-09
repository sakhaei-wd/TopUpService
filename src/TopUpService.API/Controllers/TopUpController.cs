using Microsoft.AspNetCore.Mvc;
using TopUpService.Application.Interfaces;
using TopUpService.API.Models;

namespace TopUpService.API.Controllers;

/// <summary>
/// Exposes read-only endpoints for operational visibility into TopUp transactions.
/// The primary processing path is asynchronous (RabbitMQ), not HTTP.
/// </summary>
[ApiController]
[Route("api/topup")]
[Produces("application/json")]
public sealed class TopUpController : ControllerBase
{
    private readonly ITopUpRepository _repository;
    private readonly ILogger<TopUpController> _logger;

    public TopUpController(ITopUpRepository repository, ILogger<TopUpController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves the current status of a TopUp transaction by its internal ID.
    /// Useful for Switch.Net operators to inspect processing state.
    /// </summary>
    /// <param name="id">Transaction GUID.</param>
    [HttpGet("{id:guid}", Name = "GetTopUpById")]
    [ProducesResponseType(typeof(TopUpTransactionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var transaction = await _repository.GetByIdAsync(id, ct);

        if (transaction is null)
        {
            _logger.LogWarning("Transaction not found. Id={Id}", id);
            return NotFound(new { message = $"Transaction {id} not found." });
        }

        return Ok(TopUpTransactionResponse.From(transaction));
    }

    /// <summary>
    /// Retrieves the status of a TopUp transaction by its idempotency key.
    /// Allows Switch.Net to poll for a result when the result queue message was lost.
    /// </summary>
    /// <param name="idempotencyKey">The key originally sent with the request message.</param>
    [HttpGet("by-key/{idempotencyKey}", Name = "GetTopUpByKey")]
    [ProducesResponseType(typeof(TopUpTransactionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByIdempotencyKey(string idempotencyKey, CancellationToken ct)
    {
        var transaction = await _repository.GetByIdempotencyKeyAsync(idempotencyKey, ct);

        if (transaction is null)
            return NotFound(new { message = $"Transaction with key '{idempotencyKey}' not found." });

        return Ok(TopUpTransactionResponse.From(transaction));
    }

    /// <summary>
    /// Health probe endpoint used by load balancers and Kubernetes liveness checks.
    /// </summary>
    [HttpGet("/health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Health() => Ok(new { status = "healthy", utc = DateTimeOffset.UtcNow });
}
