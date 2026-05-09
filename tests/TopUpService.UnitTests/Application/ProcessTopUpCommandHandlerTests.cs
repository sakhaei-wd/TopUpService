using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TopUpService.Application.Commands;
using TopUpService.Application.Events;
using TopUpService.Application.Interfaces;
using TopUpService.Application.Services;
using TopUpService.Domain.Entities;
using TopUpService.Domain.Enums;
using Xunit;

namespace TopUpService.UnitTests.Application;

public sealed class ProcessTopUpCommandHandlerTests
{
    private readonly Mock<ITopUpRepository> _repoMock = new();
    private readonly Mock<IMciTopUpClient> _mciMock = new();
    private readonly Mock<ITopUpResultPublisher> _publisherMock = new();
    private readonly Mock<IIdempotencyStore> _idempotencyMock = new();

    private ProcessTopUpCommandHandler BuildHandler() => new(
        _repoMock.Object,
        _mciMock.Object,
        _publisherMock.Object,
        _idempotencyMock.Object,
        NullLogger<ProcessTopUpCommandHandler>.Instance);

    private static ProcessTopUpCommand BuildCommand(string key = "KEY-001") =>
        new(key, "SWITCH-001", "09120000000", 50_000);

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_WhenMciSucceeds_ShouldPublishSuccessResult()
    {
        // Arrange
        _idempotencyMock.Setup(x => x.ExistsAsync("KEY-001", default)).ReturnsAsync(false);
        _mciMock.Setup(x => x.ChargeAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), default))
                .ReturnsAsync(new MciTopUpResult(true, "MCI-REF-ABCD", null, null));

        TopUpResultEvent? published = null;
        _publisherMock.Setup(x => x.PublishAsync(It.IsAny<TopUpResultEvent>(), default))
                      .Callback<TopUpResultEvent, CancellationToken>((e, _) => published = e)
                      .Returns(Task.CompletedTask);

        // Act
        await BuildHandler().HandleAsync(BuildCommand());

        // Assert
        published.Should().NotBeNull();
        published!.IsSuccess.Should().BeTrue();
        published.RequiresReversal.Should().BeFalse();
        published.MciReferenceNumber.Should().Be("MCI-REF-ABCD");
    }

    // ── Idempotency ───────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_WhenKeyAlreadyProcessed_ShouldReturnWithoutCallingMci()
    {
        // Arrange
        _idempotencyMock.Setup(x => x.ExistsAsync("KEY-001", default)).ReturnsAsync(true);

        // Act
        await BuildHandler().HandleAsync(BuildCommand());

        // Assert — MCI should never be called for a duplicate
        _mciMock.Verify(
            x => x.ChargeAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), default),
            Times.Never);
        _publisherMock.Verify(x => x.PublishAsync(It.IsAny<TopUpResultEvent>(), default), Times.Never);
    }

    // ── Business failure ──────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_WhenMciReturnBusinessError_ShouldPublishFailureWithoutReversal()
    {
        // Arrange
        _idempotencyMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), default)).ReturnsAsync(false);
        _mciMock.Setup(x => x.ChargeAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), default))
                .ReturnsAsync(new MciTopUpResult(false, null, "ERR_INVALID_MSISDN", "Phone not found"));

        TopUpResultEvent? published = null;
        _publisherMock.Setup(x => x.PublishAsync(It.IsAny<TopUpResultEvent>(), default))
                      .Callback<TopUpResultEvent, CancellationToken>((e, _) => published = e)
                      .Returns(Task.CompletedTask);

        // Act
        await BuildHandler().HandleAsync(BuildCommand("KEY-002"));

        // Assert
        published!.IsSuccess.Should().BeFalse();
        published.RequiresReversal.Should().BeFalse(); // MCI never charged → no reversal needed
    }

    // ── Transient failure / reversal ──────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_WhenAllRetriesExhausted_ShouldPublishReversalRequired()
    {
        // Arrange
        _idempotencyMock.Setup(x => x.ExistsAsync(It.IsAny<string>(), default)).ReturnsAsync(false);

        // Always throw a transient exception
        _mciMock.Setup(x => x.ChargeAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), default))
                .ThrowsAsync(new HttpRequestException("Connection refused"));

        // Prevent actual Task.Delay waits in tests
        TopUpResultEvent? published = null;
        _publisherMock.Setup(x => x.PublishAsync(It.IsAny<TopUpResultEvent>(), default))
                      .Callback<TopUpResultEvent, CancellationToken>((e, _) => published = e)
                      .Returns(Task.CompletedTask);

        // Act
        await BuildHandler().HandleAsync(BuildCommand("KEY-003"));

        // Assert
        published!.IsSuccess.Should().BeFalse();
        published.RequiresReversal.Should().BeTrue();
    }
}
