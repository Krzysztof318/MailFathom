// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Text;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.ListEmails;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.ListEmails;

/// <summary>Covers the mailbox listing use case: its filters, its bounds, and the keyset walk it issues cursors for.</summary>
public sealed class MailboxTimelineReaderTests
{
    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>What the default catalog serves, so an unscoped request reaches every email a test arranged.</summary>
    private static readonly MailAccountId[] EveryAccountTheSyntheticTimelineUses =
    [
        MailAccountId.Create(SyntheticEmailSummaries.DefaultAccountId),
        MailAccountId.Create("secondary"),
    ];

    private static readonly MailboxFolderFreshness InboxFreshness = new(
        MailAccountId.Create(SyntheticEmailSummaries.DefaultAccountId),
        MailFolderAlias.Create(SyntheticEmailSummaries.DefaultFolderAlias),
        new DateTimeOffset(2026, 7, 30, 6, 0, 0, TimeSpan.Zero));

    public static TheoryData<string> MalformedCursors =>
    [
        "not a cursor at all",
        Encoded("1.0"),
        Encoded("2.0.0102030405060708090a0b0c0d0e0f10.abc"),
        Encoded("1.notanumber.0102030405060708090a0b0c0d0e0f10.abc"),
        Encoded("1.-5.0102030405060708090a0b0c0d0e0f10.abc"),
        Encoded("1.0.not-a-guid.abc"),
        Encoded("1.0.00000000000000000000000000000000.abc"),
        Encoded("1.0.0102030405060708090a0b0c0d0e0f10."),
    ];

    public static TheoryData<EmailTimelineDirection> BothDirections =>
    [
        EmailTimelineDirection.NewestFirst,
        EmailTimelineDirection.OldestFirst,
    ];

    [Fact]
    public async Task ListEmailsAsync_NoFilters_ReturnsEveryEmailNewestFirst()
    {
        // Arrange
        var emails = SyntheticEmailSummaries.CreateDailyRun(4, FirstJuly);
        var reader = ReaderOver(new InMemoryStoredEmailTimeline().WithAll(emails));

        // Act
        var result = await reader.ListEmailsAsync(new ListEmailsRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            emails.Reverse().Select(email => email.StoredEmailId),
            result.Emails.Select(email => email.StoredEmailId));
    }

    [Fact]
    public async Task ListEmailsAsync_ReadingOldestFirst_ReturnsEveryEmailInTheReverseOrder()
    {
        // Arrange
        var emails = SyntheticEmailSummaries.CreateDailyRun(4, FirstJuly);
        var reader = ReaderOver(new InMemoryStoredEmailTimeline().WithAll(emails));

        // Act
        var result = await reader.ListEmailsAsync(
            new ListEmailsRequest { Direction = EmailTimelineDirection.OldestFirst },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            emails.Select(email => email.StoredEmailId),
            result.Emails.Select(email => email.StoredEmailId));
    }

    /// <summary>Undated mail sits at the far end of whichever direction is read, never above the newest mail.</summary>
    [Theory]
    [MemberData(nameof(BothDirections))]
    public async Task ListEmailsAsync_EmailWithNoReceivedTimestamp_SortsAtTheUndatedEndOfTheDirection(
        EmailTimelineDirection direction)
    {
        // Arrange
        var undated = SyntheticEmailSummaries.Create(receivedAt: null);
        var dated = SyntheticEmailSummaries.CreateDailyRun(2, FirstJuly);
        var reader = ReaderOver(new InMemoryStoredEmailTimeline().WithAll([undated, .. dated]));

        // Act
        var result = await reader.ListEmailsAsync(
            new ListEmailsRequest { Direction = direction },
            TestContext.Current.CancellationToken);

        // Assert
        var undatedPosition = direction is EmailTimelineDirection.NewestFirst ? result.Emails.Count - 1 : 0;
        Assert.Equal(undated.StoredEmailId, result.Emails[undatedPosition].StoredEmailId);
    }

    [Fact]
    public async Task ListEmailsAsync_NoPageSizeNamed_ReturnsTheDefaultNumberOfEmails()
    {
        // Arrange
        var emails = SyntheticEmailSummaries.CreateDailyRun(MailboxQueryPageSize.DefaultValue + 5, FirstJuly);
        var reader = ReaderOver(new InMemoryStoredEmailTimeline().WithAll(emails));

        // Act
        var result = await reader.ListEmailsAsync(new ListEmailsRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailboxQueryPageSize.DefaultValue, result.Emails.Count);
    }

