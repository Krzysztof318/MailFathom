// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>
/// Proves the outgoing record is what stops one authored request delivering twice, and that a send stopped mid
/// transmission is still there to be found afterwards.
/// </summary>
/// <remarks>
/// Neither claim is reachable from a unit test. The first is a unique index refusing an insert two transactions each
/// reached without seeing the other, which only a real database decides; the second is a row read back through a new
/// scope after the one that wrote it is gone, which is the closest a test gets to the restart it describes.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedOutgoingEmailStoreTests(MailFathomOrchestrationFixture orchestration)
{
    private static readonly MailAccountId Account = SyntheticMailAccount.AccountId;

    [Fact]
    public async Task OpenAsync_ForTheSameIdentityInTwoConcurrentSessions_IsRefusedByTheDatabaseAndLeavesOneRecord()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var request = CreateRequest("outbox-identity-race", "anna@example.test");

        // Act
        var commits = await services.InTwoScopesAsync(
            async (firstScope, secondScope, token) =>
            {
                await using var first = await firstScope.GetRequiredService<IPersistenceSessionFactory>()
                    .BeginSessionAsync(token);
                await using var second = await secondScope.GetRequiredService<IPersistenceSessionFactory>()
                    .BeginSessionAsync(token);

                // Both open before either commits, which is the whole point: neither transaction can see the other's
                // pending row, so both reach the insert and only the index can refuse one.
                await firstScope.GetRequiredService<IOutgoingEmailStore>()
                    .OpenAsync(first, request, MimeOf("race").Length, token);
                await secondScope.GetRequiredService<IOutgoingEmailStore>()
                    .OpenAsync(second, request, MimeOf("race").Length, token);

                return (First: await first.CommitAsync(token), Second: await second.CommitAsync(token));
            },
            cancellationToken);

        // Assert
        Assert.Equal(PersistenceCommitResult.Committed, commits.First);
        Assert.Equal(PersistenceCommitResult.ConcurrencyConflict, commits.Second);
        Assert.Single(await ReadOutstandingAsync(services, request, cancellationToken));
    }

    /// <summary>
    /// The same authored request arriving twice through the outbox, which is the shape a retried command or a rule that
    /// ran again actually takes: the loser of the race retries, finds the winner's record, and delivers nothing further.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_SameIdentityTwice_LeavesOneRecordAndTheMessageThatWasFirstStored()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var request = CreateRequest("outbox-duplicate-enqueue", "bruno@example.test");
        var firstMime = MimeOf("duplicate-enqueue-first");
        var recomposedMime = MimeOf("duplicate-enqueue-second");

        var first = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailOutbox>().EnqueueAsync(request, firstMime, token),
            cancellationToken);

        // Act
        var retried = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailOutbox>().EnqueueAsync(request, recomposedMime, token),
            cancellationToken);

        // Assert
        Assert.Equal(first.Id, retried.Id);
        Assert.Single(await ReadOutstandingAsync(services, request, cancellationToken));

        // The message is the one the first enqueue stored. A recomposed message carries a different Message-ID, so
        // letting the second write win would thread one send as two in every recipient's client.
        var storedContent = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IEmailContentStore>()
                .FindOutgoingContentAsync(first.Id, token),
            cancellationToken);
        Assert.NotNull(storedContent);
        Assert.True(
            firstMime.Span.SequenceEqual(storedContent.RawMime.Span),
            "The stored outgoing message is not the one the first enqueue wrote.");
        Assert.Null(storedContent.FindIntegrityDefect());
        Assert.Equal(firstMime.Length, retried.MimeByteLength);
    }

    /// <summary>
    /// A send stopped where a crash would leave it: the transmission was announced and nothing answered. What has to
    /// hold is that a later process finds the record, reads it as undecidable, and does not treat it as finished.
    /// </summary>
    [Fact]
    public async Task ReadOutstandingAsync_ForARecordLeftMidTransmission_FindsItAndReportsAnUnknownOutcome()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var request = CreateRequest("outbox-unknown-outcome", "clara@example.test");
        var enqueued = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailOutbox>()
                .EnqueueAsync(request, MimeOf("unknown-outcome"), token),
            cancellationToken);

        // Act
        var announced = await services.CommitAsync(
            async (scope, session, token) =>
            {
                var store = scope.GetRequiredService<IOutgoingEmailStore>();
                await store.CountAttemptAsync(session, enqueued.Id, token);
                await store.RecordTransmissionBegunAsync(session, enqueued.Id, token);
            },
            cancellationToken);

        // Assert
        Assert.Equal(PersistenceCommitResult.Committed, announced);

        var found = Assert.Single(await ReadOutstandingAsync(services, request, cancellationToken));
        Assert.Equal(OutgoingEmailStage.TransmissionBegun, found.Stage);
        Assert.True(found.HasUnknownOutcome);
        Assert.False(found.IsTerminal);
        Assert.Equal(1, found.AttemptCount);

        // A message that may already have been transmitted can never be recorded as withdrawn.
        await Assert.ThrowsAsync<InvalidOperationException>(() => services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IOutgoingEmailStore>().AdvanceAsync(
                session,
                enqueued.Id,
                OutgoingEmailStage.Cancelled,
                replyCode: null,
                token),
            cancellationToken));

        // Once the send has stopped, nothing goes on writing about it: a late reply would settle a recipient on a
        // record nothing will offer again, and a later failure would overwrite the one an operator reads as the reason.
        await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IOutgoingEmailStore>().AdvanceAsync(
                session,
                enqueued.Id,
                OutgoingEmailStage.Refused,
                replyCode: 554,
                token),
            cancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IOutgoingEmailStore>().RecordRecipientOutcomesAsync(
                session,
                enqueued.Id,
                [OutgoingRecipientOutcome.Answered(
                    request.Recipients[0],
                    OutgoingRecipientStatus.Accepted,
                    replyCode: 250,
                    DateTimeOffset.UnixEpoch)],
                token),
            cancellationToken));

        await Assert.ThrowsAsync<InvalidOperationException>(() => services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IOutgoingEmailStore>().RecordFailureAsync(
                session,
                enqueued.Id,
                MailFathomErrorCode.MailDeliveryUnavailable,
                token),
            cancellationToken));
    }

    /// <summary>
    /// A partial acceptance and the attempt that follows it: the recipient the message reached is never offered again,
    /// the one permanently refused is never offered again either, and the one a server deferred is what is left.
    /// </summary>
    [Fact]
    public async Task RecordRecipientOutcomesAsync_AfterAPartialAcceptance_LeavesOnlyTheUnsettledRecipientOutstanding()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var request = CreateRequest(
            "outbox-partial-acceptance",
            "anna@example.test",
            "bruno@example.invalid",
            "clara@example.test");
        var enqueued = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailOutbox>()
                .EnqueueAsync(request, MimeOf("partial-acceptance"), token),
            cancellationToken);
        var answeredAt = DateTimeOffset.UnixEpoch.AddMinutes(1);

        // Act
        var answered = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IOutgoingEmailStore>()
                .RecordRecipientOutcomesAsync(
                    session,
                    enqueued.Id,
                    [
                        Answered(request, 0, OutgoingRecipientStatus.Accepted, 250, answeredAt),
                        Answered(request, 1, OutgoingRecipientStatus.Refused, 550, answeredAt),
                        Answered(request, 2, OutgoingRecipientStatus.Pending, 451, answeredAt),
                    ],
                    token),
            cancellationToken);

        // Assert
        Assert.Equal(PersistenceCommitResult.Committed, answered);

        var reread = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutgoingEmailStore>().FindAsync(enqueued.Id, token),
            cancellationToken);
        Assert.NotNull(reread);
        Assert.Equal([request.Recipients[2]], reread.OutstandingRecipients);
        Assert.Equal([250, 550, 451], reread.Recipients.Select(outcome => outcome.LastReplyCode));

        // A later attempt answering about a recipient already settled changes nothing, so a transient reply cannot
        // undo a delivery that already happened.
        var reanswered = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IOutgoingEmailStore>()
                .RecordRecipientOutcomesAsync(
                    session,
                    enqueued.Id,
                    [Answered(request, 0, OutgoingRecipientStatus.Pending, 451, answeredAt.AddMinutes(1))],
                    token),
            cancellationToken);
        Assert.Equal(PersistenceCommitResult.Committed, reanswered);

        var afterTheLateAnswer = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutgoingEmailStore>().FindAsync(enqueued.Id, token),
            cancellationToken);
        Assert.NotNull(afterTheLateAnswer);
        Assert.Equal([request.Recipients[2]], afterTheLateAnswer.OutstandingRecipients);
    }

    /// <summary>Erasing the record erases the message it points at, which is the obligation the cascade carries.</summary>
    [Fact]
    public async Task DeletingTheRecord_ErasesTheStoredMessageAndTheRecipientsWithIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var request = CreateRequest("outbox-erasure", "anna@example.test");
        var enqueued = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailOutbox>().EnqueueAsync(request, MimeOf("erasure"), token),
            cancellationToken);

        // Act
        await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .OutgoingEmails
                .Where(message => message.Id == enqueued.Id.Value)
                .ExecuteDeleteAsync(token),
            cancellationToken);

        // Assert
        var storedContent = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IEmailContentStore>()
                .FindOutgoingContentAsync(enqueued.Id, token),
            cancellationToken);
        Assert.Null(storedContent);

        var remainingRecipientCount = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .OutgoingEmailRecipients
                .AsNoTracking()
                .CountAsync(recipient => recipient.OutgoingEmailId == enqueued.Id.Value, token),
            cancellationToken);
        Assert.Equal(0, remainingRecipientCount);
    }

    private static OutgoingRecipientOutcome Answered(
        OutgoingEmailRequest request,
        int recipientIndex,
        OutgoingRecipientStatus status,
        int replyCode,
        DateTimeOffset answeredAt) => OutgoingRecipientOutcome.Answered(
            request.Recipients[recipientIndex],
            status,
            replyCode,
            answeredAt);

    /// <summary>Reads the account's outbox and narrows it to the one request the calling test authored.</summary>
    /// <remarks>
    /// Narrowed by the requester rather than asserted as the whole answer, because every class in this collection shares
    /// one database and one account: another test's queued send is somebody else's row, not a defect in this one.
    /// </remarks>
    private static async Task<IReadOnlyList<OutgoingEmailRecord>> ReadOutstandingAsync(
        OrchestratedMailFathomServices services,
        OutgoingEmailRequest request,
        CancellationToken cancellationToken)
    {
        var outstanding = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutgoingEmailStore>()
                .ReadOutstandingAsync(Account, limit: 100, token),
            cancellationToken);

        return [.. outstanding.Where(record => record.Requester == request.Requester)];
    }

    private static OutgoingEmailRequest CreateRequest(string invocationIdentity, params string[] addresses) =>
        OutgoingEmailRequest.Create(
            Account,
            OutgoingEmailRequester.Command(invocationIdentity),
            [.. addresses.Select(address => RecipientOf(address))]);

    private static OutgoingRecipient RecipientOf(string address)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, address, out var emailAddress));

        return OutgoingRecipient.Create(emailAddress, OutgoingRecipientRole.To);
    }

    /// <summary>Builds a synthetic outgoing message whose bytes differ per scenario.</summary>
    private static ReadOnlyMemory<byte> MimeOf(string discriminator) => Encoding.ASCII.GetBytes(
        $"Message-ID: <{discriminator}@example.test>\r\nSubject: {discriminator}\r\n\r\nSynthetic body.\r\n")
        .AsMemory();
}
