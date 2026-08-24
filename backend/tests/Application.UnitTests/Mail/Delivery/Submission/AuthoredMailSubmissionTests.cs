// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Jobs;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Mail.Delivery.Submission;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Governance;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Scheduling;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Submission;

/// <summary>Covers the one use case a boundary reaches to send a message that answers nothing.</summary>
/// <remarks>
/// <para>
/// The outbox behind it is the real one over an in-memory store, because the claims about idempotency and about a
/// refusal leaving nothing behind are claims about what was written down. The composer is a substitute, and only there:
/// what it produces is MIME, which is the MimeKit adapter's own suite to prove, while what this use case owes is that
/// the composition is reached with the right message and that its refusal reaches the caller as a coded failure.
/// </para>
/// <para>
/// Two properties are asserted throughout rather than once: a refusal writes no record and signals no account, and no
/// refusal message repeats an address, a subject, or a body.
/// </para>
/// </remarks>
public sealed class AuthoredMailSubmissionTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly DateTimeOffset Recorded = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    private static readonly ReadOnlyMemory<byte> ComposedMime =
        Encoding.ASCII.GetBytes("Message-ID: <one@example.test>\r\n\r\nHello.").AsMemory();

    [Fact]
    public async Task SubmitAsync_AMessageSomebodyWrote_QueuesItAgainstTheAccountTheyNamed()
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore();
        var submission = SubmissionOver(store, out _, out var signal);

        // Act
        var record = await submission.SubmitAsync(
            RequestTo("anna@example.test"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Account, record.AccountId);
        Assert.Equal(OutgoingEmailStage.Recorded, record.Stage);
        Assert.Equal(ComposedMime.Length, record.MimeByteLength);
        Assert.Equal(1, signal.Depth);
    }

    /// <summary>The account is named the way every other request names one, and the composition is told the identity it resolved to.</summary>
    [Fact]
    public async Task SubmitAsync_AccountNamedByItsDisplayName_ComposesAsTheAccountThatNameSelects()
    {
        // Arrange
        var submission = SubmissionOver(new InMemoryOutgoingEmailStore(), out var composer, out _);
        var request = RequestTo("anna@example.test") with
        {
            Account = MailAccountSelector.Create(SyntheticServedAccount.Of(Account).DisplayName.Value),
        };

        // Act
        await submission.SubmitAsync(request, TestContext.Current.CancellationToken);

        // Assert
        composer.Received(1).Compose(
            Account,
            request.Requester,
            Arg.Any<AuthoredEmail>(),
            Arg.Any<MailDeliveryCapabilities>());
    }

    /// <summary>What the author wrote is what is composed, and the sending address is not among the things they can write.</summary>
    [Fact]
    public async Task SubmitAsync_AMessageSomebodyWrote_ComposesTheFieldsTheyWroteAndNoThreading()
    {
        // Arrange
        var submission = SubmissionOver(new InMemoryOutgoingEmailStore(), out var composer, out _);
        var request = RequestTo("anna@example.test") with
        {
            Subject = "Lunch on Thursday",
            PlainTextBody = "Shall we?",
            HtmlBody = "<p>Shall we?</p>",
        };

        // Act
        await submission.SubmitAsync(request, TestContext.Current.CancellationToken);

        // Assert
        var authored = ComposedMessage(composer);
        Assert.Equal("Lunch on Thursday", authored.Subject);
        Assert.Equal("Shall we?", authored.PlainTextBody);
        Assert.Equal("<p>Shall we?</p>", authored.HtmlBody);
        Assert.Empty(authored.Attachments);
        Assert.False(authored.Threading.IsThreaded);
        Assert.Equal("anna@example.test", Assert.Single(authored.Recipients).Address);
    }

    /// <summary>
    /// No submission server has been asked anything when a send is written down, so the composition is held to the
    /// answers that stay correct whatever one turns out to say.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_AMessageSomebodyWrote_ComposesAgainstWhatNoServerHasSaidYet()
    {
        // Arrange
        var submission = SubmissionOver(new InMemoryOutgoingEmailStore(), out var composer, out _);

        // Act
        await submission.SubmitAsync(RequestTo("anna@example.test"), TestContext.Current.CancellationToken);

        // Assert
        composer.Received(1).Compose(
            Arg.Any<MailAccountId>(),
            Arg.Any<OutgoingEmailRequester>(),
            Arg.Any<AuthoredEmail>(),
            MailDeliveryCapabilities.BeforeAnyServerHasSpoken);
    }

    /// <summary>Somebody named out of the book is addressed by the resolution every author shares, and nothing else here knows about contacts.</summary>
    [Fact]
    public async Task SubmitAsync_RecipientNamedOutOfTheBook_ComposesTheAddressTheyPrefer()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        var anna = ContactOf("Anna Kowalska", "anna@example.test");
        book.Hold(anna);
        var submission = SubmissionOver(new InMemoryOutgoingEmailStore(), out var composer, out _, book);
        var request = RequestTo("anna@example.test") with
        {
            Recipients = [NamedRecipient.ByContact(OutgoingRecipientRole.To, anna.Id)],
        };

        // Act
        await submission.SubmitAsync(request, TestContext.Current.CancellationToken);

        // Assert
        var recipient = Assert.Single(ComposedMessage(composer).Recipients);
        Assert.Equal("anna@example.test", recipient.Address);
        Assert.Equal(anna.Id, recipient.Contact);
    }

    /// <summary>
    /// The grant is asked for before the book is read and before anything is composed, so a caller without it spends
    /// nothing and learns nothing about who this deployment holds.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_CallerWithoutTheSendGrant_RefusesBeforeComposingAnything()
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore();
        var submission = SubmissionOver(
            store,
            out var composer,
            out var signal,
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => submission.SubmitAsync(RequestTo("anna@example.test"), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailSend, refusal.RequiredPermission);
        Assert.Empty(store.OpenRequests);
        Assert.Equal(0, signal.Depth);
        composer.DidNotReceiveWithAnyArgs().Compose(default, default!, default!, default!);
    }

    /// <summary>An account nobody serves and text naming no account at all are one answer, and neither writes anything down.</summary>
    [Fact]
    public async Task SubmitAsync_AccountThisDeploymentDoesNotServe_RefusesAndWritesNothing()
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore();
        var submission = SubmissionOver(store, out var composer, out _);
        var request = RequestTo("anna@example.test") with { Account = MailAccountSelector.Create("archive") };

        // Act
        var refusal = await Assert.ThrowsAsync<MailAccountNotAccessibleException>(
            () => submission.SubmitAsync(request, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailAccountNotAccessible, refusal.ErrorCode);
        Assert.Empty(store.OpenRequests);
        composer.DidNotReceiveWithAnyArgs().Compose(default, default!, default!, default!);
    }

    /// <summary>
    /// A list longer than a record could ever hold is refused before the book is read, because the reads carry what the
    /// caller supplied and the resolution treats that length as a defect in whoever called it.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_MoreRecipientsThanARecordHolds_RefusesAsABoundRatherThanFailing()
    {
        // Arrange
        var submission = SubmissionOver(new InMemoryOutgoingEmailStore(), out var composer, out _);
        var request = RequestTo("anna@example.test") with
        {
            Recipients =
            [
                .. Enumerable
                    .Range(0, OutgoingEmailRequest.MaximumRecipientCount + 1)
                    .Select(position => NamedRecipient.AtAddress(
                        OutgoingRecipientRole.To,
                        $"person{position}@example.test")),
            ],
        };

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => submission.SubmitAsync(request, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailBoundExceeded, refusal.ErrorCode);
        Assert.Contains(
            OutgoingEmailRequest.MaximumRecipientCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            refusal.Message,
            StringComparison.Ordinal);
        composer.DidNotReceiveWithAnyArgs().Compose(default, default!, default!, default!);
    }

    /// <summary>One recipient that resolves to nobody refuses the whole message, and the refusal names nobody it counted.</summary>
    [Fact]
    public async Task SubmitAsync_RecipientNamingAContactTheBookDoesNotHold_RefusesTheWholeMessage()
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore();
        var submission = SubmissionOver(store, out var composer, out _);
        var request = RequestTo("anna@example.test") with
        {
            Recipients =
            [
                NamedRecipient.AtAddress(OutgoingRecipientRole.To, "anna@example.test"),
                NamedRecipient.ByContactName(OutgoingRecipientRole.Cc, ContactDisplayName.Create("Nobody At All")),
            ],
        };

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => submission.SubmitAsync(request, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailRecipientUnresolved, refusal.ErrorCode);
        Assert.DoesNotContain("anna@example.test", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Nobody At All", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(store.OpenRequests);
        composer.DidNotReceiveWithAnyArgs().Compose(default, default!, default!, default!);
    }

    /// <summary>A name several people carry addresses nobody, and what the caller is told is how many rather than which.</summary>
    [Fact]
    public async Task SubmitAsync_RecipientNamingANameSeveralContactsCarry_RefusesWithTheCountAndNoNames()
    {
        // Arrange
        var book = new InMemoryContactBookStore();
        book.Hold(ContactOf("Anna Kowalska", "anna.one@example.test"));
        book.Hold(ContactOf("Anna Kowalska", "anna.two@example.test"));
        var submission = SubmissionOver(new InMemoryOutgoingEmailStore(), out _, out _, book);
        var request = RequestTo("anna@example.test") with
        {
            Recipients = [NamedRecipient.ByContactName(OutgoingRecipientRole.To, ContactDisplayName.Create("Anna Kowalska"))],
        };

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => submission.SubmitAsync(request, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailRecipientUnresolved, refusal.ErrorCode);
        Assert.Contains("2 contacts", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("anna.one@example.test", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("anna.two@example.test", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Every way a composition can refuse reaches the caller as a code it can act on, rather than as a failure the boundary cannot describe.</summary>
    [Theory]
    [InlineData(AuthoredEmailRefusalReason.SenderUnconfigured, 56002)]
    [InlineData(AuthoredEmailRefusalReason.HeaderInjected, 51013)]
    [InlineData(AuthoredEmailRefusalReason.FieldUnusable, 51013)]
    [InlineData(AuthoredEmailRefusalReason.InternationalizationUnsupported, 51013)]
    [InlineData(AuthoredEmailRefusalReason.BoundExceeded, 51014)]
    public async Task SubmitAsync_CompositionRefused_RaisesTheCodeThatRefusalIsPublishedUnder(
        AuthoredEmailRefusalReason reason,
        int expectedErrorCode)
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore();
        var submission = SubmissionOver(store, out var composer, out var signal);
        composer
            .Compose(
                Arg.Any<MailAccountId>(),
                Arg.Any<OutgoingEmailRequester>(),
                Arg.Any<AuthoredEmail>(),
                Arg.Any<MailDeliveryCapabilities>())
            .Returns(AuthoredEmailComposition.Refused(reason, AuthoredEmailField.Subject, bound: 64));

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => submission.SubmitAsync(RequestTo("anna@example.test"), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(expectedErrorCode, refusal.ErrorCode.Value);
        Assert.Empty(store.OpenRequests);
        Assert.Equal(0, signal.Depth);
    }

    /// <summary>A bound names the number the deployment configured, so a caller learns how much less to write.</summary>
    [Fact]
    public async Task SubmitAsync_CompositionRefusedOnABound_NamesTheBoundAndNotWhatWasMeasured()
    {
        // Arrange
        var submission = SubmissionOver(new InMemoryOutgoingEmailStore(), out var composer, out _);
        composer
            .Compose(
                Arg.Any<MailAccountId>(),
                Arg.Any<OutgoingEmailRequester>(),
                Arg.Any<AuthoredEmail>(),
                Arg.Any<MailDeliveryCapabilities>())
            .Returns(AuthoredEmailComposition.Refused(
                AuthoredEmailRefusalReason.BoundExceeded,
                AuthoredEmailField.PlainTextBody,
                bound: 100_000));

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => submission.SubmitAsync(RequestTo("anna@example.test"), TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("plainTextBody", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("100000", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>The same key twice is one message, which is the whole of what makes a retried call safe to make.</summary>
    [Fact]
    public async Task SubmitAsync_SameIdempotencyKeyTwice_AnswersWithOneRecord()
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore();
        var submission = SubmissionOver(store, out _, out _);
        var first = await submission.SubmitAsync(
            RequestTo("anna@example.test"),
            TestContext.Current.CancellationToken);

        // Act
        var retried = await submission.SubmitAsync(
            RequestTo("anna@example.test"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(first.Id, retried.Id);
        var outstanding = await store.ReadOutstandingAsync(Account, limit: 10, TestContext.Current.CancellationToken);
        Assert.Single(outstanding);
    }

    /// <summary>A retry is the send it repeats, so the trail of it carries one entry however often the call is made.</summary>
    [Fact]
    public async Task SubmitAsync_SameIdempotencyKeyTwice_RecordsTheSendOnce()
    {
        // Arrange
        var auditor = Substitute.For<IAuthoredSendAuditor>();
        var submission = SubmissionOver(
            new InMemoryOutgoingEmailStore(),
            out _,
            out _,
            governor: AuthoredSendGovernors.Governing(auditor: auditor));
        await submission.SubmitAsync(RequestTo("anna@example.test"), TestContext.Current.CancellationToken);

        // Act
        await submission.SubmitAsync(RequestTo("anna@example.test"), TestContext.Current.CancellationToken);

        // Assert
        await auditor.Received(1).RecordAuthoredSendAsync(
            Arg.Any<AuthoredSend>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A key of its own is a message of its own, which is what lets somebody write to the same person twice.</summary>
    [Fact]
    public async Task SubmitAsync_ASecondMessageWithItsOwnKey_IsASecondRecord()
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore();
        var submission = SubmissionOver(store, out _, out _);
        var first = await submission.SubmitAsync(
            RequestTo("anna@example.test"),
            TestContext.Current.CancellationToken);

        // Act
        var second = await submission.SubmitAsync(
            RequestTo("anna@example.test") with { Requester = OutgoingEmailRequester.Command("send-2") },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(first.Id, second.Id);
        var outstanding = await store.ReadOutstandingAsync(Account, limit: 10, TestContext.Current.CancellationToken);
        Assert.Equal(2, outstanding.Count);
    }

    /// <summary>
    /// The bounds are judged in the use case rather than at the tool, so a boundary reaching this one another way meets
    /// the same refusal and leaves the same nothing behind.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_RecipientNothingVouchesFor_IsRefusedWithoutWritingARecord()
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore();
        var submission = SubmissionOver(
            store,
            out _,
            out var signal,
            governor: AuthoredSendGovernors.Governing(
                settings: new AuthoredSendSettings(UnvouchedRecipientPosture.Refuse)));

        // Act
        var refusal = await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => submission.SubmitAsync(
                RequestTo("accomplice@elsewhere.test"),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingRecipientUnvouched, refusal.ErrorCode);
        Assert.DoesNotContain("elsewhere.test", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(await store.ReadOutstandingAsync(Account, limit: 10, TestContext.Current.CancellationToken));
        Assert.Equal(0, signal.Depth);
    }

    /// <summary>A recipient this deployment may never write to is refused on the surface a caller reaches as well.</summary>
    [Fact]
    public async Task SubmitAsync_RecipientTheDeploymentMayNeverWriteTo_IsRefusedWithoutWritingARecord()
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore();
        Assert.True(OutgoingRecipientRule.TryCreateForDomain("elsewhere.test", out var denied));
        var submission = SubmissionOver(
            store,
            out _,
            out _,
            governor: AuthoredSendGovernors.Governing(
                recipientPolicy: OutgoingRecipientPolicy.Create([], [denied])));

        // Act
        var refusal = await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => submission.SubmitAsync(
                RequestTo("accomplice@elsewhere.test"),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingRecipientRefusedByPolicy, refusal.ErrorCode);
        Assert.Empty(await store.ReadOutstandingAsync(Account, limit: 10, TestContext.Current.CancellationToken));
    }

    /// <summary>What a caller was admitted for is counted, so the caller that filled its period is refused the next message.</summary>
    [Fact]
    public async Task SubmitAsync_CallerThatFilledItsOwnPeriod_IsRefusedTheNextMessage()
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore();
        var ledger = new AuthoredSendUsageLedger(
            AuthoredSendCeilings.Create(TimeSpan.FromDays(1), maxMessagesPerCaller: 1, maxRecipientsPerCaller: 0),
            new FakeTimeProvider(Recorded));
        var submission = SubmissionOver(
            store,
            out _,
            out _,
            governor: AuthoredSendGovernors.Governing(ledger: ledger));
        await submission.SubmitAsync(RequestTo("anna@example.test"), TestContext.Current.CancellationToken);

        // Act
        var refusal = await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => submission.SubmitAsync(
                RequestTo("anna@example.test") with { Requester = OutgoingEmailRequester.Command("send-2") },
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingMailCeilingReached, refusal.ErrorCode);
        Assert.Single(await store.ReadOutstandingAsync(Account, limit: 10, TestContext.Current.CancellationToken));
    }

    private static AuthoredEmail ComposedMessage(IAuthoredEmailComposer composer) => (AuthoredEmail)composer
        .ReceivedCalls()
        .Single(call => call.GetMethodInfo().Name == nameof(IAuthoredEmailComposer.Compose))
        .GetArguments()[2]!;

    /// <summary>
    /// A message written for a time that has already gone is refused where the author is still present to be told,
    /// rather than sent at once — the two readings of a past time are opposite and a system that guessed would
    /// sometimes send a message somebody was still writing.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_ADueTimeThatHasAlreadyPassed_IsRefusedBeforeAnythingIsComposed()
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore();
        var submission = SubmissionOver(store, out var composer, out var signal);
        var request = RequestTo("anna@example.test") with
        {
            DueAt = ZonedInstant.At(Recorded),
        };

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => submission.SubmitAsync(request, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailScheduleRefused, refusal.ErrorCode);
        Assert.Empty(store.OpenRequests);
        Assert.Equal(0, signal.Depth);
        composer.DidNotReceiveWithAnyArgs().Compose(default!, default!, default!, default!);
    }

    /// <summary>The refusal states the rule and never repeats the time, the address, or anything else the caller sent.</summary>
    [Fact]
    public async Task SubmitAsync_ADueTimeThatHasAlreadyPassed_SaysNothingAboutTheMessage()
    {
        // Arrange
        var submission = SubmissionOver(new InMemoryOutgoingEmailStore(), out _, out _);
        var request = RequestTo("anna@example.test") with
        {
            Subject = "Quarterly figures",
            DueAt = ZonedInstant.At(Recorded.AddDays(-1)),
        };

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => submission.SubmitAsync(request, TestContext.Current.CancellationToken));

        // Assert
        Assert.DoesNotContain("anna", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Quarterly", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("2026", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A time still to come is written onto the record, which is what makes the send unclaimable until it arrives.</summary>
    [Fact]
    public async Task SubmitAsync_ADueTimeStillToCome_RecordsTheSendHeldUntilIt()
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore();
        var submission = SubmissionOver(store, out _, out var signal);
        var dueAt = ZonedInstant.At(Recorded.AddHours(9));
        var request = RequestTo("anna@example.test") with { DueAt = dueAt };

        // Act
        var record = await submission.SubmitAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(dueAt, record.DueAt);
        Assert.Equal(dueAt.Instant, record.AvailableAt);
        Assert.True(record.IsWaitingAt(Recorded));
        Assert.Equal(0, signal.Depth);
    }

    private static MailSubmissionRequest RequestTo(string address) => new()
    {
        Account = MailAccountSelector.For(Account),
        Recipients = [NamedRecipient.AtAddress(OutgoingRecipientRole.To, address)],
        Subject = "Hello",
        PlainTextBody = "Hello.",
        Requester = OutgoingEmailRequester.Command("send-1"),
    };

    private static AuthoredMailSubmission SubmissionOver(
        InMemoryOutgoingEmailStore store,
        out IAuthoredEmailComposer composer,
        out MailOutboxSignal signal,
        InMemoryContactBookStore? book = null,
        AccessAuthorization? authorization = null,
        AuthoredSendGovernor? governor = null)
    {
        composer = ComposingAuthoredEmails.ThatComposes(ComposedMime);
        signal = new MailOutboxSignal(capacity: 8);

        var accountCatalog = Substitute.For<IDeploymentMailAccountCatalog>();
        accountCatalog.ServedAccounts.Returns([SyntheticServedAccount.Of(Account)]);

        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var session = Substitute.For<IPersistenceSession>();
            session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);

            return session;
        });

        return new AuthoredMailSubmission(
            accountCatalog,
            new NamedRecipientResolver(book ?? new InMemoryContactBookStore()),
            composer,
            new MailOutbox(
                store,
                Substitute.For<IEmailContentStore>(),
                new OptimisticConcurrencyRetryPolicy(
                    sessionFactory,
                    new PersistenceConcurrencyOptions(),
                    new FakeTimeProvider()),
                signal,
                Substitute.For<IJobStore>(),
                Substitute.For<IOutboxOperationStore>(),
                authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend),
                OutgoingMailGovernors.Permitting(),
                OutgoingMailScreenings.Inactive(),
                new FakeTimeProvider(Recorded)),
            governor ?? AuthoredSendGovernors.Permitting(authorization),
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend),
            new FakeTimeProvider(Recorded));
    }

    private static Contact ContactOf(string displayName, params string[] addresses) => Contact.Create(
        ContactId.Create(Guid.CreateVersion7(Recorded)),
        ContactDisplayName.Create(displayName),
        [.. addresses.Select(Address)],
        Address(addresses[0]),
        note: null,
        ContactOrigin.Asserted,
        Recorded,
        Recorded);

    private static EmailAddress Address(string address)
    {
        if (!EmailAddress.TryCreate(displayName: null, address, out var emailAddress))
        {
            throw new InvalidOperationException($"The test address '{address}' names no mailbox.");
        }

        return emailAddress;
    }
}
