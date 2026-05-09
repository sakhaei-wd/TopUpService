using Microsoft.Extensions.Logging;
using TopUpService.Application.Commands;
using TopUpService.Application.Events;
using TopUpService.Application.Interfaces;
using TopUpService.Domain.Entities;
using TopUpService.Domain.Enums;

namespace TopUpService.Application.Services;

/// <summary>
/// Orchestrates the full lifecycle of a TopUp transaction.
///
/// Flow:
///   1. Check idempotency — skip if already processed.
///   2. Persist a Pending transaction.
///   3. Call MCI API with up to <see cref="MaxRetries"/> attempts.
///   4. Transition domain entity to Succeeded | Failed | ReversalRequired.
///   5. Publish result back to Switch.Net via the result queue.
/// </summary>
public sealed class ProcessTopUpCommandHandler
{
    private const int MaxRetries = 3;

    private readonly ITopUpRepository _repository;
    private readonly IMciTopUpClient _mciClient;
    private readonly ITopUpResultPublisher _resultPublisher;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly ILogger<ProcessTopUpCommandHandler> _logger;

    public ProcessTopUpCommandHandler(
        ITopUpRepository repository,
        IMciTopUpClient mciClient,
        ITopUpResultPublisher resultPublisher,
        IIdempotencyStore idempotencyStore,
        ILogger<ProcessTopUpCommandHandler> logger)
    {
        _repository = repository;
        _mciClient = mciClient;
        _resultPublisher = resultPublisher;
        _idempotencyStore = idempotencyStore;
        _logger = logger;
    }

    /// <summary>
    /// Entry point called by the RabbitMQ consumer for every incoming message.
    /// </summary>
    public async Task HandleAsync(ProcessTopUpCommand command, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Received TopUp command. IdempotencyKey={Key} SwitchRef={Ref} Phone={Phone} Amount={Amount}",
            command.IdempotencyKey,
            command.SwitchReferenceNumber,
            command.PhoneNumber,
            command.AmountRials);

        // ── 1. Idempotency guard ──────────────────────────────────────────────
        if (await _idempotencyStore.ExistsAsync(command.IdempotencyKey, ct))
        {
            _logger.LogWarning(
                "Duplicate message detected. IdempotencyKey={Key} — skipping.",
                command.IdempotencyKey);
            return;
        }

        // ── 2. Persist initial state ──────────────────────────────────────────
        var transaction = TopUpTransaction.Create(
            command.IdempotencyKey,
            command.SwitchReferenceNumber,
            command.PhoneNumber,
            command.AmountRials);

        await _repository.AddAsync(transaction, ct);
        await _repository.SaveChangesAsync(ct);

        // ── 3. Call MCI with retry ────────────────────────────────────────────
        transaction.MarkProcessing();

        string? lastErrorMessage = null;
        bool mciCallSucceeded = false;
        string? mciRef = null;

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            _logger.LogInformation(
                "Calling MCI API. TransactionId={Id} Attempt={Attempt}/{Max}",
                transaction.Id, attempt, MaxRetries);

