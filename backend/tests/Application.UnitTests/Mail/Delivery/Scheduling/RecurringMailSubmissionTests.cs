// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Scheduling;
using MailFathom.Application.Mail.Delivery.Submission;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Scheduling;
using MailFathom.Domain.Failures;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Scheduling;

/// <summary>Covers what declaring a repeated message writes down, what it refuses, and what stopping one means.</summary>
/// <remarks>
/// The store behind it is an in-memory one rather than a substitute, because the claims here are about what was written
/// down: one declaration per authored act, a stopped declaration that keeps its row, and a draft stored with the
/// declaration in one session. The composer is a substitute, and only there — what it produces is MIME, which is the
/// MimeKit adapter's own suite to prove.
/// </remarks>
public sealed class RecurringMailSubmissionTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly DateTimeOffset Declared = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    private static readonly ReadOnlyMemory<byte> ComposedMime =
        Encoding.ASCII.GetBytes("Message-ID: <weekly@example.test>\r\n\r\nHello.").AsMemory();

    /// <summary>A declaration and the draft its occasions are composed from are one write, so neither can exist alone.</summary>
    [Fact]
    public async Task DeclareAsync_ARepetitionThisSystemCanRun_WritesTheDeclarationAndItsDraftTogether()
    {
        // Arrange
        var store = new InMemoryRecurringSendStore(new FakeTimeProvider(Declared));
        var stagedSessions = new List<IPersistenceSession>();
        var submission = SubmissionOver(store, out var contentStore, stagedSessions: stagedSessions);

        // Act
        var declaration = await submission.DeclareAsync(
            RequestFor("Daily at 09:00 Europe/Warsaw"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Daily at 09:00 Europe/Warsaw", declaration.Schedule);
        Assert.Equal(Declared, declaration.DeclaredAt);
        Assert.True(declaration.IsActive);
        Assert.Null(declaration.LastOccurrenceAt);
        await contentStore.Received(1).SaveRecurringSendDraftAsync(
            stagedSessions.Single(),
            declaration.Id,
            Arg.Is<PlacedEmailContent>(placed => placed!.RawMime.ToArray().SequenceEqual(ComposedMime.ToArray())),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The recipients every occasion is offered to are settled once, here, rather than resolved again each week.</summary>
    [Fact]
    public async Task DeclareAsync_ARepetitionThisSystemCanRun_KeepsThePeopleEveryOccasionIsOfferedTo()
    {
        // Arrange
        var submission = SubmissionOver(new InMemoryRecurringSendStore(), out _);

        // Act
        var declaration = await submission.DeclareAsync(
            RequestFor("Every 01:00:00"),
            TestContext.Current.CancellationToken);

        // Assert
        var recipient = Assert.Single(declaration.Recipients);
        Assert.Equal(
            "anna@example.test",
            recipient.Address.NormalizedAddress,
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(OutgoingRecipientRole.To, recipient.Role);
    }

    /// <summary>The same authored act arriving twice is one declaration, which is what keeps a retry from doubling what a mailbox sends.</summary>
    [Fact]
    public async Task DeclareAsync_TheSameAuthoredActTwice_AnswersWithOneDeclaration()
    {
        // Arrange
        var store = new InMemoryRecurringSendStore();
        var submission = SubmissionOver(store, out _);
        var first = await submission.DeclareAsync(
            RequestFor("Daily at 09:00"),
            TestContext.Current.CancellationToken);

        // Act
        var retried = await submission.DeclareAsync(
            RequestFor("Daily at 09:00"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(first.Id, retried.Id);
        Assert.Single(store.Declarations);
    }

    /// <summary>A repetition written in a form the syntax does not name is refused, and the refusal says what was wrong with it.</summary>
    [Theory]
    [InlineData("every so often")]
    [InlineData("Daily at 25:00")]
    [InlineData("Every 00:00:00")]
    [InlineData("Daily at 09:00 Mars/Olympus")]
    public async Task DeclareAsync_AScheduleTheSyntaxDoesNotName_IsRefusedBeforeAnythingIsRead(string schedule)
    {
        // Arrange
        var store = new InMemoryRecurringSendStore();
        var submission = SubmissionOver(store, out var contentStore);

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => submission.DeclareAsync(RequestFor(schedule), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailScheduleRefused, refusal.ErrorCode);
        Assert.NotEmpty(refusal.Message);
        Assert.Empty(store.Declarations);
        await contentStore.DidNotReceiveWithAnyArgs().SaveRecurringSendDraftAsync(
            default!,
            default,
            default!,
            TestContext.Current.CancellationToken);
    }

    /// <summary>A refusal names the forms a repetition may take and never the message it was declared for.</summary>
    [Fact]
    public async Task DeclareAsync_AScheduleTheSyntaxDoesNotName_SaysNothingAboutTheMessage()
    {
        // Arrange
        var submission = SubmissionOver(new InMemoryRecurringSendStore(), out _);
        var request = RequestFor("whenever") with { Subject = "Quarterly figures" };

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => submission.DeclareAsync(request, TestContext.Current.CancellationToken));

        // Assert
        Assert.DoesNotContain("anna", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Quarterly", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Declaring what a mailbox sends is writing from it, so it asks for the grant that lets a caller send.</summary>
    [Fact]
    public async Task DeclareAsync_CallerWithoutTheSendGrant_RefusesAndDeclaresNothing()
    {
        // Arrange
        var store = new InMemoryRecurringSendStore();
        var submission = SubmissionOver(
            store,
            out _,
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => submission.DeclareAsync(RequestFor("Daily at 09:00"), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailSend, refusal.RequiredPermission);
        Assert.Empty(store.Declarations);
    }

    /// <summary>A declaration for an account this deployment does not serve is refused before anything is composed.</summary>
    [Fact]
    public async Task DeclareAsync_AnAccountThisDeploymentDoesNotServe_IsRefused()
    {
        // Arrange
        var submission = SubmissionOver(new InMemoryRecurringSendStore(), out _);
        var request = RequestFor("Daily at 09:00") with
        {
            Account = MailAccountSelector.For(MailAccountId.Create("nobody")),
        };

        // Act, Assert
        await Assert.ThrowsAsync<MailAccountNotAccessibleException>(
            () => submission.DeclareAsync(request, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// An account another owner owns is refused exactly as one nobody serves, so a repetition cannot be declared
    /// against a mailbox this caller may not even read — which would otherwise send as that owner on every occasion.
    /// </summary>
    [Fact]
    public async Task DeclareAsync_AnAccountTheCallersOwnerDoesNotOwn_IsRefusedAndDeclaresNothing()
    {
        // Arrange
        var store = new InMemoryRecurringSendStore();
        var submission = SubmissionOver(
            store,
            out _,
            AccessAuthorizations.ForOwnerGranted(SyntheticMailOwner.Another, MailFathomPermission.MailSend));

        // Act
        var refusal = await Assert.ThrowsAsync<MailAccountNotAccessibleException>(
            () => submission.DeclareAsync(
                RequestFor("Daily at 09:00"),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailAccountSelector.For(Account), refusal.RequestedAccount);
        Assert.Empty(store.Declarations);
    }

    /// <summary>Stopping a declaration stops every occasion still to come, and the row it leaves says when.</summary>
    [Fact]
    public async Task CancelAsync_AnActiveDeclaration_StopsEveryOccasionStillToCome()
    {
        // Arrange
        var store = new InMemoryRecurringSendStore(new FakeTimeProvider(Declared));
        var submission = SubmissionOver(store, out _);
        var declaration = await submission.DeclareAsync(
            RequestFor("Daily at 09:00"),
            TestContext.Current.CancellationToken);

        // Act
        var cancellation = await submission.CancelAsync(declaration.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(RecurringSendCancellation.Cancelled, cancellation);
        var stopped = store.Read(declaration.Id);
        Assert.False(stopped.IsActive);
        Assert.Equal(Declared, stopped.CancelledAt);
    }

    /// <summary>Stopping the same declaration twice is answered rather than refused, so a retried request is not a failure.</summary>
    [Fact]
    public async Task CancelAsync_ADeclarationAlreadyStopped_AnswersThatItWas()
    {
        // Arrange
        var store = new InMemoryRecurringSendStore();
        var submission = SubmissionOver(store, out _);
        var declaration = await submission.DeclareAsync(
            RequestFor("Daily at 09:00"),
            TestContext.Current.CancellationToken);
        await submission.CancelAsync(declaration.Id, TestContext.Current.CancellationToken);

        // Act
        var cancellation = await submission.CancelAsync(declaration.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(RecurringSendCancellation.AlreadyCancelled, cancellation);
    }

    /// <summary>A declaration nothing holds is answered rather than raised over, because the caller cannot tell the two apart.</summary>
    [Fact]
    public async Task CancelAsync_ADeclarationNothingHolds_AnswersThatThereIsNone()
    {
        // Arrange
        var submission = SubmissionOver(new InMemoryRecurringSendStore(), out _);

        // Act
        var cancellation = await submission.CancelAsync(
            RecurringSendId.Create(Guid.CreateVersion7()),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(RecurringSendCancellation.NotFound, cancellation);
    }

    /// <summary>Stopping what repeats is deciding about somebody's correspondents, so it asks for the same grant declaring one does.</summary>
    [Fact]
    public async Task CancelAsync_CallerWithoutTheSendGrant_Refuses()
    {
        // Arrange
        var submission = SubmissionOver(
            new InMemoryRecurringSendStore(),
            out _,
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => submission.CancelAsync(
                RecurringSendId.Create(Guid.CreateVersion7()),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailSend, refusal.RequiredPermission);
    }

    /// <summary>
    /// The draft is handed over before anything opens a unit of work, which is what makes the object backend legal
    /// here: joining a session is what opens its transaction, so a placement made while no session exists is one made
    /// with no transaction open across it.
    /// </summary>
    [Fact]
    public async Task DeclareAsync_ARepetitionThisSystemCanRun_PlacesTheDraftBeforeAnyPersistenceSessionExists()
    {
        // Arrange
        var store = new InMemoryRecurringSendStore(new FakeTimeProvider(Declared));
        var stagedSessions = new List<IPersistenceSession>();
        var submission = SubmissionOver(store, out var contentStore, stagedSessions: stagedSessions);
        var sessionsOpenWhenPlaced = -1;
        contentStore
            .PlaceContentAsync(
                Arg.Any<EmailContentKind>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                sessionsOpenWhenPlaced = stagedSessions.Count;

                return Task.FromResult(PlacedEmailContent.InDatabase(call.ArgAt<ReadOnlyMemory<byte>>(1)));
            });

        // Act
        await submission.DeclareAsync(
            RequestFor("Daily at 09:00 Europe/Warsaw"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, sessionsOpenWhenPlaced);
        Assert.Single(stagedSessions);
    }

    /// <summary>
    /// A conflicted attempt replays the whole unit of work, and the placement is not part of it. Every attempt stages
    /// the same locator over the same object, so the endpoint sees one write however many times the commit is repeated.
    /// </summary>
    [Fact]
    public async Task DeclareAsync_APersistenceConflictThenACommit_PlacesTheDraftOnceAcrossBothAttempts()
    {
        // Arrange
        var store = new InMemoryRecurringSendStore(new FakeTimeProvider(Declared));
        var stagedSessions = new List<IPersistenceSession>();
        var submission = SubmissionOver(
            store,
            out var contentStore,
            stagedSessions: stagedSessions,
            conflictingAttempts: 1);

        // Act
        var declaration = await submission.DeclareAsync(
            RequestFor("Daily at 09:00 Europe/Warsaw"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, stagedSessions.Count);
        await contentStore.Received(1).PlaceContentAsync(
            EmailContentKind.RecurringSendDraft,
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<CancellationToken>());
        await contentStore.Received(2).SaveRecurringSendDraftAsync(
            Arg.Any<IPersistenceSession>(),
            declaration.Id,
            Arg.Any<PlacedEmailContent>(),
            Arg.Any<CancellationToken>());
    }

    private static RecurringMailSubmissionRequest RequestFor(string schedule) => new()
    {
        Account = MailAccountSelector.For(Account),
        Recipients = [NamedRecipient.AtAddress(OutgoingRecipientRole.To, "anna@example.test")],
        Subject = "Weekly report",
        PlainTextBody = "Here it is.",
        Requester = OutgoingEmailRequester.Command("declare-1"),
        Schedule = schedule,
    };

    private static RecurringMailSubmission SubmissionOver(
        IRecurringSendStore recurringSends,
        out IEmailContentStore contentStore,
        AccessAuthorization? authorization = null,
        List<IPersistenceSession>? stagedSessions = null,
        int conflictingAttempts = 0)
    {
        contentStore = ContentStores.Substituted();

        // One authorization, for the reason AuthoredSendGovernors.Governing states: the mailboxes the send leaves from,
        // the book the recipients are resolved out of, and the caller the send is judged for are one scoped instance in
        // production.
        var callerAuthorization = authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend);
        var accountCatalog = OwnedMailAccountCatalogs.For(callerAuthorization, SyntheticServedAccount.Of(Account));

        var conflictsLeft = conflictingAttempts;
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var session = Substitute.For<IPersistenceSession>();
            session.CommitAsync(Arg.Any<CancellationToken>()).Returns(
                conflictsLeft-- > 0
                    ? PersistenceCommitResult.ConcurrencyConflict
                    : PersistenceCommitResult.Committed);
            stagedSessions?.Add(session);

            return session;
        });

        return new RecurringMailSubmission(
            accountCatalog,
            new NamedRecipientResolver(new InMemoryContactBookStore(), ContactBookOwnerships.For(callerAuthorization)),
            ComposingAuthoredEmails.ThatComposes(ComposedMime),
            recurringSends,
            contentStore,
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                TimeProvider.System),
            callerAuthorization);
    }

}
