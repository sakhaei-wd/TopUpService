using FluentAssertions;
using TopUpService.Domain.Entities;
using TopUpService.Domain.Enums;
using TopUpService.Domain.Exceptions;
using Xunit;

namespace TopUpService.UnitTests.Domain;

public sealed class TopUpTransactionTests
{
    // ── Factory method ────────────────────────────────────────────────────────

    [Fact]
    public void Create_WithValidData_ShouldReturnPendingTransaction()
    {
        // Arrange & Act
        var tx = TopUpTransaction.Create("KEY-001", "SWITCH-REF-001", "09120000000", 50_000);

        // Assert
        tx.Id.Should().NotBeEmpty();
        tx.Status.Should().Be(TopUpStatus.Pending);
        tx.AttemptCount.Should().Be(0);
        tx.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData("", "ref", "09120000000", 1000)]
    [InlineData("key", "", "09120000000", 1000)]
    [InlineData("key", "ref", "", 1000)]
    [InlineData("key", "ref", "09120000000", 0)]
    [InlineData("key", "ref", "09120000000", -100)]
    public void Create_WithInvalidData_ShouldThrowDomainException(
        string key, string switchRef, string phone, long amount)
    {
        var act = () => TopUpTransaction.Create(key, switchRef, phone, amount);
        act.Should().Throw<TopUpDomainException>();
    }

    // ── State transitions ─────────────────────────────────────────────────────

    [Fact]
    public void MarkProcessing_FromPending_ShouldIncrementAttemptCountAndChangeStatus()
    {
        var tx = BuildPending();

        tx.MarkProcessing();

        tx.Status.Should().Be(TopUpStatus.Processing);
        tx.AttemptCount.Should().Be(1);
    }

    [Fact]
    public void MarkProcessing_WhenNotPending_ShouldThrow()
    {
        var tx = BuildProcessing();

        var act = () => tx.MarkProcessing();

        act.Should().Throw<TopUpDomainException>()
           .WithMessage("*MarkProcessing*");
    }

    [Fact]
    public void MarkSucceeded_FromProcessing_ShouldSetSuccessState()
    {
        var tx = BuildProcessing();

        tx.MarkSucceeded("MCI-REF-123");

        tx.Status.Should().Be(TopUpStatus.Succeeded);
        tx.MciReferenceNumber.Should().Be("MCI-REF-123");
        tx.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkSucceeded_WithEmptyRef_ShouldThrow()
    {
        var tx = BuildProcessing();

        var act = () => tx.MarkSucceeded(string.Empty);

        act.Should().Throw<TopUpDomainException>();
    }

    [Fact]
    public void MarkFailed_FromProcessing_ShouldSetFailedState()
    {
        var tx = BuildProcessing();

        tx.MarkFailed("ERR_INVALID_MSISDN");

        tx.Status.Should().Be(TopUpStatus.Failed);
        tx.FailureReason.Should().Be("ERR_INVALID_MSISDN");
        tx.ProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkReversalRequired_FromProcessing_ShouldSetReversalState()
    {
        var tx = BuildProcessing();

        tx.MarkReversalRequired("All retries exhausted");

        tx.Status.Should().Be(TopUpStatus.ReversalRequired);
        tx.FailureReason.Should().Be("All retries exhausted");
    }

    [Fact]
    public void RecordRetryAttempt_FromProcessing_ShouldIncrementAttemptCount()
    {
        var tx = BuildProcessing();
        var initialCount = tx.AttemptCount;

        tx.RecordRetryAttempt();

        tx.AttemptCount.Should().Be(initialCount + 1);
        tx.Status.Should().Be(TopUpStatus.Processing); // Status unchanged
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TopUpTransaction BuildPending() =>
        TopUpTransaction.Create("KEY-001", "SWITCH-001", "09120000000", 50_000);

    private static TopUpTransaction BuildProcessing()
    {
        var tx = BuildPending();
        tx.MarkProcessing();
        return tx;
    }
}
