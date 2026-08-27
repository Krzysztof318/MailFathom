// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.BrowseThread;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Emails.Threads;
using MailFathom.Application.Observability;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.BrowseThread;

/// <summary>Covers the conversation a thread screen is drawn from: what it spans, the order it has, and how it is paged.</summary>
public sealed class MailThreadBrowserTests
{
    /// <summary>The literal the scanner in the guarded-egress tests reports, standing in for a credential in mail.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

    private static readonly MailAccountId Account = MailAccountId.Create("personal");
    private static readonly MailAccountId SecondAccount = MailAccountId.Create("work");
    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");
    private static readonly MailFolderAlias Sent = MailFolderAlias.Create("SENT");
    private static readonly MailFolderAlias Withheld = MailFolderAlias.Create("PRIVATE");

    private static readonly EmailThreadId Conversation =
        EmailThreadId.Create(new Guid("11111111-1111-1111-1111-111111111111"));

    private static readonly EmailThreadId OtherConversation =
        EmailThreadId.Create(new Guid("22222222-2222-2222-2222-222222222222"));

    /// <summary>The question is in the inbox and the answer is in the sent folder, so a thread scoped to one shows half an exchange.</summary>
    [Fact]
    public async Task BrowsePageAsync_AConversationSpanningTwoFolders_ReturnsBothOfThem()
    {
        // Arrange
        var question = Message(1, Inbox, "2026-08-16T09:00:00Z");
        var answer = Message(2, Sent, "2026-08-16T10:00:00Z", answers: question);
        var browser = BrowserOver([question, answer]);

        // Act
        var thread = await browser.BrowsePageAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(thread);
        Assert.Equal(
            [question.StoredEmailId, answer.StoredEmailId],
            thread.Messages.Select(message => message.Email.StoredEmailId));
        Assert.Equal(2, thread.MessageCount);
    }

    /// <summary>
    /// The read narrows by no account, which is why a caller opening a conversation does not have to say which mailbox
    /// they were looking at. Today's assembler keeps a conversation inside the account that holds it, so this pins that
    /// nothing in this reading adds a narrowing of its own on top of the ownership one.
    /// </summary>
    [Fact]
    public async Task BrowsePageAsync_AConversationHeldInTwoOfTheOwnersAccounts_NarrowsByNeitherOfThem()
    {
        // Arrange
        var here = Message(1, Inbox, "2026-08-16T09:00:00Z");
        var there = Message(2, Inbox, "2026-08-16T10:00:00Z", accountId: SecondAccount, answers: here);
        var browser = BrowserOver([here, there], ownedAccounts: [Account, SecondAccount]);

        // Act
        var thread = await browser.BrowsePageAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(thread);
        Assert.Equal(2, thread.Messages.Count);
    }

    /// <summary>A message in a folder an operator withheld is in no conversation this surface publishes and in no count of one.</summary>
    [Fact]
    public async Task BrowsePageAsync_AConversationReachingAWithheldFolder_PublishesNeitherTheMessageNorItsAuthor()
    {
        // Arrange
        var opening = Message(1, Inbox, "2026-08-16T09:00:00Z", sender: "anna@example.test");
        var withheld = Message(2, Withheld, "2026-08-16T10:00:00Z", sender: "marek@example.test", answers: opening);
        var browser = BrowserOver([opening, withheld]);

        // Act
        var thread = await browser.BrowsePageAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(thread);
        Assert.Equal([opening.StoredEmailId], thread.Messages.Select(message => message.Email.StoredEmailId));
        Assert.Equal(1, thread.MessageCount);
        Assert.Equal(["anna@example.test"], thread.Participants.Select(participant => participant.Address));
    }

    /// <summary>The reply relation decides the order, so a reply follows what it answers however its sender dated it.</summary>
    [Fact]
    public async Task BrowsePageAsync_AReplyDatedBeforeWhatItAnswers_KeepsItUnderTheMessageItAnswers()
    {
        // Arrange
        var opening = Message(1, Inbox, "2026-08-16T10:00:00Z");
        var reply = Message(2, Inbox, "2026-08-16T09:00:00Z", answers: opening);
        var browser = BrowserOver([opening, reply]);

        // Act
        var thread = await browser.BrowsePageAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(thread);
        Assert.Equal(
            [opening.StoredEmailId, reply.StoredEmailId],
            thread.Messages.Select(message => message.Email.StoredEmailId));
        Assert.Equal([0, 1], thread.Messages.Select(message => message.Position));
        Assert.Equal(opening.StoredEmailId, thread.Messages[1].AnsweredStoredEmailId);
    }

