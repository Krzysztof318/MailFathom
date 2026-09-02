// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Vectorization;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Embeddings;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Embeddings;

public sealed class BoundedEmailEmbeddingBacklogTests
{
    private const string RefusedInstrument = "mailfathom.embedding.backlog.refused";

    private const string DepthInstrument = "mailfathom.embedding.backlog.depth";

    /// <summary>Guards against a read that never completes. No assertion depends on how long the read takes.</summary>
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

    [Fact]
    public void TryEnqueue_BeyondTheConfiguredCapacity_RefusesInsteadOfWaiting()
    {
        // Arrange
        var backlog = new BoundedEmailEmbeddingBacklog(new EmailEmbeddingBacklogOptions { Capacity = 2 });
        var messages = CreateMessages(3);

        // Act
        var accepted = messages.Select(backlog.TryEnqueue).ToArray();

        // Assert
        Assert.Equal([true, true, false], accepted);
        Assert.Equal(2, backlog.Depth);
    }

    [Fact]
    public async Task ReadAllAsync_MessagesOffered_ServesThemInTheOrderTheyWereOfferedAndLowersTheDepth()
    {
        // Arrange
        var backlog = new BoundedEmailEmbeddingBacklog(new EmailEmbeddingBacklogOptions { Capacity = 4 });
        var messages = CreateMessages(3);
        foreach (var message in messages)
        {
            backlog.TryEnqueue(message);
        }

        // Act
        var read = await ReadAsync(backlog, messages.Count);

        // Assert
        Assert.Equal(messages, read);
        Assert.Equal(0, backlog.Depth);
    }

    [Fact]
    public async Task ReadAllAsync_CancelledWhileTheBacklogIsEmpty_EndsInsteadOfWaitingForever()
    {
        // Arrange
        var backlog = new BoundedEmailEmbeddingBacklog(new EmailEmbeddingBacklogOptions { Capacity = 4 });
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var storedEmailId in backlog.ReadAllAsync(cancellation.Token))
            {
                Assert.Fail($"An empty backlog served {storedEmailId}.");
            }
        });

        // Assert
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
    }

    /// <summary>
    /// The refusal counter is the only place an operator learns that mail the live path stored was never offered for
    /// embedding, and it is what tells them how much the backfill will have to reach. A refusal that incremented
    /// nothing would leave that invisible while `TryEnqueue` still answered correctly, so the count is asserted where
    /// it leaves the process rather than inferred from the return value.
    /// </summary>
    [Fact]
    public void TryEnqueue_RefusedByTheBound_CountsEachRefusalOnceAndCountsNothingForAnAcceptedOffer()
    {
        // Arrange
        var backlog = new BoundedEmailEmbeddingBacklog(new EmailEmbeddingBacklogOptions { Capacity = 1 });
        using var measurements = new RecordedMailFathomMeasurements(RefusedInstrument);
        var messages = CreateMessages(3);

        // Act
        foreach (var message in messages)
        {
            backlog.TryEnqueue(message);
        }

        // Assert
        Assert.Equal([1, 1], measurements.ValuesOf(RefusedInstrument));
    }

    /// <summary>
    /// The depth gauge is the signal an instance falling behind shows up in first, and a gauge reports only what its
    /// callback returns when something asks. A callback wired to a constant, or to something other than what is
    /// waiting, would leave every dashboard flat, so it is observed rather than trusted to mirror the property beside
    /// it.
    /// </summary>
    /// <remarks>
    /// Asserted as the change between two observations rather than as a value. Every backlog an earlier test in this
    /// class built published a gauge of the same name that is still alive on the process-wide meter, and each answers
    /// for its own backlog, so one observation records several numbers; only this backlog's moves between the two.
    /// </remarks>
    [Fact]
    public async Task Depth_ObservedThroughTheGauge_FollowsWhatIsWaiting()
    {
        // Arrange
        var backlog = new BoundedEmailEmbeddingBacklog(new EmailEmbeddingBacklogOptions { Capacity = 4 });
        using var measurements = new RecordedMailFathomMeasurements(DepthInstrument);
        var whileNothingWaited = ObserveTotalDepth(measurements);

        foreach (var message in CreateMessages(3))
        {
            backlog.TryEnqueue(message);
        }

        // Act
        var whileThreeWaited = ObserveTotalDepth(measurements);
        await ReadAsync(backlog, 2);
        var afterTwoWereTaken = ObserveTotalDepth(measurements);

        // Assert
        Assert.Equal(3, whileThreeWaited - whileNothingWaited);
        Assert.Equal(-2, afterTwoWereTaken - whileThreeWaited);
    }

    [Fact]
    public void Constructor_CapacityIsNotPositive_RefusesTheBacklog()
    {
        // Arrange
        var options = new EmailEmbeddingBacklogOptions { Capacity = 0 };

        // Act
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedEmailEmbeddingBacklog(options));

        // Assert
        Assert.Equal("options", refusal.ParamName);
    }

    /// <summary>Asks every depth gauge for its value and returns what this observation alone summed to.</summary>
    /// <remarks>
    /// The recorded list is cumulative, so one observation's total is the difference between the running sums before
    /// and after it. Summing across the gauges is what makes the other backlogs' constant answers cancel out of the
    /// comparison between two observations.
    /// </remarks>
    private static double ObserveTotalDepth(RecordedMailFathomMeasurements measurements)
    {
        var before = measurements.ValuesOf(DepthInstrument).Sum();
        measurements.ObserveGauges();

        return measurements.ValuesOf(DepthInstrument).Sum() - before;
    }

    private static IReadOnlyList<StoredEmailId> CreateMessages(int count) =>
        [.. Enumerable.Range(0, count).Select(_ => StoredEmailId.Create(Guid.CreateVersion7()))];

    private static async Task<IReadOnlyList<StoredEmailId>> ReadAsync(BoundedEmailEmbeddingBacklog backlog, int count)
    {
        using var deadlockGuard = new CancellationTokenSource(DeadlockGuard);

        List<StoredEmailId> read = [];

        await foreach (var storedEmailId in backlog.ReadAllAsync(deadlockGuard.Token))
        {
            read.Add(storedEmailId);

            if (read.Count == count)
            {
                break;
            }
        }

        return read;
    }
}