    /// <summary>The page is bounded by the effective size, and the row beyond it is what establishes the next cursor.</summary>
    [Fact]
    public async Task ListEmailsAsync_MoreEmailsThanThePage_BoundsThePageAndAsksForOneRowBeyondIt()
    {
        // Arrange
        var timeline = new InMemoryStoredEmailTimeline().WithAll(SyntheticEmailSummaries.CreateDailyRun(10, FirstJuly));
        var reader = ReaderOver(timeline);

        // Act
        var result = await reader.ListEmailsAsync(
            new ListEmailsRequest { PageSize = 3 },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, result.Emails.Count);
        Assert.Equal(4, Assert.Single(timeline.Calls).Limit);
        Assert.True(result.HasMore);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(MailboxQueryPageSize.MaximumValue + 1)]
    public async Task ListEmailsAsync_PageSizeOutsideTheAcceptedRange_IsRejectedRatherThanClamped(int pageSize)
    {
        // Arrange
        var timeline = new InMemoryStoredEmailTimeline().WithAll(SyntheticEmailSummaries.CreateDailyRun(3, FirstJuly));
        var reader = ReaderOver(timeline);

        // Act
        var failure = await Assert.ThrowsAsync<MailboxQueryPageSizeOutOfRangeException>(() =>
            reader.ListEmailsAsync(new ListEmailsRequest { PageSize = pageSize }, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(pageSize, failure.RequestedPageSize);
        Assert.Equal(MailboxQueryPageSize.MaximumValue, failure.MaximumPageSize);
        Assert.Empty(timeline.Calls);
    }

    [Fact]
    public async Task ListEmailsAsync_PageEndingTheResultSet_ReturnsNoCursor()
    {
        // Arrange
        var reader = ReaderOver(
            new InMemoryStoredEmailTimeline().WithAll(SyntheticEmailSummaries.CreateDailyRun(3, FirstJuly)));

        // Act
        var result = await reader.ListEmailsAsync(
            new ListEmailsRequest { PageSize = 3 },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.NextCursor);
        Assert.False(result.HasMore);
    }

    /// <summary>
    /// The acceptance criterion of the specification: a walk with stable filters visits every row exactly once, across
    /// equal received timestamps, across undated mail, and in both directions.
    /// </summary>
    [Theory]
    [MemberData(nameof(BothDirections))]
    public async Task ListEmailsAsync_PagingWithStableFilters_VisitsEveryEmailExactlyOnce(
        EmailTimelineDirection direction)
    {
        // Arrange
        var sharedTimestamp = FirstJuly.AddDays(10);
        IReadOnlyList<EmailSummary> emails =
        [
            .. SyntheticEmailSummaries.CreateDailyRun(3, FirstJuly),
            SyntheticEmailSummaries.Create(sharedTimestamp),
            SyntheticEmailSummaries.Create(sharedTimestamp),
            SyntheticEmailSummaries.Create(sharedTimestamp),
            SyntheticEmailSummaries.Create(receivedAt: null),
            SyntheticEmailSummaries.Create(receivedAt: null),
        ];
        var reader = ReaderOver(new InMemoryStoredEmailTimeline().WithAll(emails));

        // Act
        var visited = await WalkEveryPageAsync(reader, new ListEmailsRequest { PageSize = 2, Direction = direction });

        // Assert
        Assert.Equal(emails.Count, visited.Count);
        Assert.Equal(
            emails.Select(email => email.StoredEmailId).OrderBy(id => id.Value).ToArray(),
            visited.OrderBy(id => id.Value).ToArray());
    }

    /// <summary>A cursor names a boundary in one filtered set, so honoring it against another would return an arbitrary window.</summary>
    [Fact]
    public async Task ListEmailsAsync_CursorPresentedAgainstDifferentFilters_IsRejected()
    {
        // Arrange
        var timeline = new InMemoryStoredEmailTimeline().WithAll(SyntheticEmailSummaries.CreateDailyRun(5, FirstJuly));
        var reader = ReaderOver(timeline);
        var firstPage = await reader.ListEmailsAsync(
            new ListEmailsRequest { PageSize = 2 },
            TestContext.Current.CancellationToken);

        // Act, Assert
        await Assert.ThrowsAsync<MailboxQueryCursorFilterMismatchException>(() => reader.ListEmailsAsync(
            new ListEmailsRequest { PageSize = 2, Cursor = firstPage.NextCursor, SubjectFragment = "invoice" },
            TestContext.Current.CancellationToken));
    }

    /// <summary>Changing the page size moves no boundary, so the same walk continues under a different page size.</summary>
    [Fact]
    public async Task ListEmailsAsync_CursorPresentedWithADifferentPageSize_ContinuesTheSameWalk()
    {
        // Arrange
        var emails = SyntheticEmailSummaries.CreateDailyRun(5, FirstJuly);
        var reader = ReaderOver(new InMemoryStoredEmailTimeline().WithAll(emails));
        var firstPage = await reader.ListEmailsAsync(
            new ListEmailsRequest { PageSize = 2 },
            TestContext.Current.CancellationToken);

        // Act
        var secondPage = await reader.ListEmailsAsync(
            new ListEmailsRequest { PageSize = 3, Cursor = firstPage.NextCursor },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            emails.Reverse().Skip(2).Select(email => email.StoredEmailId),
            secondPage.Emails.Select(email => email.StoredEmailId));
    }

    [Theory]
    [MemberData(nameof(MalformedCursors))]
    public async Task ListEmailsAsync_CursorThisSystemDidNotIssue_IsRejected(string cursor)
    {
        // Arrange
        var timeline = new InMemoryStoredEmailTimeline().WithAll(SyntheticEmailSummaries.CreateDailyRun(3, FirstJuly));
        var reader = ReaderOver(timeline);

        // Act
        await Assert.ThrowsAsync<MailboxQueryCursorMalformedException>(() => reader.ListEmailsAsync(
            new ListEmailsRequest { Cursor = cursor },
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Empty(timeline.Calls);
    }

    /// <summary>A client that carries the field but has nothing to put in it has asked for the first page.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ListEmailsAsync_BlankCursor_ReadsTheFirstPage(string cursor)
    {
        // Arrange
        var timeline = new InMemoryStoredEmailTimeline().WithAll(SyntheticEmailSummaries.CreateDailyRun(3, FirstJuly));
        var reader = ReaderOver(timeline);

        // Act
        var result = await reader.ListEmailsAsync(
            new ListEmailsRequest { Cursor = cursor },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, result.Emails.Count);
        Assert.Null(Assert.Single(timeline.Calls).ContinueAfter);
    }

    /// <summary>An empty page would confirm the identifier exists, so an account nobody serves is refused instead.</summary>
    [Fact]
    public async Task ListEmailsAsync_AccountThisDeploymentDoesNotServe_IsRejectedWithoutReadingTheTimeline()
    {
        // Arrange
        var timeline = new InMemoryStoredEmailTimeline().WithAll(SyntheticEmailSummaries.CreateDailyRun(3, FirstJuly));
        var reader = ReaderOver(timeline, CatalogServing(MailAccountId.Create("primary")));

        // Act
        var failure = await Assert.ThrowsAsync<MailAccountNotAccessibleException>(() => reader.ListEmailsAsync(
            new ListEmailsRequest { AccountIds = [MailAccountId.Create("unknown")] },
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailAccountId.Create("unknown"), failure.AccountId);
        Assert.Empty(timeline.Calls);
    }

    /// <summary>Removing an account from configuration leaves its rows stored, so an unscoped read must not reach them.</summary>
    [Fact]
    public async Task ListEmailsAsync_NoAccountNamed_ReadsOnlyTheAccountsThisDeploymentServes()
    {
        // Arrange
        var served = SyntheticEmailSummaries.Create(FirstJuly, accountId: "primary");
        var retired = SyntheticEmailSummaries.Create(FirstJuly.AddDays(1), accountId: "retired");
        var timeline = new InMemoryStoredEmailTimeline().WithAll([served, retired]);
        var reader = ReaderOver(timeline, CatalogServing(MailAccountId.Create("primary")));

        // Act
        var result = await reader.ListEmailsAsync(new ListEmailsRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(served.StoredEmailId, Assert.Single(result.Emails).StoredEmailId);
        Assert.Equal(
            [MailAccountId.Create("primary")],
            Assert.Single(timeline.Calls).Filter.Selection.Scope.AccountIds);
    }

    /// <summary>Configuration allows serving no account while a local copy still exists, and none of it is readable.</summary>
    [Fact]
    public async Task ListEmailsAsync_NoAccountServedAtAll_ReturnsAnEmptyPageWithoutReadingAnything()
    {
        // Arrange
        var timeline = new InMemoryStoredEmailTimeline().WithAll(SyntheticEmailSummaries.CreateDailyRun(3, FirstJuly));
        var freshnessReader = FreshnessReaderReturning(InboxFreshness);
        var reader = ReaderOver(timeline, CatalogServing(), freshnessReader);

        // Act
        var result = await reader.ListEmailsAsync(new ListEmailsRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Emails);
        Assert.Null(result.NextCursor);
        Assert.Empty(result.FolderFreshness);
        Assert.Empty(timeline.Calls);
        await freshnessReader.DidNotReceive()
            .ReadAsync(Arg.Any<MailboxScope>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The refusals a request earns do not depend on how many accounts the deployment happens to serve.</summary>
    [Fact]
    public async Task ListEmailsAsync_NoAccountServedAtAllAndAnUnusableFilter_StillRefusesTheFilter()
    {
        // Arrange
        var reader = ReaderOver(new InMemoryStoredEmailTimeline(), CatalogServing());

        // Act, Assert
        await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(() => reader.ListEmailsAsync(
            new ListEmailsRequest { SenderAddress = "not-an-address" },
            TestContext.Current.CancellationToken));
    }

    /// <summary>The limit bounds what a request may name; a deployment's own account count is not caller input.</summary>
    [Fact]
    public async Task ListEmailsAsync_MoreServedAccountsThanARequestMayName_IsStillAnswered()
    {
        // Arrange
        var servedAccountIds = Enumerable.Range(0, MailboxScope.MaximumAccountIds + 1)
            .Select(index => MailAccountId.Create($"account-{index:D3}"))
            .ToArray();
        var email = SyntheticEmailSummaries.Create(FirstJuly, accountId: "account-007");
        var timeline = new InMemoryStoredEmailTimeline().With(email);
        var reader = ReaderOver(timeline, CatalogServing(servedAccountIds));

        // Act
        var result = await reader.ListEmailsAsync(new ListEmailsRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(email.StoredEmailId, Assert.Single(result.Emails).StoredEmailId);
        Assert.Equal(servedAccountIds, Assert.Single(timeline.Calls).Filter.Selection.Scope.AccountIds);
    }

    /// <summary>The resolved accounts take part in the fingerprint, so a cursor cannot outlive the scope it described.</summary>
    [Fact]
    public async Task ListEmailsAsync_CursorIssuedBeforeAnAccountWasRemoved_IsRejected()
    {
        // Arrange
        var timeline = new InMemoryStoredEmailTimeline().WithAll(SyntheticEmailSummaries.CreateDailyRun(4, FirstJuly));
        var whileBothWereServed = ReaderOver(timeline, CatalogServing(EveryAccountTheSyntheticTimelineUses));
        var firstPage = await whileBothWereServed.ListEmailsAsync(
            new ListEmailsRequest { PageSize = 2 },
            TestContext.Current.CancellationToken);
        var afterOneWasRemoved = ReaderOver(timeline, CatalogServing(MailAccountId.Create("primary")));

        // Act, Assert
        await Assert.ThrowsAsync<MailboxQueryCursorFilterMismatchException>(() =>
            afterOneWasRemoved.ListEmailsAsync(
                new ListEmailsRequest { PageSize = 2, Cursor = firstPage.NextCursor },
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListEmailsAsync_AccountFilter_ReturnsOnlyThatAccountsEmails()
    {
        // Arrange
        var wanted = SyntheticEmailSummaries.Create(FirstJuly, accountId: "primary");
        var other = SyntheticEmailSummaries.Create(FirstJuly.AddDays(1), accountId: "secondary");
        var reader = ReaderOver(new InMemoryStoredEmailTimeline().WithAll([wanted, other]));

        // Act
        var result = await reader.ListEmailsAsync(
            new ListEmailsRequest { AccountIds = [MailAccountId.Create("primary")] },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(wanted.StoredEmailId, Assert.Single(result.Emails).StoredEmailId);
    }

    [Fact]
    public async Task ListEmailsAsync_FolderFilter_ReturnsOnlyThatFoldersEmails()
    {
        // Arrange
        var wanted = SyntheticEmailSummaries.Create(FirstJuly, folderAlias: "ARCHIVE");
        var other = SyntheticEmailSummaries.Create(FirstJuly.AddDays(1), folderAlias: "INBOX");
        var reader = ReaderOver(new InMemoryStoredEmailTimeline().WithAll([wanted, other]));

        // Act
        var result = await reader.ListEmailsAsync(
            new ListEmailsRequest { FolderAliases = [MailFolderAlias.Create("archive")] },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(wanted.StoredEmailId, Assert.Single(result.Emails).StoredEmailId);
    }

    [Fact]
    public async Task ListEmailsAsync_SenderFilter_MatchesTheAddressWhateverCaseEitherSideWrote()
    {
        // Arrange
        var wanted = SyntheticEmailSummaries.Create(FirstJuly, senderAddress: "Anna@Example.test");
        var other = SyntheticEmailSummaries.Create(FirstJuly.AddDays(1), senderAddress: "bob@example.test");
        var reader = ReaderOver(new InMemoryStoredEmailTimeline().WithAll([wanted, other]));

        // Act
        var result = await reader.ListEmailsAsync(
            new ListEmailsRequest { SenderAddress = "anna@EXAMPLE.test" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(wanted.StoredEmailId, Assert.Single(result.Emails).StoredEmailId);
    }

    /// <summary>A recipient is an addressee, so the filter reaches the <c>To</c> and <c>Cc</c> headers alike.</summary>
    [Fact]
    public async Task ListEmailsAsync_RecipientFilter_MatchesAToAndACcAddressAlike()
    {
        // Arrange
        var addressedTo = SyntheticEmailSummaries.Create(FirstJuly, toAddresses: ["ANNA@EXAMPLE.TEST"]);
        var copiedTo = SyntheticEmailSummaries.Create(FirstJuly.AddDays(1), toAddresses: ["BOB@EXAMPLE.TEST"]);
        var unrelated = SyntheticEmailSummaries.Create(FirstJuly.AddDays(2), toAddresses: ["CAROL@EXAMPLE.TEST"]);
        var reader = ReaderOver(new InMemoryStoredEmailTimeline()
            .With(addressedTo)
            .With(copiedTo, "ANNA@EXAMPLE.TEST")
            .With(unrelated));

        // Act
        var result = await reader.ListEmailsAsync(
            new ListEmailsRequest { RecipientAddress = "anna@example.test" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [copiedTo.StoredEmailId, addressedTo.StoredEmailId],
            result.Emails.Select(email => email.StoredEmailId));
    }

    [Fact]
    public async Task ListEmailsAsync_SubjectFragment_MatchesWithoutRegardToCase()
    {
        // Arrange
        var wanted = SyntheticEmailSummaries.Create(FirstJuly, subject: "Quarterly Invoice 2026");
        var other = SyntheticEmailSummaries.Create(FirstJuly.AddDays(1), subject: "Lunch plans");
        var untitled = SyntheticEmailSummaries.Create(FirstJuly.AddDays(2));
        var reader = ReaderOver(new InMemoryStoredEmailTimeline().WithAll([wanted, other, untitled]));

        // Act
        var result = await reader.ListEmailsAsync(
            new ListEmailsRequest { SubjectFragment = "invoice" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(wanted.StoredEmailId, Assert.Single(result.Emails).StoredEmailId);
    }

    /// <summary>The range starts inclusively and ends exclusively, and mail nobody could date falls inside neither bound.</summary>
    [Fact]
    public async Task ListEmailsAsync_ReceivedRange_KeepsTheStartExcludesTheEndAndExcludesUndatedMail()
    {
        // Arrange
        var onTheStart = SyntheticEmailSummaries.Create(FirstJuly);
        var inside = SyntheticEmailSummaries.Create(FirstJuly.AddDays(1));
        var onTheEnd = SyntheticEmailSummaries.Create(FirstJuly.AddDays(2));
        var undated = SyntheticEmailSummaries.Create(receivedAt: null);
        var reader = ReaderOver(new InMemoryStoredEmailTimeline().WithAll([onTheStart, inside, onTheEnd, undated]));

        // Act
        var result = await reader.ListEmailsAsync(
            new ListEmailsRequest { ReceivedOnOrAfter = FirstJuly, ReceivedBefore = FirstJuly.AddDays(2) },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [inside.StoredEmailId, onTheStart.StoredEmailId],
            result.Emails.Select(email => email.StoredEmailId));
    }

    [Fact]
    public async Task ListEmailsAsync_ReceivedRangeEndingBeforeItStarts_IsRejected()
    {
        // Arrange
        var timeline = new InMemoryStoredEmailTimeline().WithAll(SyntheticEmailSummaries.CreateDailyRun(3, FirstJuly));
        var reader = ReaderOver(timeline);

        // Act
        var failure = await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(() => reader.ListEmailsAsync(
            new ListEmailsRequest { ReceivedOnOrAfter = FirstJuly.AddDays(2), ReceivedBefore = FirstJuly },
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("received date range", failure.FilterName);
        Assert.Empty(timeline.Calls);
    }

    /// <summary>Flags nobody has observed are unset, so such an email matches the unseen side of the filter.</summary>
    [Fact]
    public async Task ListEmailsAsync_UnseenFilter_IncludesEmailsWhoseFlagsWereNeverObserved()
    {
        // Arrange
        var neverObserved = SyntheticEmailSummaries.Create(FirstJuly);
        var observedUnseen = SyntheticEmailSummaries.Create(
            FirstJuly.AddDays(1),
            isRemotelySeen: false,
            remoteFlagsObservedAt: FirstJuly.AddDays(3));
        var observedSeen = SyntheticEmailSummaries.Create(
            FirstJuly.AddDays(2),
            isRemotelySeen: true,
            remoteFlagsObservedAt: FirstJuly.AddDays(3));
        var reader = ReaderOver(
            new InMemoryStoredEmailTimeline().WithAll([neverObserved, observedUnseen, observedSeen]));

        // Act
        var result = await reader.ListEmailsAsync(
            new ListEmailsRequest { IsRemotelySeen = false },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [observedUnseen.StoredEmailId, neverObserved.StoredEmailId],
            result.Emails.Select(email => email.StoredEmailId));
        Assert.False(result.Emails[^1].RemoteFlags.WasObserved);
    }

    /// <summary>Attachment presence follows the extraction classification, so an inline-only message is not mail with attachments.</summary>
    [Fact]
    public async Task ListEmailsAsync_AttachmentFilter_TreatsAnInlineOnlyEmailAsCarryingNone()
    {
        // Arrange
        var withAttachment = SyntheticEmailSummaries.Create(FirstJuly, attachmentCount: 1);
        var inlineOnly = SyntheticEmailSummaries.Create(FirstJuly.AddDays(1), inlineResourceCount: 3);
        var reader = ReaderOver(new InMemoryStoredEmailTimeline().WithAll([withAttachment, inlineOnly]));

        // Act
        var withAttachments = await reader.ListEmailsAsync(
            new ListEmailsRequest { HasAttachments = true },
            TestContext.Current.CancellationToken);
        var withoutAttachments = await reader.ListEmailsAsync(
            new ListEmailsRequest { HasAttachments = false },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(withAttachment.StoredEmailId, Assert.Single(withAttachments.Emails).StoredEmailId);
        var inlineOnlySummary = Assert.Single(withoutAttachments.Emails);
        Assert.Equal(inlineOnly.StoredEmailId, inlineOnlySummary.StoredEmailId);
        Assert.Equal(3, inlineOnlySummary.Attachments.InlineResourceCount);
    }

    [Fact]
    public async Task ListEmailsAsync_SeveralFiltersTogether_ReturnsOnlyTheEmailsMatchingAllOfThem()
    {
        // Arrange
        var wanted = SyntheticEmailSummaries.Create(
            FirstJuly.AddDays(1),
            accountId: "primary",
            folderAlias: "ARCHIVE",
            subject: "Quarterly invoice",
            senderAddress: "anna@example.test",
            attachmentCount: 2);
        var wrongFolder = SyntheticEmailSummaries.Create(
            FirstJuly.AddDays(1),
            folderAlias: "INBOX",
            subject: "Quarterly invoice",
            senderAddress: "anna@example.test",
            attachmentCount: 2);
        var wrongSender = SyntheticEmailSummaries.Create(
            FirstJuly.AddDays(1),
            folderAlias: "ARCHIVE",
            subject: "Quarterly invoice",
            senderAddress: "bob@example.test",
            attachmentCount: 2);
        var outsideTheRange = SyntheticEmailSummaries.Create(
            FirstJuly.AddDays(9),
            folderAlias: "ARCHIVE",
            subject: "Quarterly invoice",
            senderAddress: "anna@example.test",
            attachmentCount: 2);
        var reader = ReaderOver(
            new InMemoryStoredEmailTimeline().WithAll([wanted, wrongFolder, wrongSender, outsideTheRange]));

        // Act
        var result = await reader.ListEmailsAsync(
            new ListEmailsRequest
            {
                AccountIds = [MailAccountId.Create("primary")],
                FolderAliases = [MailFolderAlias.Create("ARCHIVE")],
                SubjectFragment = "invoice",
                SenderAddress = "anna@example.test",
                HasAttachments = true,
                ReceivedOnOrAfter = FirstJuly,
                ReceivedBefore = FirstJuly.AddDays(5),
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(wanted.StoredEmailId, Assert.Single(result.Emails).StoredEmailId);
    }

    /// <summary>Freshness travels with every page, and it is read for the same normalized scope the page was.</summary>
    [Fact]
    public async Task ListEmailsAsync_AnyPage_ReportsHowCurrentTheScopesLocalCopyIs()
    {
        // Arrange
        var freshnessReader = FreshnessReaderReturning(InboxFreshness);
        var reader = ReaderOver(
            new InMemoryStoredEmailTimeline().WithAll(SyntheticEmailSummaries.CreateDailyRun(2, FirstJuly)),
            freshnessReader: freshnessReader);

        // Act
        var result = await reader.ListEmailsAsync(
            new ListEmailsRequest { AccountIds = [MailAccountId.Create("primary")] },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(InboxFreshness, Assert.Single(result.FolderFreshness));
        await freshnessReader.Received(1).ReadAsync(
            Arg.Is<MailboxScope>(scope => scope != null
                && scope.AccountIds.Count == 1
                && scope.AccountIds[0] == MailAccountId.Create("primary")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListEmailsAsync_CancelledCaller_StopsBeforeReadingTheTimeline()
    {
        // Arrange
        var reader = ReaderOver(
            new InMemoryStoredEmailTimeline().WithAll(SyntheticEmailSummaries.CreateDailyRun(2, FirstJuly)));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act, Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            reader.ListEmailsAsync(new ListEmailsRequest(), cancellation.Token));
    }

    [Fact]
    public async Task ListEmailsAsync_NoRequest_IsRejected()
    {
        // Arrange
        var reader = ReaderOver(new InMemoryStoredEmailTimeline());

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            reader.ListEmailsAsync(null!, TestContext.Current.CancellationToken));
    }

    private static async Task<IReadOnlyList<StoredEmailId>> WalkEveryPageAsync(
        MailboxTimelineReader reader,
        ListEmailsRequest request)
    {
        var visited = new List<StoredEmailId>();
        string? cursor = null;

        // Bounded so a use case that reissues the same cursor fails on the assertions rather than looping forever.
        for (var page = 0; page < 100; page++)
        {
            var result = await reader.ListEmailsAsync(
                request with { Cursor = cursor },
                TestContext.Current.CancellationToken);

            visited.AddRange(result.Emails.Select(email => email.StoredEmailId));
            cursor = result.NextCursor;

            if (cursor is null)
            {
                break;
            }
        }

        return visited;
    }

    private static MailboxTimelineReader ReaderOver(
        InMemoryStoredEmailTimeline timeline,
        IMailAccountCatalog? accountCatalog = null,
        ISynchronizationFreshnessReader? freshnessReader = null) => new(
        timeline,
        freshnessReader ?? FreshnessReaderReturning(InboxFreshness),
        new MailboxScopeResolver(accountCatalog ?? CatalogServing(EveryAccountTheSyntheticTimelineUses)));

    /// <summary>Builds a catalog that serves exactly the accounts named, in the order the port promises.</summary>
    private static IMailAccountCatalog CatalogServing(params MailAccountId[] servedAccountIds)
    {
        var catalog = Substitute.For<IMailAccountCatalog>();
        catalog.ServedAccountIds.Returns(
        [
            .. servedAccountIds.OrderBy(accountId => accountId.Value, StringComparer.Ordinal),
        ]);

        return catalog;
    }

    private static ISynchronizationFreshnessReader FreshnessReaderReturning(params MailboxFolderFreshness[] freshness)
    {
        var reader = Substitute.For<ISynchronizationFreshnessReader>();
        reader.ReadAsync(Arg.Any<MailboxScope>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MailboxFolderFreshness>>(freshness));

        return reader;
    }

    private static string Encoded(string payload) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));
}
