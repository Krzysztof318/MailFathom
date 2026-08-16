// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery;

public sealed class MailOutboxTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly ReadOnlyMemory<byte> RawMime =
        Encoding.ASCII.GetBytes("Message-ID: <one@example.test>\r\n\r\nHello.").AsMemory();

    /// <summary>The record and the message it points at are one write, so a caller cannot leave half a send behind.</summary>
    [Fact]
    public async Task EnqueueAsync_NewRequest_RecordsTheSendAndItsMessageInOneSession()
    {
        // Arrange
        var contentStore = Substitute.For<IEmailContentStore>();
        var stagedSessions = new List<IPersistenceSession>();
        var outbox = CreateOutbox(new InMemoryOutgoingMessageStore(), contentStore, stagedSessions);
        var request = CreateRequest("mfctl-4f2a");

        // Act
        var record = await outbox.EnqueueAsync(request, RawMime, CancellationToken.None);

        // Assert
        Assert.Equal(OutgoingMessageStage.Recorded, record.Stage);
        Assert.Equal(RawMime.Length, record.MimeByteLength);
        Assert.Equal(request.Recipients, record.OutstandingRecipients);
        await contentStore.Received(1).SaveOutgoingContentAsync(
            stagedSessions.Single(),
            record.Id,
            RawMime,
            Arg.Any<CancellationToken>());
    }

    /// <summary>The same authored request arriving twice is one record, which is what makes it one delivery.</summary>
    [Fact]
    public async Task EnqueueAsync_SameIdentityTwice_AnswersWithOneRecord()
    {
        // Arrange
        var contentStore = Substitute.For<IEmailContentStore>();
        var store = new InMemoryOutgoingMessageStore();
        var outbox = CreateOutbox(store, contentStore);
        var first = await outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), RawMime, CancellationToken.None);

        // Act
        var retried = await outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), RawMime, CancellationToken.None);

        // Assert
        Assert.Equal(first.Id, retried.Id);
        Assert.Equal(2, store.OpenRequests.Count);
        var outstanding = await store.ReadOutstandingAsync(Account, limit: 10, CancellationToken.None);
        Assert.Single(outstanding);
    }

    /// <summary>A second send that was genuinely authored carries a key of its own and is a second record.</summary>
    [Fact]
    public async Task EnqueueAsync_SecondAuthoredRequest_IsASecondRecord()
    {
        // Arrange
        var store = new InMemoryOutgoingMessageStore();
        var outbox = CreateOutbox(store, Substitute.For<IEmailContentStore>());
        var first = await outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), RawMime, CancellationToken.None);

        // Act
        var second = await outbox.EnqueueAsync(CreateRequest("mfctl-91bd"), RawMime, CancellationToken.None);

        // Assert
        Assert.NotEqual(first.Id, second.Id);
        var outstanding = await store.ReadOutstandingAsync(Account, limit: 10, CancellationToken.None);
        Assert.Equal(2, outstanding.Count);
    }

    /// <summary>A send with nothing to transmit is refused before anything is durable.</summary>
    [Fact]
    public async Task EnqueueAsync_NoMime_IsRefusedBeforeAnythingIsWritten()
    {
        // Arrange
        var store = new InMemoryOutgoingMessageStore();
        var outbox = CreateOutbox(store, Substitute.For<IEmailContentStore>());

        // Act
        var thrown = await Assert.ThrowsAsync<ArgumentException>(
            () => outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), ReadOnlyMemory<byte>.Empty, CancellationToken.None));

        // Assert
        Assert.Equal("rawMime", thrown.ParamName);
        Assert.Empty(store.OpenRequests);
    }

    /// <summary>
    /// The losing side of a race for one identity retries in a fresh session and finds the winner's record, which is
    /// how two callers asking together deliver once.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_LosesTheRaceForOneIdentity_RetriesAndFindsTheWinnersRecord()
    {
        // Arrange
        var store = new InMemoryOutgoingMessageStore();
        var contentStore = Substitute.For<IEmailContentStore>();
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        var losing = Substitute.For<IPersistenceSession>();
        var winning = Substitute.For<IPersistenceSession>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(losing, winning);
        losing.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.ConcurrencyConflict);
        winning.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
        var outbox = new MailOutbox(store, contentStore, CreateRetryPolicy(sessionFactory));

        // Act
        var record = await outbox.EnqueueAsync(CreateRequest("mfctl-4f2a"), RawMime, CancellationToken.None);

        // Assert
        Assert.Equal(2, store.OpenRequests.Count);
        var outstanding = await store.ReadOutstandingAsync(Account, limit: 10, CancellationToken.None);
        Assert.Equal(record.Id, Assert.Single(outstanding).Id);
    }

    private static OutgoingMessageRequest CreateRequest(string invocationIdentity)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "anna@example.test", out var address));

        return OutgoingMessageRequest.Create(
            Account,
            OutgoingMessageRequester.Command(invocationIdentity),
            [OutgoingRecipient.Create(address, OutgoingRecipientRole.To)]);
    }

    private static MailOutbox CreateOutbox(
        IOutgoingMessageStore store,
        IEmailContentStore contentStore,
        List<IPersistenceSession>? stagedSessions = null)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var session = Substitute.For<IPersistenceSession>();
            session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
            stagedSessions?.Add(session);

            return session;
        });

        return new MailOutbox(store, contentStore, CreateRetryPolicy(sessionFactory));
    }

    /// <summary>Builds the policy the outbox commits through, over the real clock the policy's own tests use.</summary>
    /// <remarks>
    /// A controlled clock would deadlock rather than help: the backoff between attempts is a
    /// <c>Task.Delay</c> against this provider, and one that never advances never completes it. The delay the one
    /// conflicting test pays is a few tens of milliseconds.
    /// </remarks>
    private static OptimisticConcurrencyRetryPolicy CreateRetryPolicy(IPersistenceSessionFactory sessionFactory) =>
        new(sessionFactory, new PersistenceConcurrencyOptions(), TimeProvider.System);
}
