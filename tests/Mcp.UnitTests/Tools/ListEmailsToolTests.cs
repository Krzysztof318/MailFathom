// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.ListEmails;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Folders;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Mcp.Tools;
using MailFathom.Mcp.Tools.Results;
using MailFathom.Mcp.Tools.Summaries;
using MailFathom.Mcp.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Covers what the <c>list_emails</c> tool itself owns: converting arguments and publishing a page.</summary>
/// <remarks>
/// <para>
/// The tool calls the real <see cref="MailboxTimelineReader" /> rather than a substitute for it, because the use case is
/// where every bound and every authorization decision lives and a substitute would only prove that the tool composes with
/// a fiction. What the stubs replace is storage, the boundary below the use case.
/// </para>
/// <para>
/// Two properties are asserted throughout rather than in one test of their own: a refused call never reaches storage, and
/// no failure message carries the value that was refused. Both hold for every path through the boundary, so proving them
/// once would prove them for one path.
/// </para>
/// </remarks>
public sealed class ListEmailsToolTests
{
    private const string ServedAccountId = "personal";

    [Fact]
    public async Task ListEmailsAsync_NoArgument_ReadsEveryServedAccountNewestFirstWithTheDefaultPageSize()
    {
        // Arrange
        var timeline = new StubStoredEmailTimelineReader();
        var tool = ToolOver(timeline);

        // Act
        await tool.ListEmailsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(timeline.LastFilter);
        var filter = timeline.LastFilter;
        Assert.Equal([MailAccountId.Create(ServedAccountId)], filter.Selection.Scope.AccountIds);
        Assert.Empty(filter.Selection.Scope.SelectedFolders);
        Assert.Equal(EmailTimelineDirection.NewestFirst, filter.Direction);
        Assert.Null(timeline.LastContinueAfter);

        // One row beyond the page is what establishes whether another page exists.
        Assert.Equal(MailboxQueryPageSize.DefaultValue + 1, timeline.LastLimit);
    }

