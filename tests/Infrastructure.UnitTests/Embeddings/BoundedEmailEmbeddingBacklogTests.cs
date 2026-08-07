// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Generation;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Embeddings;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Embeddings;

public sealed class BoundedEmailEmbeddingBacklogTests
{
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