            try
            {
                var result = await _mciClient.ChargeAsync(
                    command.PhoneNumber,
                    command.AmountRials,
                    // Use IdempotencyKey as the per-request correlation ID toward MCI.
                    requestId: command.IdempotencyKey,
                    ct);

                if (result.IsSuccess)
                {
                    mciCallSucceeded = true;
                    mciRef = result.ReferenceNumber;
                    _logger.LogInformation(
                        "MCI charge succeeded. TransactionId={Id} MciRef={MciRef}",
                        transaction.Id, mciRef);
                    break;
                }

                // Business failure (e.g. invalid number, insufficient balance on operator side)
                // Do NOT retry on explicit business errors.
                lastErrorMessage = $"MCI error [{result.ErrorCode}]: {result.ErrorMessage}";
                _logger.LogWarning(
                    "MCI returned a business failure. TransactionId={Id} Code={Code} Message={Msg} — not retrying.",
                    transaction.Id, result.ErrorCode, result.ErrorMessage);
                break;
            }
            catch (OperationCanceledException)
            {
                throw; // Honour cancellation immediately.
            }
            catch (Exception ex)
            {
                // Transient infrastructure error — eligible for retry.
                lastErrorMessage = ex.Message;
                _logger.LogError(ex,
                    "Transient error calling MCI. TransactionId={Id} Attempt={Attempt}/{Max}",
                    transaction.Id, attempt, MaxRetries);

                if (attempt < MaxRetries)
                {
                    transaction.RecordRetryAttempt();
                    await _repository.UpdateAsync(transaction, ct);
                    await _repository.SaveChangesAsync(ct);

                    // Exponential back-off: 1s, 2s, 4s …
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                    _logger.LogInformation(
                        "Waiting {Delay}s before retry. TransactionId={Id}", delay.TotalSeconds, transaction.Id);
                    await Task.Delay(delay, ct);
                }
            }
        }

        // ── 4. Finalise domain state ──────────────────────────────────────────
        TopUpResultEvent resultEvent;

        if (mciCallSucceeded)
        {
            transaction.MarkSucceeded(mciRef!);
            resultEvent = new TopUpResultEvent(
                IdempotencyKey: command.IdempotencyKey,
                SwitchReferenceNumber: command.SwitchReferenceNumber,
                IsSuccess: true,
                MciReferenceNumber: mciRef,
                RequiresReversal: false,
                FailureReason: null,
                ProcessedAt: DateTimeOffset.UtcNow);
        }
        else if (transaction.AttemptCount >= MaxRetries && lastErrorMessage is not null
                 && IsTransientFailure(lastErrorMessage))
        {
            // All retries exhausted on a transient error → Switch must reverse.
            transaction.MarkReversalRequired(lastErrorMessage);
            resultEvent = new TopUpResultEvent(
                IdempotencyKey: command.IdempotencyKey,
                SwitchReferenceNumber: command.SwitchReferenceNumber,
                IsSuccess: false,
                MciReferenceNumber: null,
                RequiresReversal: true,
                FailureReason: lastErrorMessage,
                ProcessedAt: DateTimeOffset.UtcNow);
        }
        else
        {
            // Business failure — no reversal needed because MCI never charged.
            transaction.MarkFailed(lastErrorMessage ?? "Unknown MCI failure");
            resultEvent = new TopUpResultEvent(
                IdempotencyKey: command.IdempotencyKey,
                SwitchReferenceNumber: command.SwitchReferenceNumber,
                IsSuccess: false,
                MciReferenceNumber: null,
                RequiresReversal: false,
                FailureReason: lastErrorMessage,
                ProcessedAt: DateTimeOffset.UtcNow);
        }

        await _repository.UpdateAsync(transaction, ct);
        await _repository.SaveChangesAsync(ct);

        // ── 5. Mark idempotency key as done BEFORE publishing ─────────────────
        // This ordering ensures we won't publish twice even if the process restarts
        // after DB write but before queue write — the worker will re-publish on
        // restart since idempotency check is the first guard.
        await _idempotencyStore.MarkAsync(command.IdempotencyKey, ct);

        // ── 6. Publish result to Switch ───────────────────────────────────────
        await _resultPublisher.PublishAsync(resultEvent, ct);

        _logger.LogInformation(
            "TopUp processing complete. TransactionId={Id} Status={Status} RequiresReversal={Rev}",
            transaction.Id,
            transaction.Status,
            resultEvent.RequiresReversal);
    }

    /// <summary>
    /// Heuristic: an error is "transient" if it was thrown as an exception
    /// (infrastructure level) rather than being a well-formed MCI business error.
    /// Business errors never populate <see cref="Exception"/>.
    /// </summary>
    private static bool IsTransientFailure(string message) =>
        !message.StartsWith("MCI error [", StringComparison.Ordinal);
}