    [Fact]
    public async Task ListEmailsAsync_EveryFilterNamed_PassesEachOneToTheUseCase()
    {
        // Arrange
        var timeline = new StubStoredEmailTimelineReader();
        var tool = ToolOver(timeline);
        var rangeStart = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var rangeEnd = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

        // Act
        await tool.ListEmailsAsync(
            accounts: [ServedAccountId],
            folders: ["archive"],
            senderAddress: "sender@example.test",
            recipientAddress: "recipient@example.test",
            subjectFragment: "invoice",
            receivedOnOrAfter: rangeStart,
            receivedBefore: rangeEnd,
            isRemotelySeen: false,
            hasAttachments: true,
            direction: ListEmailsDirection.OldestFirst,
            pageSize: 10,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(timeline.LastFilter);
        var filter = timeline.LastFilter;
        Assert.Equal([MailAccountId.Create(ServedAccountId)], filter.Selection.Scope.AccountIds);
        Assert.Equal(
            [new MailFolderIdentity(MailAccountId.Create(ServedAccountId), MailFolderAlias.Create("ARCHIVE"))],
            filter.Selection.Scope.SelectedFolders);
        Assert.Equal("invoice", filter.Selection.SubjectFragment);
        Assert.Equal(rangeStart, filter.Selection.ReceivedOnOrAfter);
        Assert.Equal(rangeEnd, filter.Selection.ReceivedBefore);
        Assert.False(filter.Selection.IsRemotelySeen);
        Assert.True(filter.Selection.HasAttachments);
        Assert.Equal(EmailTimelineDirection.OldestFirst, filter.Direction);
        Assert.Equal(11, timeline.LastLimit);
    }

    /// <summary>An alias is MailFathom's own name for a folder, so a caller's spelling is normalized rather than matched literally.</summary>
    [Fact]
    public async Task ListEmailsAsync_OneFolderSpelledSeveralWays_NamesThatFolderOnce()
    {
        // Arrange
        var timeline = new StubStoredEmailTimelineReader();
        var tool = ToolOver(timeline);

        // Act
        await tool.ListEmailsAsync(
            folders: ["inbox", "INBOX", " Inbox "],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(timeline.LastFilter);
        Assert.Equal(
            [new MailFolderIdentity(MailAccountId.Create(ServedAccountId), MailFolderAlias.Create("INBOX"))],
            timeline.LastFilter.Selection.Scope.SelectedFolders);
    }

    /// <summary>A caller that knows the role but not the deployment's own name for the folder names the role instead.</summary>
    [Fact]
    public async Task ListEmailsAsync_AFolderNamedByItsRole_ReadsTheFolderPlayingIt()
    {
        // Arrange
        var timeline = new StubStoredEmailTimelineReader();
        var tool = ToolMapping(
            StubMailFolderMappings.Nothing.With(
                MailAccountId.Create(ServedAccountId),
                MailFolderMapping.ToRemotePath(
                    MailFolderAlias.Create("spam"),
                    RemoteFolderPath.Create("INBOX.Spam"),
                    specialUse: MailFolderSpecialUse.Junk)),
            timeline);

        // Act
        await tool.ListEmailsAsync(folders: ["role:Junk"], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(timeline.LastFilter);
        Assert.Equal(
            [new MailFolderIdentity(MailAccountId.Create(ServedAccountId), MailFolderAlias.Create("SPAM"))],
            timeline.LastFilter.Selection.Scope.SelectedFolders);
    }

    /// <summary>A role nothing carries is refused once, in the place every caller names a folder through, rather than answered with an empty page.</summary>
    [Fact]
    public async Task ListEmailsAsync_ARoleNoFolderPlays_IsRefusedNamingTheRole()
    {
        // Arrange
        var tool = ToolOver(new StubStoredEmailTimelineReader());

        // Act
        var failure = await Assert.ThrowsAsync<MailFolderRoleUnmappedException>(
            () => tool.ListEmailsAsync(folders: ["role:Junk"], cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFolderSpecialUse.Junk, failure.Role);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ListEmailsAsync_BlankAccountIdentifier_IsRefusedWithoutReading(string blank)
    {
        // Arrange
        var timeline = new StubStoredEmailTimelineReader();
        var tool = ToolOver(timeline);

        // Act
        var failure = await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(
            () => tool.ListEmailsAsync(accounts: [blank], cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailboxQueryFilterInvalid, failure.ErrorCode);
        Assert.Equal("accounts", failure.FilterName);
        Assert.NotNull(failure.InnerException);
        Assert.Equal(0, timeline.ReadCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("in\u0001box")]
    public async Task ListEmailsAsync_UnusableFolderAlias_IsRefusedWithoutReading(string unusable)
    {
        // Arrange
        var timeline = new StubStoredEmailTimelineReader();
        var tool = ToolOver(timeline);

        // Act
        var failure = await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(
            () => tool.ListEmailsAsync(folders: [unusable], cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailboxQueryFilterInvalid, failure.ErrorCode);
        Assert.Equal("folder aliases", failure.FilterName);
        Assert.Equal(0, timeline.ReadCount);
    }

    /// <summary>A ceiling that only applies after every element has been converted is not a ceiling.</summary>
    [Fact]
    public async Task ListEmailsAsync_MoreAccountsThanTheQueryAccepts_IsRefusedWithoutConvertingThem()
    {
        // Arrange
        var timeline = new StubStoredEmailTimelineReader();
        var tool = ToolOver(timeline);
        var namedAccounts = Enumerable
            .Range(0, MailboxScope.MaximumAccountIds + 1)
            .Select(index => $"account-{index}")
            .ToArray();

        // Act
        var failure = await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(
            () => tool.ListEmailsAsync(accounts: namedAccounts, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("accounts", failure.FilterName);
        Assert.Equal(0, timeline.ReadCount);
    }

    [Fact]
    public async Task ListEmailsAsync_MoreFolderAliasesThanTheQueryAccepts_IsRefusedWithoutConvertingThem()
    {
        // Arrange
        var timeline = new StubStoredEmailTimelineReader();
        var tool = ToolOver(timeline);
        var namedFolders = Enumerable
            .Range(0, MailboxScope.MaximumFolderAliases + 1)
            .Select(index => $"folder-{index}")
            .ToArray();

        // Act
        var failure = await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(
            () => tool.ListEmailsAsync(folders: namedFolders, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("folder aliases", failure.FilterName);
        Assert.Equal(0, timeline.ReadCount);
    }

    /// <summary>An unserved account identifier is named back in the refusal a client reads, so it cannot be arbitrary text.</summary>
    [Theory]
    [InlineData("victim@example.test\nINJECTED admin login")]
    [InlineData("account\u0000")]
    public async Task ListEmailsAsync_AccountIdentifierCarryingAControlCharacter_IsRefusedWithoutReading(string unusable)
    {
        // Arrange
        var timeline = new StubStoredEmailTimelineReader();
        var tool = ToolOver(timeline);

        // Act
        var failure = await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(
            () => tool.ListEmailsAsync(accounts: [unusable], cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailboxQueryFilterInvalid, failure.ErrorCode);
        Assert.DoesNotContain("victim@example.test", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, timeline.ReadCount);
    }

    [Fact]
    public async Task ListEmailsAsync_IdentifierLongerThanTheBoundaryAccepts_IsRefusedWithoutReading()
    {
        // Arrange
        var timeline = new StubStoredEmailTimelineReader();
        var tool = ToolOver(timeline);

        // Act
        var failure = await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(
            () => tool.ListEmailsAsync(
                accounts: [new string('a', 257)],
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("accounts", failure.FilterName);
        Assert.Equal(0, timeline.ReadCount);
    }

    /// <summary>A refused identifier is caller input, and a boundary that echoes input back has started returning content.</summary>
    [Fact]
    public async Task ListEmailsAsync_UnusableFolderAlias_NamesNoRefusedValue()
    {
        // Arrange
        const string PersonalData = "victim@example.test";
        var tool = ToolOver(new StubStoredEmailTimelineReader());

        // Act
        var failure = await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(
            () => tool.ListEmailsAsync(
                folders: [$"{PersonalData}\u0001"],
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.DoesNotContain(PersonalData, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The use case decides authorization, so the tool lets its refusal travel rather than answering an empty page.</summary>
    [Fact]
    public async Task ListEmailsAsync_AccountThisDeploymentDoesNotServe_RaisesTheUseCaseRefusalWithoutReading()
    {
        // Arrange
        var timeline = new StubStoredEmailTimelineReader();
        var tool = ToolOver(timeline);

        // Act
        var failure = await Assert.ThrowsAsync<MailAccountNotAccessibleException>(
            () => tool.ListEmailsAsync(
                accounts: [ServedAccountId, "someone-elses"],
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailAccountNotAccessible, failure.ErrorCode);
        Assert.Equal(0, timeline.ReadCount);
    }

    /// <summary>A page size the query does not serve is the use case's refusal too, and the tool neither clamps nor re-codes it.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(MailboxQueryPageSize.MaximumValue + 1)]
    public async Task ListEmailsAsync_PageSizeOutsideTheServedRange_RaisesTheUseCaseRefusalWithoutReading(int pageSize)
    {
        // Arrange
        var timeline = new StubStoredEmailTimelineReader();
        var tool = ToolOver(timeline);

        // Act
        var failure = await Assert.ThrowsAsync<MailboxQueryPageSizeOutOfRangeException>(
            () => tool.ListEmailsAsync(pageSize: pageSize, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailboxQueryPageSizeOutOfRange, failure.ErrorCode);
        Assert.Equal(0, timeline.ReadCount);
    }

    [Fact]
    public async Task ListEmailsAsync_CursorThisSystemDidNotIssue_RaisesTheUseCaseRefusal()
    {
        // Arrange
        var tool = ToolOver(new StubStoredEmailTimelineReader());

        // Act
        var failure = await Assert.ThrowsAsync<MailboxQueryCursorMalformedException>(
            () => tool.ListEmailsAsync(cursor: "not-a-cursor", cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailboxQueryCursorMalformed, failure.ErrorCode);
    }

    /// <summary>A blank cursor is the first page, so a client that carries the field with nothing in it is not refused.</summary>
    [Fact]
    public async Task ListEmailsAsync_BlankCursor_ReadsTheFirstPage()
    {
        // Arrange
        var timeline = new StubStoredEmailTimelineReader();
        var tool = ToolOver(timeline);

        // Act
        await tool.ListEmailsAsync(cursor: "   ", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(timeline.LastContinueAfter);
        Assert.Equal(1, timeline.ReadCount);
    }

    [Fact]
    public async Task ListEmailsAsync_MatchingEmail_PublishesEveryFieldOfTheSummary()
    {
        // Arrange
        var storedEmailId = Guid.CreateVersion7();
        var sentAt = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var receivedAt = new DateTimeOffset(2026, 3, 1, 8, 0, 5, TimeSpan.Zero);
        var observedAt = new DateTimeOffset(2026, 3, 2, 6, 0, 0, TimeSpan.Zero);
        var summary = new EmailSummary
        {
            StoredEmailId = StoredEmailId.Create(storedEmailId),
            AccountId = MailAccountId.Create(ServedAccountId),
            FolderAlias = MailFolderAlias.Create("INBOX"),
            InternetMessageId = "<abc@example.test>",
            Subject = "Quarterly invoice",
            SentAt = sentAt,
            ReceivedAt = receivedAt,
            SizeOctets = 4096,
            SenderDisplayName = "Accounts Payable",
            SenderAddress = "billing@example.test",
            ToAddresses = ["finance@example.test", "cfo@example.test"],
            Attachments = new StoredEmailAttachmentSummary(
                AttachmentCount: 2,
                TotalSizeOctets: 3072,
                InlineResourceCount: 1,
                IsEncrypted: false,
                CarriesUnverifiedSignature: true,
                ContainsUnexpandedTnefPart: false),
            ContentAvailability = StoredEmailContentAvailability.ExceededSizeLimit,
            RemoteFlags = new RemoteEmailFlagSnapshot(
                observedAt,
                IsSeen: true,
                IsAnswered: true,
                IsFlagged: false,
                IsDraft: false,
                IsDeleted: false),
        };
        var tool = ToolOver(new StubStoredEmailTimelineReader(summary));

        // Act
        var result = await tool.ListEmailsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var published = Assert.Single(result.Emails);
        Assert.Equal(storedEmailId.ToString(), published.StoredEmailId);
        Assert.Equal(ServedAccountId, published.AccountId);
        Assert.Equal(SyntheticServedAccount.DisplayNameOf(MailAccountId.Create(ServedAccountId)).Value, published.AccountDisplayName);
        Assert.Equal("INBOX", published.FolderAlias);
        Assert.Equal("<abc@example.test>", published.InternetMessageId);
        Assert.Equal("Quarterly invoice", published.Subject);
        Assert.Equal(sentAt, published.SentAt);
        Assert.Equal(receivedAt, published.ReceivedAt);
        Assert.Equal(4096, published.SizeBytes);
        Assert.Equal("Accounts Payable", published.SenderDisplayName);
        Assert.Equal("billing@example.test", published.SenderAddress);
        Assert.Equal(["finance@example.test", "cfo@example.test"], published.ToAddresses);
        Assert.Equal(2, published.Attachments.AttachmentCount);
        Assert.Equal(3072, published.Attachments.TotalSizeBytes);
        Assert.Equal(1, published.Attachments.InlineResourceCount);
        Assert.False(published.Attachments.IsEncrypted);
        Assert.True(published.Attachments.CarriesUnverifiedSignature);
        Assert.False(published.Attachments.ContainsUnexpandedTnefPart);
        Assert.Equal(ListedEmailContentAvailability.ExceededSizeLimit, published.ContentAvailability);
        Assert.True(published.RemoteFlags.Seen);
        Assert.True(published.RemoteFlags.Answered);
        Assert.False(published.RemoteFlags.Flagged);
        Assert.Equal(observedAt, published.RemoteFlags.ObservedAt);
        Assert.True(published.RemoteFlags.WasObserved);
    }

    /// <summary>An email waiting for storage room is published as waiting, rather than ending the listing.</summary>
    /// <remarks>
    /// The mapping refuses a stored state it has no wire value for, which is the right refusal for a value nobody
    /// decided how to publish and the wrong thing to happen to a whole page of mail. This is what proves the decision
    /// was made.
    /// </remarks>
    [Fact]
    public async Task ListEmailsAsync_EmailAwaitingStorageHeadroom_PublishesThatItsContentIsNotStoredYet()
    {
        // Arrange
        var summary = SummaryReceivedAt(null) with
        {
            ContentAvailability = StoredEmailContentAvailability.AwaitingStorageHeadroom,
        };
        var tool = ToolOver(new StubStoredEmailTimelineReader(summary));

        // Act
        var result = await tool.ListEmailsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var published = Assert.Single(result.Emails);
        Assert.Equal(ListedEmailContentAvailability.AwaitingStorageHeadroom, published.ContentAvailability);
    }

    /// <summary>Flags nobody has read are published as such, so a caller cannot read them as a server reporting no flag set.</summary>
    [Fact]
    public async Task ListEmailsAsync_EmailWhoseFlagsWereNeverObserved_PublishesThatNobodyHasLooked()
    {
        // Arrange
        var tool = ToolOver(new StubStoredEmailTimelineReader(SummaryReceivedAt(null)));

        // Act
        var result = await tool.ListEmailsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var published = Assert.Single(result.Emails);
        Assert.False(published.RemoteFlags.WasObserved);
        Assert.Null(published.RemoteFlags.ObservedAt);
        Assert.False(published.RemoteFlags.Seen);
    }

    /// <summary>A cursor is issued only when a further row exists, so a caller that stops at its absence has seen every email.</summary>
    [Fact]
    public async Task ListEmailsAsync_MoreEmailsThanThePage_PublishesTheCursorThatContinuesTheWalk()
    {
        // Arrange
        var firstReceivedAt = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var timeline = new StubStoredEmailTimelineReader(
            [.. Enumerable.Range(0, 3).Select(hourOffset => SummaryReceivedAt(firstReceivedAt.AddHours(hourOffset)))]);
        var tool = ToolOver(timeline);

        // Act
        var result = await tool.ListEmailsAsync(pageSize: 2, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Emails.Count);
        Assert.NotNull(result.NextCursor);
    }

    [Fact]
    public async Task ListEmailsAsync_LastPage_PublishesNoCursor()
    {
        // Arrange
        var tool = ToolOver(new StubStoredEmailTimelineReader(SummaryReceivedAt(null)));

        // Act
        var result = await tool.ListEmailsAsync(pageSize: 2, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result.Emails);
        Assert.Null(result.NextCursor);
    }

    /// <summary>Freshness travels with every page, because a listing is served from local state whether or not a server is reachable.</summary>
    [Fact]
    public async Task ListEmailsAsync_AnyPage_PublishesTheFreshnessOfEveryCoveredFolder()
    {
        // Arrange
        var synchronizedAt = new DateTimeOffset(2026, 3, 2, 6, 0, 0, TimeSpan.Zero);
        var tool = ToolOver(
            new StubStoredEmailTimelineReader(),
            new StubSynchronizationFreshnessReader(
                new MailboxFolderFreshness(
                    MailAccountId.Create(ServedAccountId),
                    MailFolderAlias.Create("INBOX"),
                    synchronizedAt),
                new MailboxFolderFreshness(
                    MailAccountId.Create(ServedAccountId),
                    MailFolderAlias.Create("ARCHIVE"),
                    SynchronizedAt: null)));

        // Act
        var result = await tool.ListEmailsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [("INBOX", true), ("ARCHIVE", false)],
            [.. result.FolderFreshness.Select(entry => (entry.FolderAlias, entry.WasSynchronized))]);
        Assert.Equal(synchronizedAt, result.FolderFreshness[0].SynchronizedAt);
        Assert.Null(result.FolderFreshness[1].SynchronizedAt);
        Assert.All(
            result.FolderFreshness,
            entry => Assert.Equal(
                SyntheticServedAccount.DisplayNameOf(MailAccountId.Create(ServedAccountId)).Value,
                entry.AccountDisplayName));
    }

    /// <summary>The display name is what a person recognizes a mailbox by, so naming an account with it reads the same mailbox the identifier does.</summary>
    [Fact]
    public async Task ListEmailsAsync_AnAccountNamedByItsDisplayName_ReadsTheSameAccountTheIdentifierNames()
    {
        // Arrange
        var timeline = new StubStoredEmailTimelineReader();
        var tool = ToolOver(timeline);

        // Act
        await tool.ListEmailsAsync(
            accounts: [SyntheticServedAccount.DisplayNameOf(MailAccountId.Create(ServedAccountId)).Value.ToUpperInvariant()],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(timeline.LastFilter);
        Assert.Equal([MailAccountId.Create(ServedAccountId)], timeline.LastFilter.Selection.Scope.AccountIds);
    }

    [Fact]
    public async Task ListEmailsAsync_CancelledCaller_StopsRatherThanAnsweringFromWhatItHad()
    {
        // Arrange
        var tool = ToolOver(new StubStoredEmailTimelineReader(SummaryReceivedAt(null)));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act, Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tool.ListEmailsAsync(cancellationToken: cancellation.Token));
    }

    /// <summary>Junk is mail a filter already set aside, so a listing that says nothing about it leaves it out.</summary>
    [Fact]
    public async Task ListEmailsAsync_NoAnswerAboutJunk_LeavesTheJunkFolderOutAndSaysSo()
    {
        // Arrange
        var timeline = new StubStoredEmailTimelineReader();
        var junkFolder = new MailFolderIdentity(MailAccountId.Create(ServedAccountId), MailFolderAlias.Create("JUNK"));
        var tool = ToolOver(timeline, junkFolders: StubJunkMailFolderCatalog.Naming(junkFolder));

        // Act
        var result = await tool.ListEmailsAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(timeline.LastFilter);
        Assert.Equal([junkFolder], timeline.LastFilter.Selection.Scope.WithheldFolders);
        Assert.False(result.IncludedJunkMail);
    }

    /// <summary>Somebody looking for a message a filter took asks for it, and the answer says which listing they got.</summary>
    [Fact]
    public async Task ListEmailsAsync_JunkAskedFor_ListsItAndSaysSo()
    {
        // Arrange
        var timeline = new StubStoredEmailTimelineReader();
        var junkFolder = new MailFolderIdentity(MailAccountId.Create(ServedAccountId), MailFolderAlias.Create("JUNK"));
        var tool = ToolOver(timeline, junkFolders: StubJunkMailFolderCatalog.Naming(junkFolder));

        // Act
        var result = await tool.ListEmailsAsync(
            includeJunkMail: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(timeline.LastFilter);
        Assert.Empty(timeline.LastFilter.Selection.Scope.WithheldFolders);
        Assert.True(result.IncludedJunkMail);
    }

    private static EmailSummary SummaryReceivedAt(DateTimeOffset? receivedAt) => new()
    {
        StoredEmailId = StoredEmailId.Create(Guid.CreateVersion7()),
        AccountId = MailAccountId.Create(ServedAccountId),
        FolderAlias = MailFolderAlias.Create("INBOX"),
        ReceivedAt = receivedAt,
        SentAt = receivedAt,
        SizeOctets = 1024,
        ToAddresses = [],
        Attachments = StoredEmailAttachmentSummary.None,
        ContentAvailability = StoredEmailContentAvailability.Available,
        RemoteFlags = RemoteEmailFlagSnapshot.NeverObserved,
    };

    private static ListEmailsTool ToolOver(
        StubStoredEmailTimelineReader timeline,
        StubSynchronizationFreshnessReader? freshness = null,
        StubJunkMailFolderCatalog? junkFolders = null) =>
        ToolMapping(StubMailFolderMappings.Nothing, timeline, freshness, junkFolders);

    private static ListEmailsTool ToolMapping(
        StubMailFolderMappings folderMappings,
        StubStoredEmailTimelineReader timeline,
        StubSynchronizationFreshnessReader? freshness = null,
        StubJunkMailFolderCatalog? junkFolders = null) => new(
        new MailboxTimelineReader(
            timeline,
            freshness ?? new StubSynchronizationFreshnessReader(),
            new MailboxScopeResolver(
                new StubMailAccountCatalog(ServedAccountId),
                StubMailFolderParticipation.Everything,
                junkFolders ?? StubJunkMailFolderCatalog.None,
                folderMappings.Resolver)),
        new StubMailAccountCatalog(ServedAccountId));
}
