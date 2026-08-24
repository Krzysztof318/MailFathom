// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Mail.Delivery.Scheduling;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Scheduling;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Scheduling;

/// <summary>Covers what one occasion of a recurring send produces, and above all what stops a second one starting.</summary>
/// <remarks>
/// The occasion arithmetic itself belongs to the schedule syntax and is proved there. What is asserted here is what the
/// occasion does to this mailbox: an ordinary outgoing record with an identity of its own, one at a time, and nothing
/// at all once the declaration has been stopped.
/// </remarks>
public sealed class RecurringSendOccurrenceHandlerTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    /// <summary>Ten in the morning on a Wednesday, which is after that day's nine o'clock occasion and before the next.</summary>
    private static readonly DateTimeOffset Dispatched = new(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Occurrence = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    private static readonly ReadOnlyMemory<byte> DraftMime =
        Encoding.ASCII.GetBytes("Message-ID: <draft@example.test>\r\n\r\nWeekly.").AsMemory();

    /// <summary>The job this handler answers is the one a declaration's recurring dispatch enqueues.</summary>
    [Fact]
    public void JobType_IsTheOneARecurringDeclarationDispatchesUnder()
    {
        // Arrange
        var world = new OccurrenceWorld();

        // Act, Assert
        Assert.Equal(JobType.SendRecurringOccurrence, world.Handler.JobType);
    }

    /// <summary>
    /// An occasion that has come produces one ordinary message, written for the occasion itself rather than for the
    /// moment the dispatch noticed it, and the declaration records what it has now produced.
    /// </summary>
    [Fact]
    public async Task RunAsync_AnOccasionThatHasCome_ProducesOneMessageDueAtTheOccasion()
    {
        // Arrange
        var world = new OccurrenceWorld();
        var declaration = world.Declare("Daily at 09:00");

        // Act
        await world.RunAsync(declaration.Id);

        // Assert
        var request = Assert.Single(world.Outgoing.OpenRequests);
        Assert.Equal(OutgoingEmailOrigin.Schedule, request.Requester.Origin);
        Assert.Equal(Occurrence, request.DueAt?.Instant);
        var recorded = world.RecurringSends.Read(declaration.Id);
        Assert.Equal(Occurrence, recorded.LastOccurrenceAt);
        Assert.NotNull(recorded.LastOccurrenceEmailId);
    }

    /// <summary>
    /// One occurrence is in flight at a time: while the message the last occasion produced is still queued, this
    /// occasion is answered rather than started, so a week of unreachable provider does not queue a week of copies.
    /// </summary>
    [Theory]
    [InlineData(OutgoingEmailStage.Recorded)]
    [InlineData(OutgoingEmailStage.TransmissionBegun)]
    public async Task RunAsync_ThePreviousOccurrenceStillInFlight_AnswersTheOccasionRatherThanStartingIt(
        OutgoingEmailStage stage)
    {
        // Arrange
        var world = new OccurrenceWorld();
        var declaration = world.Declare("Daily at 09:00");
        await world.RunAsync(declaration.Id);
        var previous = world.RecurringSends.Read(declaration.Id).LastOccurrenceEmailId!.Value;
        world.Outgoing.Arrange(previous, stage);
        world.Advance(TimeSpan.FromDays(1));

        // Act
        await world.RunAsync(declaration.Id);

        // Assert
        Assert.Single(world.Outgoing.OpenRequests);
        Assert.Equal(Occurrence, world.RecurringSends.Read(declaration.Id).LastOccurrenceAt);
    }

    /// <summary>Once the previous occurrence has ended, the next occasion produces its own message.</summary>
    [Fact]
    public async Task RunAsync_ThePreviousOccurrenceHavingEnded_ProducesTheNextOccasionsOwnMessage()
    {
        // Arrange
        var world = new OccurrenceWorld();
        var declaration = world.Declare("Daily at 09:00");
        await world.RunAsync(declaration.Id);
        var previous = world.RecurringSends.Read(declaration.Id).LastOccurrenceEmailId!.Value;
        world.Outgoing.Arrange(previous, OutgoingEmailStage.Sent);
        world.Advance(TimeSpan.FromDays(1));

        // Act
        await world.RunAsync(declaration.Id);

        // Assert
        Assert.Equal(2, world.Outgoing.OpenRequests.Count);
        Assert.Equal(Occurrence.AddDays(1), world.RecurringSends.Read(declaration.Id).LastOccurrenceAt);
    }

    /// <summary>Two dispatches reaching one occasion compose one identity, so the outbox answers the second with the first's record.</summary>
    [Fact]
    public async Task RunAsync_TwoDispatchesReachingOneOccasion_ProduceOneMessage()
    {
        // Arrange
        var world = new OccurrenceWorld();
        var declaration = world.Declare("Daily at 09:00");
        await world.RunAsync(declaration.Id);
        var first = world.RecurringSends.Read(declaration.Id).LastOccurrenceEmailId;

        // The record the first run left is removed from the equation, so what answers the second run is the
        // idempotency identity rather than the one-occurrence-at-a-time check in front of it.
        world.Outgoing.Arrange(first!.Value, OutgoingEmailStage.Sent);

        // Act
        await world.RunAsync(declaration.Id);

        // Assert
        Assert.Equal(first, world.RecurringSends.Read(declaration.Id).LastOccurrenceEmailId);
    }

    /// <summary>A declaration stopped between the dispatch and this attempt produces nothing, which is what stopping one means.</summary>
    [Fact]
    public async Task RunAsync_ADeclarationStoppedBeforeTheAttempt_ProducesNothing()
    {
        // Arrange
        var world = new OccurrenceWorld();
        var declaration = world.Declare("Daily at 09:00");
        world.Stop(declaration.Id);

        // Act
        await world.RunAsync(declaration.Id);

        // Assert
        Assert.Empty(world.Outgoing.OpenRequests);
    }

    /// <summary>A declaration nothing holds produces nothing rather than raising, because the attempt cannot repair it.</summary>
    [Fact]
    public async Task RunAsync_ADeclarationNothingHolds_ProducesNothing()
    {
        // Arrange
        var world = new OccurrenceWorld();

        // Act
        await world.RunAsync(RecurringSendId.Create(Guid.CreateVersion7()));

        // Assert
        Assert.Empty(world.Outgoing.OpenRequests);
    }

    /// <summary>
    /// A declaration whose draft is missing raises rather than passing over, because a repetition that silently
    /// produced nothing every week is the failure nobody would notice.
    /// </summary>
    [Fact]
    public async Task RunAsync_ADeclarationHoldingNoDraft_Raises()
    {
        // Arrange
        var world = new OccurrenceWorld(storeDraft: false);
        var declaration = world.Declare("Daily at 09:00");

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => world.RunAsync(declaration.Id));
    }

    /// <summary>A payload of another job's contract is a defect in what enqueued it rather than an occasion to guess at.</summary>
    [Fact]
    public async Task RunAsync_APayloadOfAnotherContract_IsRefused()
    {
        // Arrange
        var world = new OccurrenceWorld();

        // Act
        var thrown = await Assert.ThrowsAsync<ArgumentException>(
            () => world.Handler.RunAsync(
                RunScheduledMailRulesJobPayload.For(Account),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("payload", thrown.ParamName);
    }

    /// <summary>Assembles one occasion over in-memory stores, with a composer that produces no MIME.</summary>
    private sealed class OccurrenceWorld
    {
        private readonly FakeTimeProvider clock = new(Dispatched);
        private readonly IEmailContentStore contentStore = ContentStores.Substituted();

        internal OccurrenceWorld(bool storeDraft = true)
        {
            this.Outgoing = new InMemoryOutgoingEmailStore(timeProvider: this.clock);
            this.RecurringSends = new InMemoryRecurringSendStore(this.clock);

            this.contentStore
                .FindRecurringSendDraftAsync(Arg.Any<RecurringSendId>(), Arg.Any<CancellationToken>())
                .Returns(storeDraft
                    ? new StoredEmailContent(DraftMime, DraftMime.Length, SHA256.HashData(DraftMime.Span))
                    : null);

            var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
            sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            {
                var session = Substitute.For<IPersistenceSession>();
                session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);

                return session;
            });

            var retryPolicy = new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                TimeProvider.System);

            var outbox = new MailOutbox(
                this.Outgoing,
                this.contentStore,
                retryPolicy,
                new MailOutboxSignal(capacity: 8),
                Substitute.For<IJobStore>(),
                Substitute.For<IOutboxOperationStore>(),
                AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process),
                OutgoingMailGovernors.Permitting(),
                OutgoingMailScreenings.Inactive(),
                this.clock);

            this.Handler = new RecurringSendOccurrenceHandler(
                this.RecurringSends,
                this.Outgoing,
                this.contentStore,
                ComposerThatRecomposes(),
                outbox,
                retryPolicy,
                this.clock);
        }

        internal InMemoryOutgoingEmailStore Outgoing { get; }

        internal InMemoryRecurringSendStore RecurringSends { get; }

        internal RecurringSendOccurrenceHandler Handler { get; }

        internal RecurringSend Declare(string schedule)
        {
            Assert.True(EmailAddress.TryCreate(displayName: null, "anna@example.test", out var address));

            return this.RecurringSends.Publish(new RecurringSend
            {
                Id = RecurringSendId.Create(Guid.CreateVersion7()),
                AccountId = Account,
                Requester = OutgoingEmailRequester.Command("declare-1"),
                Recipients = [OutgoingRecipient.Create(address, OutgoingRecipientRole.To)],
                Schedule = schedule,
                DraftByteLength = DraftMime.Length,
                DeclaredAt = Dispatched.AddDays(-7),
            });
        }

        internal void Stop(RecurringSendId recurringSendId) => this.RecurringSends.Publish(
            this.RecurringSends.Read(recurringSendId) with { CancelledAt = Dispatched });

        internal void Advance(TimeSpan elapsed) => this.clock.Advance(elapsed);

        internal Task RunAsync(RecurringSendId recurringSendId) => this.Handler.RunAsync(
            RecurringSendJobPayload.For(Account, recurringSendId),
            TestContext.Current.CancellationToken);

        /// <summary>Recomposes the occasion without producing MIME, so a test here says nothing about MIME.</summary>
        private static IAuthoredEmailComposer ComposerThatRecomposes()
        {
            var composer = Substitute.For<IAuthoredEmailComposer>();
            composer
                .RecomposeAsOccurrence(
                    Arg.Any<MailAccountId>(),
                    Arg.Any<OutgoingEmailRequester>(),
                    Arg.Any<IReadOnlyList<OutgoingRecipient>>(),
                    Arg.Any<ReadOnlyMemory<byte>>(),
                    Arg.Any<MailDeliveryCapabilities>())
                .Returns(call => AuthoredEmailComposition.Composed(new ComposedOutgoingEmail(
                    OutgoingEmailRequest.Create(
                        call.ArgAt<MailAccountId>(0),
                        call.ArgAt<OutgoingEmailRequester>(1),
                        call.ArgAt<IReadOnlyList<OutgoingRecipient>>(2)),
                    InternetMessageId.Mint("example.test"),
                    DraftMime)));

            return composer;
        }
    }
}
