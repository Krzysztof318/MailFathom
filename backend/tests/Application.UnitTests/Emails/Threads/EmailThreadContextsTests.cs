// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Threads;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Threads;

public sealed class EmailThreadContextsTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("personal");
    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");
    private static readonly MailFolderAlias Withheld = MailFolderAlias.Create("Private");
    private static readonly EmailThreadId Thread = EmailThreadId.Create(new Guid("11111111-1111-1111-1111-111111111111"));

    [Fact]
    public async Task ContextForAsync_ConversationSpanningAVisibleAndAWithheldFolder_NamesAndCountsTheVisibleOnesOnly()
    {
        // Arrange
        var opening = Message(1, Inbox, "2026-08-16T09:00:00Z");
        var visibleReply = Message(2, Inbox, "2026-08-16T10:00:00Z", answers: opening);
        var withheldReply = Message(3, Withheld, "2026-08-16T11:00:00Z", answers: opening);
        var contexts = ContextsOver([opening, visibleReply, withheldReply]);

        // Act
        var thread = await contexts.ContextForAsync(
            Thread,
            opening.StoredEmailId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(thread);
        Assert.Equal(2, thread.EmailCount);
        Assert.Equal(
            [visibleReply.StoredEmailId],
            thread.OtherEmails.Select(message => message.Email.StoredEmailId));
    }

    [Fact]
    public async Task ContextForAsync_MessageWhoseParentIsWithheld_PublishesItAsARootNamingNoAncestor()
    {
        // Arrange
        var withheldOpening = Message(1, Withheld, "2026-08-16T09:00:00Z");
        var reply = Message(2, Inbox, "2026-08-16T10:00:00Z", answers: withheldOpening);
        var contexts = ContextsOver([withheldOpening, reply]);

        // Act
        var thread = await contexts.ContextForAsync(
            Thread,
            reply.StoredEmailId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(thread);
        Assert.Null(thread.AnsweredStoredEmailId);
        Assert.Equal(0, thread.Position);
        Assert.Equal(1, thread.EmailCount);
    }

    [Fact]
    public async Task ContextForAsync_ConversationLongerThanOneReadNames_SaysMoreMessagesAreNotNamed()
    {
        // Arrange
        var messages = Enumerable
            .Range(1, ReadEmailThread.MaximumNamedEmails + 5)
            .Select(ordinal => Message(ordinal, Inbox, $"2026-08-16T{(ordinal % 24):D2}:00:00Z"))
            .ToArray();
        var contexts = ContextsOver(messages);

        // Act
        var thread = await contexts.ContextForAsync(
            Thread,
            messages[0].StoredEmailId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(thread);
        Assert.Equal(messages.Length, thread.EmailCount);
        Assert.Equal(ReadEmailThread.MaximumNamedEmails, thread.OtherEmails.Count);
        Assert.True(thread.MoreEmailsNotNamed);
    }

    [Fact]
    public async Task ContextForAsync_ConversationThatFits_SaysNoMessagesAreLeftUnnamed()
    {
        // Arrange
        var opening = Message(1, Inbox, "2026-08-16T09:00:00Z");
        var reply = Message(2, Inbox, "2026-08-16T10:00:00Z", answers: opening);
        var contexts = ContextsOver([opening, reply]);

        // Act
        var thread = await contexts.ContextForAsync(
            Thread,
            opening.StoredEmailId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(thread);
        Assert.False(thread.MoreEmailsNotNamed);
        Assert.Equal(reply.StoredEmailId, Assert.Single(thread.OtherEmails).Email.StoredEmailId);
    }

    /// <summary>
    /// A conversation ending exactly at the bound was not cut, so the read that assembled all of it says so. The reader
    /// answers with one row past the bound when there is more, which is the only way the two cases tell each other apart.
    /// </summary>
    [Fact]
    public async Task AssembleAsync_ConversationOfExactlyTheAssembledBound_IsNotReportedAsCutShort()
    {
        // Arrange
        var contexts = ContextsOver(Conversation(IEmailThreadReader.MaximumAssembledEmails));

        // Act
        var thread = await contexts.AssembleAsync(Thread, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(thread.WasCutShort);
        Assert.Equal(IEmailThreadReader.MaximumAssembledEmails, thread.Emails.Count);
    }

    [Fact]
    public async Task AssembleAsync_ConversationRunningPastTheAssembledBound_IsCutShortAndDropsTheRowThatSaidSo()
    {
        // Arrange
        var contexts = ContextsOver(Conversation(IEmailThreadReader.MaximumAssembledEmails + 1));

        // Act
        var thread = await contexts.AssembleAsync(Thread, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(thread.WasCutShort);
        Assert.Equal(IEmailThreadReader.MaximumAssembledEmails, thread.Emails.Count);
    }

    /// <summary>
    /// The bound is spent on mail the caller may see. A conversation whose withheld messages fill the bound and whose
    /// readable ones sit behind them is the case that decides it: the caller is entitled to every one of the readable
    /// ones, so a bound applied before the withholding would lose mail nobody could tell was missing.
    /// </summary>
    [Fact]
    public async Task AssembleAsync_WithheldMessagesFillingTheBoundAheadOfReadableOnes_StillAssemblesTheReadableOnes()
    {
        // Arrange
        var withheld = Enumerable
            .Range(1, IEmailThreadReader.MaximumAssembledEmails)
            .Select(ordinal => Message(ordinal, Withheld, "2026-08-16T09:00:00Z"));
        var readable = Enumerable
            .Range(IEmailThreadReader.MaximumAssembledEmails + 1, 3)
            .Select(ordinal => Message(ordinal, Inbox, "2026-08-16T10:00:00Z"));
        var contexts = ContextsOver([.. withheld, .. readable]);

        // Act
        var thread = await contexts.AssembleAsync(Thread, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(thread.WasCutShort);
        Assert.Equal(
            readable.Select(email => email.StoredEmailId),
            thread.Emails.Select(email => email.Email.StoredEmailId));
    }

    [Fact]
    public async Task ContextForAsync_SeveralMessagesOfOneConversation_ReadsThatConversationOnce()
    {
        // Arrange
        var opening = Message(1, Inbox, "2026-08-16T09:00:00Z");
        var reply = Message(2, Inbox, "2026-08-16T10:00:00Z", answers: opening);
        var threadReader = new StubEmailThreadReader((Thread, opening), (Thread, reply));
        var contexts = ContextsOver(threadReader);

        // Act
        await contexts.ContextForAsync(Thread, opening.StoredEmailId, TestContext.Current.CancellationToken);
        await contexts.ContextForAsync(Thread, reply.StoredEmailId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, threadReader.ReadCount);
    }

    [Fact]
    public async Task ContextForAsync_EmailInNoConversation_PublishesNothingAboutOne()
    {
        // Arrange
        var contexts = ContextsOver([Message(1, Inbox, "2026-08-16T09:00:00Z")]);

        // Act
        var thread = await contexts.ContextForAsync(
            threadId: null,
            StoredEmailId.Create(new Guid("00000000-0000-0000-0000-000000000001")),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(thread);
    }

    [Fact]
    public async Task ContextForAsync_ActiveScanner_PublishesTheOtherMessagesWithTheirSubjectsScanned()
    {
        // Arrange
        var opening = Message(1, Inbox, "2026-08-16T09:00:00Z") with { Subject = "token secret-value" };
        var reply = Message(2, Inbox, "2026-08-16T10:00:00Z", answers: opening);
        using var egress = ScanningSensitiveContentEgress.Finding("secret-value", TimeProvider.System);
        var contexts = ContextsOver(
            new StubEmailThreadReader((Thread, opening), (Thread, reply)),
            egress.Guard);

        // Act
        var thread = await contexts.ContextForAsync(
            Thread,
            reply.StoredEmailId,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(thread);
        var named = Assert.Single(thread.OtherEmails);
        Assert.DoesNotContain("secret-value", named.Email.Subject, StringComparison.Ordinal);
    }

    /// <summary>The conversation is the unit its subjects are scanned as, so one call reports one guarded operation.</summary>
    [Fact]
    public async Task ContextForAsync_ActiveScanner_ReportsOneGuardedOperationForTheWholeConversation()
    {
        // Arrange
        var opening = Message(1, Inbox, "2026-08-16T09:00:00Z") with { Subject = "token secret-value" };
        var reply = Message(2, Inbox, "2026-08-16T10:00:00Z", answers: opening);
        var later = Message(3, Inbox, "2026-08-16T11:00:00Z", answers: opening);
        using var egress = ScanningSensitiveContentEgress.Finding("secret-value", TimeProvider.System);
        var contexts = ContextsOver(
            new StubEmailThreadReader((Thread, opening), (Thread, reply), (Thread, later)),
            egress.Guard);

        // Act
        await contexts.ContextForAsync(Thread, reply.StoredEmailId, TestContext.Current.CancellationToken);

        // Assert
        var operation = Assert.Single(egress.Telemetry.Operations);

        Assert.Equal(SensitiveContentEgressPoint.McpEmailContent, operation.EgressPoint);
        Assert.Equal(egress.Telemetry.Guarded.Count, operation.GuardedTextCount);
        Assert.True(operation.GuardedTextCount > 1);
        Assert.True(operation.WasClosed);
    }

    /// <summary>
    /// A merge folds one conversation into another and keeps the folded row, so an identifier a tool published before it
    /// still names a conversation. Assembling by that identifier reaches the mail of the conversation that survived,
    /// which is the whole reason the folded row is kept rather than deleted.
    /// </summary>
    [Fact]
    public async Task AssembleAsync_AnIdentifierPublishedBeforeAMerge_AssemblesTheConversationThatSurvivedIt()
    {
        // Arrange
        var folded = EmailThreadId.Create(new Guid("22222222-2222-2222-2222-222222222222"));
        var opening = Message(1, Inbox, "2026-08-16T09:00:00Z");
        var reply = Message(2, Inbox, "2026-08-16T10:00:00Z", answers: opening);
        var contexts = ContextsOver(
            new StubEmailThreadReader((Thread, opening), (Thread, reply)).MergedInto(folded, Thread));

        // Act
        var thread = await contexts.AssembleAsync(folded, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [opening.StoredEmailId, reply.StoredEmailId],
            thread.Emails.Select(email => email.Email.StoredEmailId));
    }

    private static ThreadedEmailSummary[] Conversation(int length) =>
    [
        .. Enumerable
            .Range(1, length)
            .Select(ordinal => Message(ordinal, Inbox, $"2026-08-16T{ordinal % 24:D2}:00:00Z")),
    ];

    private static EmailThreadContexts ContextsOver(IReadOnlyList<ThreadedEmailSummary> messages) =>
        ContextsOver(new StubEmailThreadReader([.. messages.Select(message => (Thread, message))]));

    private static EmailThreadContexts ContextsOver(
        StubEmailThreadReader threadReader,
        SensitiveContentEgressGuard? egressGuard = null)
    {
        var accountCatalog = Substitute.For<ICallerMailAccountCatalog>();
        accountCatalog.OwnedAccounts.Returns([SyntheticServedAccount.Of(Account)]);

        return new EmailThreadContexts(
            threadReader,
            new MailboxScopeResolver(
                accountCatalog,
                StubMailFolderParticipation
                    .Mapping(new MailFolderIdentity(Account, Inbox))
                    .Hiding(new MailFolderIdentity(Account, Withheld)),
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            egressGuard ?? SensitiveContentEgressGuards.Inactive());
    }

    private static ThreadedEmailSummary Message(
        int ordinal,
        MailFolderAlias folderAlias,
        string sentAt,
        ThreadedEmailSummary? answers = null)
    {
        return new ThreadedEmailSummary
        {
            StoredEmailId = StoredEmailId.Create(new Guid($"00000000-0000-0000-0000-{ordinal:D12}")),
            AccountId = Account,
            FolderAlias = folderAlias,
            ParentStoredEmailId = answers?.StoredEmailId,
            Subject = $"Message {ordinal}",
            SentAt = DateTimeOffset.Parse(sentAt, null),
            SenderAddress = $"sender{ordinal}@example.test",
        };
    }
}