    /// <summary>A page is bounded, and what the whole conversation holds is stated beside it rather than left to be counted.</summary>
    [Fact]
    public async Task BrowsePageAsync_AConversationLongerThanThePage_BoundsThePageAndStatesTheWholeCount()
    {
        // Arrange
        var browser = BrowserOver(ConversationOf(6));

        // Act
        var thread = await browser.BrowsePageAsync(Request(pageSize: 2), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(thread);
        Assert.Equal(2, thread.Messages.Count);
        Assert.Equal(2, thread.PageSize);
        Assert.Equal(6, thread.MessageCount);
        Assert.NotNull(thread.NextCursor);
    }

    /// <summary>Paging a conversation may neither skip a message nor repeat one, which is what makes it a document rather than a sample.</summary>
    [Fact]
    public async Task BrowsePageAsync_TheNextCursor_ContinuesTheConversationWithoutSkippingOrRepeatingAMessage()
    {
        // Arrange
        var messages = ConversationOf(5);
        var browser = BrowserOver(messages);

        // Act
        var first = await browser.BrowsePageAsync(Request(pageSize: 2), TestContext.Current.CancellationToken);
        var second = await browser.BrowsePageAsync(
            Request(pageSize: 2, cursor: first!.NextCursor),
            TestContext.Current.CancellationToken);
        var third = await browser.BrowsePageAsync(
            Request(pageSize: 2, cursor: second!.NextCursor),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(third);
        Assert.Equal(
            messages.Select(message => message.StoredEmailId),
            new[] { first, second, third }.SelectMany(page => page!.Messages)
                .Select(message => message.Email.StoredEmailId));
        Assert.Null(third.NextCursor);
    }

    /// <summary>A request naming no page size runs under the default, which is what the answer reports back.</summary>
    [Fact]
    public async Task BrowsePageAsync_NoPageSizeNamed_ReportsTheDefaultItRanUnder()
    {
        // Arrange
        var browser = BrowserOver(ConversationOf(2));

        // Act
        var thread = await browser.BrowsePageAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(thread);
        Assert.Equal(MailboxQueryPageSize.DefaultValue, thread.PageSize);
    }

    /// <summary>A page size outside the range is refused rather than clamped, so a screen learns the bound it asked past.</summary>
    [Fact]
    public async Task BrowsePageAsync_APageSizeOutsideTheRange_IsRefused()
    {
        // Arrange
        var browser = BrowserOver(ConversationOf(2));

        // Act, Assert
        await Assert.ThrowsAsync<MailboxQueryPageSizeOutOfRangeException>(() =>
            browser.BrowsePageAsync(
                Request(pageSize: MailboxQueryPageSize.MaximumValue + 1),
                TestContext.Current.CancellationToken));
    }

    /// <summary>A cursor this deployment never issued names no boundary, so it is refused rather than read as the beginning.</summary>
    [Fact]
    public async Task BrowsePageAsync_ACursorThisDeploymentNeverIssued_IsRefused()
    {
        // Arrange
        var browser = BrowserOver(ConversationOf(3));

        // Act, Assert
        await Assert.ThrowsAsync<MailboxQueryCursorMalformedException>(() =>
            browser.BrowsePageAsync(Request(cursor: "not-a-cursor"), TestContext.Current.CancellationToken));
    }

    /// <summary>A cursor names a boundary in one conversation, so presenting it against another is a different mistake with a different repair.</summary>
    [Fact]
    public async Task BrowsePageAsync_ACursorIssuedForAnotherConversation_IsRefusedAsAMismatch()
    {
        // Arrange
        var browser = BrowserOver(ConversationOf(3));
        var elsewhere = EmailThreadCursor
            .After(StoredEmailId.Create(Guid.CreateVersion7()), EmailThreadCursor.FingerprintOf(OtherConversation))
            .Encode();

        // Act, Assert
        await Assert.ThrowsAsync<MailboxQueryCursorFilterMismatchException>(() =>
            browser.BrowsePageAsync(Request(cursor: elsewhere), TestContext.Current.CancellationToken));
    }

    /// <summary>A boundary whose message has left the conversation is refused, because answering with the first page would read as having jumped to the top.</summary>
    [Fact]
    public async Task BrowsePageAsync_ACursorWhoseMessageTheConversationNoLongerShows_IsRefused()
    {
        // Arrange
        var browser = BrowserOver(ConversationOf(3));
        var gone = EmailThreadCursor
            .After(StoredEmailId.Create(Guid.CreateVersion7()), EmailThreadCursor.FingerprintOf(Conversation))
            .Encode();

        // Act, Assert
        await Assert.ThrowsAsync<EmailThreadCursorMessageMissingException>(() =>
            browser.BrowsePageAsync(Request(cursor: gone), TestContext.Current.CancellationToken));
    }

    /// <summary>A conversation running past what one read assembles says so, rather than ending silently at the bound.</summary>
    [Fact]
    public async Task BrowsePageAsync_AConversationLongerThanOneReadAssembles_SaysMoreOfItWasNotAssembled()
    {
        // Arrange
        var browser = BrowserOver(ConversationOf(IEmailThreadReader.MaximumAssembledEmails + 1));

        // Act
        var thread = await browser.BrowsePageAsync(Request(pageSize: 1), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(thread);
        Assert.True(thread.MoreMessagesNotAssembled);
        Assert.Equal(IEmailThreadReader.MaximumAssembledEmails, thread.MessageCount);
    }

    /// <summary>A conversation that ends at the bound is complete, and a reader must not be told part of it is missing.</summary>
    [Fact]
    public async Task BrowsePageAsync_AConversationEndingAtTheBound_SaysNothingWasLeftOut()
    {
        // Arrange
        var browser = BrowserOver(ConversationOf(IEmailThreadReader.MaximumAssembledEmails));

        // Act
        var thread = await browser.BrowsePageAsync(Request(pageSize: 1), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(thread);
        Assert.False(thread.MoreMessagesNotAssembled);
    }

    /// <summary>The header is drawn from the whole conversation, so a client never pages one to find out who is in it.</summary>
    [Fact]
    public async Task BrowsePageAsync_AConversationSeveralPeopleWroteIn_NamesThemAllFromTheFirstPage()
    {
        // Arrange
        var opening = Message(1, Inbox, "2026-08-16T09:00:00Z", sender: "anna@example.test", displayName: "Anna K");
        var reply = Message(2, Inbox, "2026-08-16T10:00:00Z", sender: "marek@example.test", answers: opening);
        var again = Message(3, Sent, "2026-08-16T11:00:00Z", sender: "anna@example.test", displayName: "Anna Kowalska");
        var browser = BrowserOver([opening, reply, again]);

        // Act
        var thread = await browser.BrowsePageAsync(Request(pageSize: 1), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(thread);
        Assert.Single(thread.Messages);
        Assert.Equal(
            [("anna@example.test", "Anna Kowalska", 2), ("marek@example.test", null, 1)],
            thread.Participants.Select(participant =>
                (participant.Address, participant.DisplayName, participant.MessageCount)));
        Assert.False(thread.MoreParticipantsNotNamed);
    }

    /// <summary>
    /// Somebody who renamed themselves is named as they write now, and the conversation's order is not what says which
    /// of their messages is the latest: the reply relation is walked before the clock, so a branch opened early carries
    /// messages written after everything a later branch holds.
    /// </summary>
    [Fact]
    public async Task BrowsePageAsync_AParticipantWhoWroteInTwoBranches_NamesThemAsTheirLatestMessageDid()
    {
        // Arrange
        var opening = Message(1, Inbox, "2026-08-16T09:00:00Z", sender: "marek@example.test");
        var answered = Message(
            2,
            Sent,
            "2026-08-16T12:00:00Z",
            answers: opening,
            sender: "anna@example.test",
            displayName: "Anna Kowalska");
        var separately = Message(
            3,
            Inbox,
            "2026-08-16T10:00:00Z",
            sender: "anna@example.test",
            displayName: "Anna K");
        var browser = BrowserOver([opening, answered, separately]);

        // Act
        var thread = await browser.BrowsePageAsync(Request(pageSize: 1), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(thread);
        Assert.Equal(
            [("marek@example.test", null, 1), ("anna@example.test", "Anna Kowalska", 2)],
            thread.Participants.Select(participant =>
                (participant.Address, participant.DisplayName, participant.MessageCount)));
    }

    /// <summary>A list expansion has an author per message, and a header drawn from hundreds of them is one nobody reads.</summary>
    [Fact]
    public async Task BrowsePageAsync_MoreAuthorsThanTheListNames_NamesThatManyAndSaysItCut()
    {
        // Arrange
        var messages = Enumerable
            .Range(1, BrowsedThread.MaximumNamedParticipants + 3)
            .Select(ordinal => Message(
                ordinal,
                Inbox,
                $"2026-08-16T{ordinal % 24:D2}:00:00Z",
                sender: $"writer{ordinal}@example.test"))
            .ToArray();
        var browser = BrowserOver(messages);

        // Act
        var thread = await browser.BrowsePageAsync(Request(pageSize: 1), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(thread);
        Assert.Equal(BrowsedThread.MaximumNamedParticipants, thread.Participants.Count);
        Assert.True(thread.MoreParticipantsNotNamed);
    }

    /// <summary>A message nobody could establish a sender for names no author, rather than one with an empty address.</summary>
    [Fact]
    public async Task BrowsePageAsync_AMessageWithNoUsableSender_NamesNoParticipantForIt()
    {
        // Arrange
        var browser = BrowserOver([Message(1, Inbox, "2026-08-16T09:00:00Z", sender: null)]);

        // Act
        var thread = await browser.BrowsePageAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(thread);
        Assert.Empty(thread.Participants);
    }

    /// <summary>What each message added is what a collapsed row draws, so it is read for the page and bounded like a list row's.</summary>
    [Fact]
    public async Task BrowsePageAsync_APageOfMessages_CarriesWhatEachOfThemAddedAndReadsItForThePageAlone()
    {
        // Arrange
        var messages = ConversationOf(3);
        var contributions = new InMemoryStoredEmailPreviews()
            .With(messages[0].StoredEmailId, new string('a', EmailPreview.MaximumCharacters + 40));
        var browser = BrowserOver(messages, previewReader: contributions);

        // Act
        var thread = await browser.BrowsePageAsync(Request(pageSize: 1), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(thread);
        Assert.Equal(
            EmailPreview.MaximumCharacters,
            Assert.Single(thread.Messages).Contribution?.Length);
        Assert.Equal(
            [messages[0].StoredEmailId],
            Assert.Single(contributions.Calls));
    }

    /// <summary>A message this deployment stored but never extracted has no contribution, which is not the same as one whose text is empty.</summary>
    [Fact]
    public async Task BrowsePageAsync_AMessageNothingHasExtracted_CarriesNoContribution()
    {
        // Arrange
        var browser = BrowserOver(ConversationOf(1));

        // Act
        var thread = await browser.BrowsePageAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(thread);
        Assert.Null(Assert.Single(thread.Messages).Contribution);
    }

    /// <summary>A message deleted between the two reads is left out rather than published as an identity with nothing behind it.</summary>
    [Fact]
    public async Task BrowsePageAsync_AMessageTheCopyNoLongerHolds_IsLeftOutOfThePage()
    {
        // Arrange
        var messages = ConversationOf(2);
        var browser = BrowserOver(
            messages,
            summaryReader: new InMemoryStoredEmailSummaries().With(StoredOf(messages[0])));

        // Act
        var thread = await browser.BrowsePageAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(thread);
        Assert.Equal([messages[0].StoredEmailId], thread.Messages.Select(message => message.Email.StoredEmailId));
        Assert.Equal(2, thread.MessageCount);
    }

    /// <summary>Only the page's messages are read in full, so opening a long conversation costs a page rather than the exchange.</summary>
    [Fact]
    public async Task BrowsePageAsync_APageOfALongConversation_ReadsTheMessagesOfThatPageAlone()
    {
        // Arrange
        var messages = ConversationOf(8);
        var summaries = new InMemoryStoredEmailSummaries().WithAll(messages.Select(StoredOf));
        var browser = BrowserOver(messages, summaryReader: summaries);

        // Act
        var thread = await browser.BrowsePageAsync(Request(pageSize: 3), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(thread);
        Assert.Equal(
            thread.Messages.Select(message => message.Email.StoredEmailId),
            Assert.Single(summaries.Calls));
    }

    /// <summary>A conversation nobody holds and one this owner may not see answer identically, so neither discloses the other.</summary>
    [Fact]
    public async Task BrowsePageAsync_AnIdentifierNamingNoConversationThisCallerMaySee_AnswersWithNothing()
    {
        // Arrange
        var browser = BrowserOver([Message(1, Withheld, "2026-08-16T09:00:00Z")]);

        // Act
        var thread = await browser.BrowsePageAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(thread);
    }

    /// <summary>An owner who owns no account reads nothing rather than every other owner's conversation.</summary>
    [Fact]
    public async Task BrowsePageAsync_AnOwnerWhoOwnsNoAccount_AnswersWithNothingWithoutReachingStorage()
    {
        // Arrange
        var threadReader = new StubEmailThreadReader([.. ConversationOf(2).Select(message => (Conversation, message))]);
        var browser = BrowserOver(threadReader, ownedAccounts: []);

        // Act
        var thread = await browser.BrowsePageAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(thread);
        Assert.Equal(0, threadReader.ReadCount);
    }

    /// <summary>Everything on the page a message's author wrote is scanned, the participant list included, so a header and its rows agree.</summary>
    [Fact]
    public async Task BrowsePageAsync_ADeploymentThatScans_RedactsTheSubjectTheSenderNamesAndTheContribution()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var message = Message(1, Inbox, "2026-08-16T09:00:00Z", displayName: $"deploy bot {Marker}");
        var summaries = new InMemoryStoredEmailSummaries().With(StoredOf(message) with
        {
            Subject = $"the key is {Marker}",
            SenderDisplayName = $"deploy bot {Marker}",
        });
        var contributions = new InMemoryStoredEmailPreviews()
            .With(message.StoredEmailId, $"it reads {Marker} today");
        var browser = BrowserOver(
            [message],
            summaryReader: summaries,
            previewReader: contributions,
            egressGuard: egress.Guard);

        // Act
        var thread = await browser.BrowsePageAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(thread);

        var published = Assert.Single(thread.Messages);

        Assert.DoesNotContain(Marker, published.Email.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, published.Email.SenderDisplayName, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, published.Contribution, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, Assert.Single(thread.Participants).DisplayName, StringComparison.Ordinal);
    }

    /// <summary>Serving a conversation a scanner could not read would be the leak the switch was turned on to prevent.</summary>
    [Fact]
    public async Task BrowsePageAsync_ADetectorThatCannotAnswer_RefusesThePageRatherThanServingItUnscanned()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Unavailable(TimeProvider.System);
        var browser = BrowserOver(ConversationOf(1), egressGuard: egress.Guard);

        // Act, Assert
        await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(() =>
            browser.BrowsePageAsync(Request(), TestContext.Current.CancellationToken));
    }

    /// <summary>The read is reported as the operation it is, so a conversation a screen waited on has a use case above its queries in a trace.</summary>
    [Fact]
    public async Task BrowsePageAsync_APageThatWasServed_ReportsTheConversationReadAndWhatItReturned()
    {
        // Arrange
        var readTelemetry = new RecordingMailboxReadTelemetry();
        var browser = BrowserOver(ConversationOf(3), readTelemetry: readTelemetry);

        // Act
        await browser.BrowsePageAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        var read = Assert.Single(readTelemetry.Reads);

        Assert.Equal(MailboxReadOperation.ReadEmailThread, read.Operation);
        Assert.Equal(3, read.ResultCount);
        Assert.True(read.WasClosed);
    }

    /// <summary>The grant is the authority here rather than at the transport, so an entrypoint added later meets the same refusal.</summary>
    [Fact]
    public async Task BrowsePageAsync_ACallerWithoutTheMailReadGrant_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var browser = BrowserOver(
            ConversationOf(2),
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailAsk));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            browser.BrowsePageAsync(Request(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailRead, refusal.RequiredPermission);
    }

    /// <summary>The grant is read before the request is, so a caller that may not read learns nothing about what this deployment accepts.</summary>
    [Fact]
    public async Task BrowsePageAsync_ACallerGrantedNothingSendingAnInvalidRequest_IsRefusedForTheGrant()
    {
        // Arrange
        var browser = BrowserOver(ConversationOf(2), authorization: AccessAuthorizations.ForCallerGranted());

        // Act, Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => browser.BrowsePageAsync(
            Request(pageSize: int.MaxValue),
            TestContext.Current.CancellationToken));
    }

    private static BrowseThreadRequest Request(int? pageSize = null, string? cursor = null) => new()
    {
        ThreadId = Conversation,
        PageSize = pageSize,
        Cursor = cursor,
    };

    private static ThreadedEmailSummary[] ConversationOf(int length) =>
    [
        .. Enumerable
            .Range(1, length)
            .Select(ordinal => Message(ordinal, Inbox, $"2026-08-16T{ordinal % 24:D2}:{ordinal % 60:D2}:00Z")),
    ];

    private static MailThreadBrowser BrowserOver(
        IReadOnlyList<ThreadedEmailSummary> messages,
        IStoredEmailSummaryReader? summaryReader = null,
        IStoredEmailPreviewReader? previewReader = null,
        SensitiveContentEgressGuard? egressGuard = null,
        IMailboxReadTelemetry? readTelemetry = null,
        AccessAuthorization? authorization = null,
        IReadOnlyList<MailAccountId>? ownedAccounts = null) => BrowserOver(
        new StubEmailThreadReader([.. messages.Select(message => (Conversation, message))]),
        summaryReader ?? new InMemoryStoredEmailSummaries().WithAll(messages.Select(StoredOf)),
        previewReader,
        egressGuard,
        readTelemetry,
        authorization,
        ownedAccounts);

    private static MailThreadBrowser BrowserOver(
        StubEmailThreadReader threadReader,
        IStoredEmailSummaryReader? summaryReader = null,
        IStoredEmailPreviewReader? previewReader = null,
        SensitiveContentEgressGuard? egressGuard = null,
        IMailboxReadTelemetry? readTelemetry = null,
        AccessAuthorization? authorization = null,
        IReadOnlyList<MailAccountId>? ownedAccounts = null)
    {
        var accountCatalog = Substitute.For<ICallerMailAccountCatalog>();
        accountCatalog.OwnedAccounts.Returns(
        [
            .. (ownedAccounts ?? [Account, SecondAccount])
                .OrderBy(accountId => accountId.Value, StringComparer.Ordinal)
                .Select(accountId => SyntheticServedAccount.Of(accountId)),
        ]);

        return new MailThreadBrowser(
            threadReader,
            summaryReader ?? new InMemoryStoredEmailSummaries(),
            previewReader ?? new InMemoryStoredEmailPreviews(),
            new MailboxScopeResolver(
                accountCatalog,
                StubMailFolderParticipation
                    .Mapping(
                        new MailFolderIdentity(Account, Inbox),
                        new MailFolderIdentity(Account, Sent),
                        new MailFolderIdentity(SecondAccount, Inbox))
                    .Hiding(new MailFolderIdentity(Account, Withheld)),
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            egressGuard ?? SensitiveContentEgressGuards.Inactive(),
            readTelemetry ?? new RecordingMailboxReadTelemetry(),
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));
    }

    private static ThreadedEmailSummary Message(
        int ordinal,
        MailFolderAlias folderAlias,
        string sentAt,
        ThreadedEmailSummary? answers = null,
        string? sender = "somebody@example.test",
        string? displayName = null,
        MailAccountId? accountId = null) => new()
        {
            StoredEmailId = StoredEmailId.Create(new Guid($"00000000-0000-0000-0000-{ordinal:D12}")),
            AccountId = accountId ?? Account,
            FolderAlias = folderAlias,
            ParentStoredEmailId = answers?.StoredEmailId,
            Subject = $"Message {ordinal}",
            SentAt = DateTimeOffset.Parse(sentAt, null),
            SenderAddress = sender,
            SenderDisplayName = displayName,
        };

    private static EmailSummary StoredOf(ThreadedEmailSummary message) => SyntheticEmailSummaries.Create(
        message.SentAt,
        message.StoredEmailId.Value,
        message.AccountId.Value,
        message.FolderAlias.Value,
        message.Subject,
        message.SenderAddress) with
    {
        SenderDisplayName = message.SenderDisplayName,
    };
}
