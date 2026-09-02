// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
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
    /// <summary>Gets the account this suite writes under, whose owner the orchestrated database provisioned.</summary>
    /// <remarks>Read on each use rather than captured in a field, because the owner is resolved when the harness starts.</remarks>
    private static MailAccountIdentity Account => SyntheticMailAccount.Account;

    /// <summary>The principal the orchestrated caller's sends are recorded under, which is the identity the harness admits it as.</summary>
    private static readonly OutgoingEmailPrincipal OrchestratedCallerPrincipal =
        OutgoingEmailPrincipal.Of("orchestrated-caller");

    /// <summary>The principal a record written directly through the store is stamped with.</summary>
    /// <remarks>Only the two direct <c>OpenAsync</c> calls below supply it; everything else here goes through the outbox, which reads the principal from whatever admitted the send.</remarks>
    private static readonly OutgoingEmailPrincipal Principal = OutgoingEmailPrincipal.Of("outbox-identity-race");

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
                    .OpenAsync(first, request, Principal, MimeOf("race").Length, token);
                await secondScope.GetRequiredService<IOutgoingEmailStore>()
                    .OpenAsync(second, request, Principal, MimeOf("race").Length, token);

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

        var first = await EnqueueAsync(services, request, firstMime, cancellationToken);

        // Act
        var retried = await EnqueueAsync(services, request, recomposedMime, cancellationToken);

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
    /// The send grant is enforced by the composed graph rather than by whatever a unit test hands the outbox. What this
    /// settles is the wiring: the principal a scope reports reaches the outbox, and a scope reporting no caller — which
    /// is every worker in this process — writes nothing into the account's outbox for a send somebody asked for.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_UnderTheProcessIdentity_IsRefusedAndLeavesNoRecord()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var request = CreateRequest("outbox-ungranted-command", "fiona@example.test");

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailOutbox>()
                .EnqueueAsync(request, MimeOf("ungranted-command"), token),
            cancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.PrincipalNotAuthorized, refusal.ErrorCode);
        Assert.Empty(await ReadOutstandingAsync(services, request, cancellationToken));
    }

    /// <summary>
    /// A recipient addressed by naming somebody keeps both facts on the record: the address the send was offered to, and
    /// the contact it was resolved from. The pair is what makes a send answerable after the book has moved on, and only a
    /// real column can establish that it round-trips.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_RecipientResolvedFromAContact_ReadsBackTheAddressAndTheContact()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var contact = ContactId.Create(Guid.CreateVersion7());
        var request = OutgoingEmailRequest.Create(
            Account,
            OutgoingEmailRequester.Command("outbox-contact-recipient"),
            [
                RecipientOf("dana@example.test", contact),
                RecipientOf("erik@example.test"),
            ]);

        // Act
        await EnqueueAsync(services, request, MimeOf("contact-recipient"), cancellationToken);

        // Assert
        var record = Assert.Single(await ReadOutstandingAsync(services, request, cancellationToken));

        Assert.Equal(
            [contact, null],
            record.Recipients.Select(outcome => outcome.Recipient.Contact));

        Assert.Equal(
            ["dana@example.test", "erik@example.test"],
            record.Recipients.Select(outcome => outcome.Recipient.Address.Address));
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
        var enqueued = await EnqueueAsync(services, request, MimeOf("unknown-outcome"), cancellationToken);

        // The claim is what counts the attempt and takes the lease, in one statement and its own transaction, exactly
        // as a delivery pass reaches this record.
        var claimed = await ClaimAsync(services, enqueued.Id, cancellationToken);

        // Act
        var announced = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IOutgoingEmailStore>()
                .RecordTransmissionBegunAsync(session, claimed.Lease, enqueued.Id, token),
            cancellationToken);

        // Assert
        Assert.Equal(PersistenceCommitResult.Committed, announced);

        var found = Assert.Single(await ReadOutstandingAsync(services, request, cancellationToken));
        Assert.Equal(OutgoingEmailStage.TransmissionBegun, found.Stage);
        Assert.True(found.HasUnknownOutcome);
        Assert.False(found.IsTerminal);
        Assert.Equal(1, found.AttemptCount);

        // Nothing claims it again, whatever its lease says: a second attempt would transmit a message that may
        // already be in somebody's mailbox, and the stage is what refuses it rather than a timer. Asserted over this
        // record rather than over the batch, because every class in this collection shares one account and another
        // test's queued send is a row this claim legitimately takes.
        Assert.DoesNotContain(
            await ClaimBatchAsync(services, cancellationToken),
            entry => entry.Record.Id == enqueued.Id);

        // A message that may already have been transmitted can never be recorded as withdrawn.
        await Assert.ThrowsAsync<InvalidOperationException>(() => services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IOutgoingEmailStore>().AdvanceAsync(
                session,
                claimed.Lease,
                enqueued.Id,
                OutgoingEmailStage.Cancelled,
                replyCode: null,
                token),
            cancellationToken));

        // Once the send has stopped, nothing goes on writing about it: a late reply would settle a recipient on a
        // record nothing will offer again, and a later failure would overwrite the one an operator reads as the reason.
        // Reaching a terminal stage gives the lease back with it, so what refuses the two writes below is the lease
        // rather than the stage — the record is held by nobody, and an attempt that holds nothing writes nothing.
        await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IOutgoingEmailStore>().AdvanceAsync(
                session,
                claimed.Lease,
                enqueued.Id,
                OutgoingEmailStage.Refused,
                replyCode: 554,
                token),
            cancellationToken);

        await Assert.ThrowsAsync<OutgoingEmailLeaseLostException>(() => services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IOutgoingEmailStore>().RecordRecipientOutcomesAsync(
                session,
                claimed.Lease,
                enqueued.Id,
                [OutgoingRecipientOutcome.Answered(
                    request.Recipients[0],
                    OutgoingRecipientStatus.Accepted,
                    replyCode: 250,
                    DateTimeOffset.UnixEpoch)],
                token),
            cancellationToken));

        await Assert.ThrowsAsync<OutgoingEmailLeaseLostException>(() => services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IOutgoingEmailStore>().RecordFailureAsync(
                session,
                claimed.Lease,
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
        var enqueued = await EnqueueAsync(services, request, MimeOf("partial-acceptance"), cancellationToken);
        var answeredAt = DateTimeOffset.UnixEpoch.AddMinutes(1);
        var claimed = await ClaimAsync(services, enqueued.Id, cancellationToken);

        // Act
        var answered = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IOutgoingEmailStore>()
                .RecordRecipientOutcomesAsync(
                    session,
                    claimed.Lease,
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
                    claimed.Lease,
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
        var enqueued = await EnqueueAsync(services, request, MimeOf("erasure"), cancellationToken);

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

    /// <summary>The principal a record is written under has to survive the round trip, because equality with it is what confines a read to one caller.</summary>
    /// <remarks>
    /// A column is the one place the value can be lost without anything failing: a row that read back as nobody would
    /// hide every send from the caller that queued it, and a row that read back as something else would hide it just as
    /// completely. Only a real read of a real column establishes either way.
    /// </remarks>
    [Fact]
    public async Task FindAsync_ForARecordACallerQueued_ReadsBackThePrincipalItWasQueuedUnder()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var request = CreateRequest("outbox-principal-round-trip", "iris@example.test");
        var enqueued = await EnqueueAsync(services, request, MimeOf("principal-round-trip"), cancellationToken);

        // Act
        var found = await FindAsync(services, enqueued.Id, cancellationToken);

        // Assert
        Assert.NotNull(found);
        Assert.Equal(OrchestratedCallerPrincipal, found.Principal);
    }

    /// <summary>Reads one record back the way the tools over a queued send do.</summary>
    private static Task<OutgoingEmailRecord?> FindAsync(
        OrchestratedMailFathomServices services,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutgoingEmailStore>().FindAsync(outgoingEmailId, token),
            cancellationToken);

    /// <summary>Claims one named record the way a delivery pass does, and fails the test when the claim missed it.</summary>
    private static async Task<ClaimedOutgoingEmail> ClaimAsync(
        OrchestratedMailFathomServices services,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var claimed = await ClaimBatchAsync(services, cancellationToken);

        return Assert.Single(claimed, entry => entry.Record.Id == outgoingEmailId);
    }

    /// <summary>Writes a send down as the agent that asked for it, which is the principal the outbox admits a command from.</summary>
    /// <remarks>
    /// The permission is stated rather than assumed away. This suite drives the production graph directly, so a scope it
    /// composes reports the process identity — correct for a rule and refused for a command — and a test about the
    /// record has to arrive as what a tool call arrives as.
    /// </remarks>
    private static async Task<OutgoingEmailRecord> EnqueueAsync(
        OrchestratedMailFathomServices services,
        OutgoingEmailRequest request,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken)
    {
        var opened = await services.AsCallerInScopeAsync(
            (scope, token) => scope.GetRequiredService<MailOutbox>().EnqueueAsync(request, rawMime, token),
            [MailFathomPermission.MailSend],
            cancellationToken);

        return opened.Record;
    }

    /// <summary>Claims whatever the account has due, which is what a pass reads and what a test asserts an absence over.</summary>
    /// <remarks>
    /// The batch is wide because every class in this collection shares one account: another test's queued send is
    /// somebody else's row, and a narrow batch would let one hide this test's record rather than report it.
    /// </remarks>
    private static Task<IReadOnlyList<ClaimedOutgoingEmail>> ClaimBatchAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutgoingEmailStore>().ClaimAsync(
                OutgoingEmailClaimRequest.Create(Account, batchSize: 100, TimeSpan.FromMinutes(10)),
                token),
            cancellationToken);

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

    private static OutgoingRecipient RecipientOf(string address, ContactId? contact = null)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, address, out var emailAddress));

        return OutgoingRecipient.Create(emailAddress, OutgoingRecipientRole.To, contact);
    }

    /// <summary>Builds a synthetic outgoing message whose bytes differ per scenario.</summary>
    private static ReadOnlyMemory<byte> MimeOf(string discriminator) => Encoding.ASCII.GetBytes(
        $"Message-ID: <{discriminator}@example.test>\r\nSubject: {discriminator}\r\n\r\nSynthetic body.\r\n")
        .AsMemory();
}
