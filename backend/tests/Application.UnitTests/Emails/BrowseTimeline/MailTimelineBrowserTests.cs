// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.BrowseTimeline;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Observability;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.BrowseTimeline;

/// <summary>Covers the message list a screen is drawn from: the page it bounds, the previews it attaches, and the walk it runs in both directions.</summary>
public sealed class MailTimelineBrowserTests
{
    /// <summary>The literal the scanner in the guarded-egress tests reports, standing in for a credential in mail.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId[] EveryAccountTheSyntheticTimelineUses =
    [
        MailAccountId.Create(SyntheticEmailSummaries.DefaultAccountId),
    ];

    public static TheoryData<EmailTimelineDirection> BothOrders =>
    [
        EmailTimelineDirection.NewestFirst,
        EmailTimelineDirection.OldestFirst,
    ];

    /// <summary>The leading end of a list has nothing before it, which is what tells a screen not to offer scrolling back.</summary>
    [Fact]
    public async Task BrowsePageAsync_NoCursor_ReadsTheLeadingEndAndOffersNoPreviousPage()
    {
        // Arrange
        var emails = SyntheticEmailSummaries.CreateDailyRun(4, FirstJuly);
        var browser = BrowserOver(new InMemoryStoredEmailTimeline().WithAll(emails));

        // Act
        var page = await browser.BrowsePageAsync(new BrowseTimelineRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            emails.Reverse().Select(email => email.StoredEmailId),
            page.Emails.Select(row => row.Email.StoredEmailId));
        Assert.Null(page.PreviousCursor);
        Assert.Null(page.NextCursor);
    }

    /// <summary>The page is bounded by the effective size, and the row beyond it is what establishes the next cursor.</summary>
    [Fact]
    public async Task BrowsePageAsync_MoreMailThanThePage_BoundsThePageAndAsksForOneRowBeyondIt()
    {
        // Arrange
        var timeline = new InMemoryStoredEmailTimeline().WithAll(SyntheticEmailSummaries.CreateDailyRun(10, FirstJuly));
        var browser = BrowserOver(timeline);

        // Act
        var page = await browser.BrowsePageAsync(
            new BrowseTimelineRequest { PageSize = 3 },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, page.Emails.Count);
        Assert.Equal(3, page.PageSize);
        Assert.Equal(4, Assert.Single(timeline.Calls).Limit);
        Assert.NotNull(page.NextCursor);
    }

    /// <summary>A request naming no page size runs under the default, which is what the answer reports back.</summary>
    [Fact]
    public async Task BrowsePageAsync_NoPageSizeNamed_ReportsTheDefaultItRanUnder()
    {
        // Arrange
        var browser = BrowserOver(
            new InMemoryStoredEmailTimeline().WithAll(SyntheticEmailSummaries.CreateDailyRun(2, FirstJuly)));

        // Act
        var page = await browser.BrowsePageAsync(new BrowseTimelineRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailboxQueryPageSize.DefaultValue, page.PageSize);
    }

    /// <summary>Continuing forward is what scrolling down is, and it may neither skip a row nor repeat one.</summary>
    [Fact]
    public async Task BrowsePageAsync_TheNextCursor_ContinuesTheListWithoutSkippingOrRepeatingARow()
    {
        // Arrange
        var emails = SyntheticEmailSummaries.CreateDailyRun(6, FirstJuly);
        var browser = BrowserOver(new InMemoryStoredEmailTimeline().WithAll(emails));

        // Act
        var first = await browser.BrowsePageAsync(
            new BrowseTimelineRequest { PageSize = 2 },
            TestContext.Current.CancellationToken);
        var second = await browser.BrowsePageAsync(
            new BrowseTimelineRequest { PageSize = 2, Cursor = first.NextCursor },
            TestContext.Current.CancellationToken);
        var third = await browser.BrowsePageAsync(
            new BrowseTimelineRequest { PageSize = 2, Cursor = second.NextCursor },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            emails.Reverse().Select(email => email.StoredEmailId),
            new[] { first, second, third }.SelectMany(page => page.Emails).Select(row => row.Email.StoredEmailId));
    }

    /// <summary>A page reached from a cursor has whatever that cursor was taken from behind it, so it can always be scrolled back from.</summary>
    [Fact]
    public async Task BrowsePageAsync_APageReachedFromACursor_OffersAPreviousPage()
    {
        // Arrange
        var browser = BrowserOver(new InMemoryStoredEmailTimeline().WithAll(
            SyntheticEmailSummaries.CreateDailyRun(6, FirstJuly)));

        var first = await browser.BrowsePageAsync(
            new BrowseTimelineRequest { PageSize = 2 },
            TestContext.Current.CancellationToken);

        // Act
        var second = await browser.BrowsePageAsync(
            new BrowseTimelineRequest { PageSize = 2, Cursor = first.NextCursor },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(second.PreviousCursor);
    }

    /// <summary>Scrolling back returns the page that was there before, in the order the list is sorted in rather than reversed.</summary>
    [Theory]
    [MemberData(nameof(BothOrders))]
    public async Task BrowsePageAsync_ReadingBackwardFromTheSecondPage_ReturnsTheFirstPageInTheSortedOrder(
        EmailTimelineDirection order)
    {
        // Arrange
        var browser = BrowserOver(new InMemoryStoredEmailTimeline().WithAll(
            SyntheticEmailSummaries.CreateDailyRun(6, FirstJuly)));

        var first = await browser.BrowsePageAsync(
            new BrowseTimelineRequest { PageSize = 2, Order = order },
            TestContext.Current.CancellationToken);
        var second = await browser.BrowsePageAsync(
            new BrowseTimelineRequest { PageSize = 2, Order = order, Cursor = first.NextCursor },
            TestContext.Current.CancellationToken);

        // Act
        var back = await browser.BrowsePageAsync(
            new BrowseTimelineRequest
            {
                PageSize = 2,
                Order = order,
                Cursor = second.PreviousCursor,
                PageDirection = TimelinePageDirection.Backward,
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            first.Emails.Select(row => row.Email.StoredEmailId),
            back.Emails.Select(row => row.Email.StoredEmailId));
    }

    /// <summary>A backward page always has something after it, because the cursor it was measured from names a row that is still there.</summary>
    [Fact]
    public async Task BrowsePageAsync_ReadingBackward_OffersTheWayForwardAgain()
    {
        // Arrange
        var browser = BrowserOver(new InMemoryStoredEmailTimeline().WithAll(
            SyntheticEmailSummaries.CreateDailyRun(6, FirstJuly)));

        var first = await browser.BrowsePageAsync(
            new BrowseTimelineRequest { PageSize = 2 },
            TestContext.Current.CancellationToken);
        var second = await browser.BrowsePageAsync(
            new BrowseTimelineRequest { PageSize = 2, Cursor = first.NextCursor },
            TestContext.Current.CancellationToken);

        // Act
        var back = await browser.BrowsePageAsync(
            new BrowseTimelineRequest
            {
                PageSize = 2,
                Cursor = second.PreviousCursor,
                PageDirection = TimelinePageDirection.Backward,
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(back.NextCursor);
    }

    /// <summary>Scrolling back to the top of a list is what makes the previous cursor stop, which is how a screen knows it has arrived.</summary>
    [Fact]
    public async Task BrowsePageAsync_ReadingBackwardOntoTheLeadingEnd_OffersNoFurtherPreviousPage()
    {
        // Arrange
        var browser = BrowserOver(new InMemoryStoredEmailTimeline().WithAll(
            SyntheticEmailSummaries.CreateDailyRun(4, FirstJuly)));

        var first = await browser.BrowsePageAsync(
            new BrowseTimelineRequest { PageSize = 2 },
            TestContext.Current.CancellationToken);
        var second = await browser.BrowsePageAsync(
            new BrowseTimelineRequest { PageSize = 2, Cursor = first.NextCursor },
            TestContext.Current.CancellationToken);

        // Act
        var back = await browser.BrowsePageAsync(
            new BrowseTimelineRequest
            {
                PageSize = 2,
                Cursor = second.PreviousCursor,
                PageDirection = TimelinePageDirection.Backward,
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(back.PreviousCursor);
    }

    /// <summary>There is no page before the leading end, so asking for one is a mistake rather than a request for the first page.</summary>
    [Fact]
    public async Task BrowsePageAsync_ReadingBackwardWithoutACursor_IsRefusedRatherThanAnsweredWithTheLeadingPage()
    {
        // Arrange
        var browser = BrowserOver(new InMemoryStoredEmailTimeline().WithAll(
            SyntheticEmailSummaries.CreateDailyRun(3, FirstJuly)));

        // Act
        var refusal = await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(() =>
            browser.BrowsePageAsync(
                new BrowseTimelineRequest { PageDirection = TimelinePageDirection.Backward },
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("page direction", refusal.FilterName);
    }

    /// <summary>A cursor names a boundary in one filtered set, so presenting it against another list is refused rather than absorbed.</summary>
    [Fact]
    public async Task BrowsePageAsync_ACursorIssuedForOtherFilters_IsRefused()
    {
        // Arrange
        var browser = BrowserOver(new InMemoryStoredEmailTimeline().WithAll(
            SyntheticEmailSummaries.CreateDailyRun(6, FirstJuly)));

        var page = await browser.BrowsePageAsync(
            new BrowseTimelineRequest { PageSize = 2 },
            TestContext.Current.CancellationToken);

        // Act, Assert
        await Assert.ThrowsAsync<MailboxQueryCursorFilterMismatchException>(() =>
            browser.BrowsePageAsync(
                new BrowseTimelineRequest { PageSize = 2, Cursor = page.NextCursor, IsRemotelyFlagged = true },
                TestContext.Current.CancellationToken));
    }

    /// <summary>The order is part of what a cursor was issued for, so turning the list over invalidates one taken before.</summary>
    [Fact]
    public async Task BrowsePageAsync_ACursorIssuedUnderTheOtherOrder_IsRefused()
    {
        // Arrange
        var browser = BrowserOver(new InMemoryStoredEmailTimeline().WithAll(
            SyntheticEmailSummaries.CreateDailyRun(6, FirstJuly)));

        var page = await browser.BrowsePageAsync(
            new BrowseTimelineRequest { PageSize = 2 },
            TestContext.Current.CancellationToken);

        // Act, Assert
        await Assert.ThrowsAsync<MailboxQueryCursorFilterMismatchException>(() =>
            browser.BrowsePageAsync(
                new BrowseTimelineRequest
                {
                    PageSize = 2,
                    Cursor = page.NextCursor,
                    Order = EmailTimelineDirection.OldestFirst,
                },
                TestContext.Current.CancellationToken));
    }

    /// <summary>A cursor this deployment never issued is refused, because answering with the leading page reads as having scrolled back to the top.</summary>
    [Fact]
    public async Task BrowsePageAsync_ACursorThisSystemDidNotIssue_IsRefused()
    {
        // Arrange
        var browser = BrowserOver(new InMemoryStoredEmailTimeline().WithAll(
            SyntheticEmailSummaries.CreateDailyRun(3, FirstJuly)));

        // Act, Assert
        await Assert.ThrowsAsync<MailboxQueryCursorMalformedException>(() =>
            browser.BrowsePageAsync(
                new BrowseTimelineRequest { Cursor = "not a cursor at all" },
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(MailboxQueryPageSize.MaximumValue + 1)]
    public async Task BrowsePageAsync_APageSizeOutsideTheAcceptedRange_IsRefusedRatherThanClamped(int pageSize)
    {
        // Arrange
        var browser = BrowserOver(new InMemoryStoredEmailTimeline().WithAll(
            SyntheticEmailSummaries.CreateDailyRun(3, FirstJuly)));

        // Act, Assert
        await Assert.ThrowsAsync<MailboxQueryPageSizeOutOfRangeException>(() =>
            browser.BrowsePageAsync(
                new BrowseTimelineRequest { PageSize = pageSize },
                TestContext.Current.CancellationToken));
    }

    /// <summary>The preview is what lets a row be drawn from the page rather than from a request per row.</summary>
    [Fact]
    public async Task BrowsePageAsync_AMessageWhoseTextWasExtracted_CarriesItsPreviewAsOneLine()
    {
        // Arrange
        var email = SyntheticEmailSummaries.Create(FirstJuly);
        var previews = new InMemoryStoredEmailPreviews().With(email.StoredEmailId, "the release\n\nis  out");
        var browser = BrowserOver(new InMemoryStoredEmailTimeline().With(email), previewReader: previews);

        // Act
        var page = await browser.BrowsePageAsync(new BrowseTimelineRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("the release is out", Assert.Single(page.Emails).Preview);
    }

    /// <summary>Mail this deployment has stored but not yet extracted has no preview, which is not the same as an empty one.</summary>
    [Fact]
    public async Task BrowsePageAsync_AMessageNothingHasExtracted_CarriesNoPreview()
    {
        // Arrange
        var browser = BrowserOver(new InMemoryStoredEmailTimeline().With(SyntheticEmailSummaries.Create(FirstJuly)));

        // Act
        var page = await browser.BrowsePageAsync(new BrowseTimelineRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(Assert.Single(page.Emails).Preview);
    }

    /// <summary>The row beyond the page is a boundary probe and never a message the caller sees, so no text is read for it.</summary>
    [Fact]
    public async Task BrowsePageAsync_MoreMailThanThePage_ReadsPreviewsForThePageAloneAndNotForTheProbe()
    {
        // Arrange
        var previews = new InMemoryStoredEmailPreviews();
        var browser = BrowserOver(
            new InMemoryStoredEmailTimeline().WithAll(SyntheticEmailSummaries.CreateDailyRun(10, FirstJuly)),
            previewReader: previews);

        // Act
        var page = await browser.BrowsePageAsync(
            new BrowseTimelineRequest { PageSize = 3 },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            page.Emails.Select(row => row.Email.StoredEmailId),
            Assert.Single(previews.Calls));
    }

    /// <summary>The preview is the message's own text, so a page whose preview went out unscanned is the leak the subject beside it was redacted to prevent.</summary>
    [Fact]
    public async Task BrowsePageAsync_ADeploymentThatScans_RedactsTheSubjectTheSenderNameAndThePreview()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var email = SyntheticEmailSummaries.Create(FirstJuly, subject: $"the key is {Marker}") with
        {
            SenderDisplayName = $"deploy bot {Marker}",
        };
        var previews = new InMemoryStoredEmailPreviews().With(email.StoredEmailId, $"it reads {Marker} today");
        var browser = BrowserOver(
            new InMemoryStoredEmailTimeline().With(email),
            previewReader: previews,
            egressGuard: egress.Guard);

        // Act
        var page = await browser.BrowsePageAsync(new BrowseTimelineRequest(), TestContext.Current.CancellationToken);

        // Assert
        var row = Assert.Single(page.Emails);

        Assert.DoesNotContain(Marker, row.Email.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, row.Email.SenderDisplayName, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, row.Preview, StringComparison.Ordinal);
    }

    /// <summary>Serving a page a scanner could not read would be the leak the switch was turned on to prevent.</summary>
    [Fact]
    public async Task BrowsePageAsync_ADetectorThatCannotAnswer_RefusesThePageRatherThanServingItUnscanned()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Unavailable(TimeProvider.System);
        var browser = BrowserOver(
            new InMemoryStoredEmailTimeline().With(SyntheticEmailSummaries.Create(FirstJuly, subject: "a subject")),
            egressGuard: egress.Guard);

        // Act, Assert
        await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(() =>
            browser.BrowsePageAsync(new BrowseTimelineRequest(), TestContext.Current.CancellationToken));
    }

    /// <summary>An owner who owns no account reads an empty list rather than every other owner's mail.</summary>
    [Fact]
    public async Task BrowsePageAsync_AnOwnerWhoOwnsNoAccount_ReadsAnEmptyPageWithoutReachingStorage()
    {
        // Arrange
        var timeline = new InMemoryStoredEmailTimeline().WithAll(SyntheticEmailSummaries.CreateDailyRun(3, FirstJuly));
        var browser = BrowserOver(timeline, accountCatalog: CatalogServing());

        // Act
        var page = await browser.BrowsePageAsync(new BrowseTimelineRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(page.Emails);
        Assert.Empty(timeline.Calls);
        Assert.Null(page.NextCursor);
        Assert.Null(page.PreviousCursor);
    }

    /// <summary>The read is reported as the operation it is, so a page a screen waited on has a use case above its queries in a trace.</summary>
    [Fact]
    public async Task BrowsePageAsync_APageThatWasServed_ReportsTheListingAndWhatItReturned()
    {
        // Arrange
        var readTelemetry = new RecordingMailboxReadTelemetry();
        var browser = BrowserOver(
            new InMemoryStoredEmailTimeline().WithAll(SyntheticEmailSummaries.CreateDailyRun(3, FirstJuly)),
            readTelemetry: readTelemetry);

        // Act
        await browser.BrowsePageAsync(new BrowseTimelineRequest(), TestContext.Current.CancellationToken);

        // Assert
        var read = Assert.Single(readTelemetry.Reads);

        Assert.Equal(MailboxReadOperation.ListMailboxTimeline, read.Operation);
        Assert.Equal(3, read.ResultCount);
        Assert.True(read.WasClosed);
    }

    /// <summary>The grant is the authority here rather than at the transport, so an entrypoint added later meets the same refusal.</summary>
    [Fact]
    public async Task BrowsePageAsync_ACallerWithoutTheMailReadGrant_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var browser = BrowserOver(
            new InMemoryStoredEmailTimeline().WithAll(SyntheticEmailSummaries.CreateDailyRun(2, FirstJuly)),
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailAsk));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            browser.BrowsePageAsync(new BrowseTimelineRequest(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailRead, refusal.RequiredPermission);
    }

    /// <summary>The grant is read before the request is, so a caller that may not read learns nothing about what this deployment accepts.</summary>
    [Fact]
    public async Task BrowsePageAsync_ACallerGrantedNothingSendingAnInvalidRequest_IsRefusedForTheGrant()
    {
        // Arrange
        var browser = BrowserOver(
            new InMemoryStoredEmailTimeline(),
            authorization: AccessAuthorizations.ForCallerGranted());

        // Act, Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() => browser.BrowsePageAsync(
            new BrowseTimelineRequest { PageSize = int.MaxValue },
            TestContext.Current.CancellationToken));
    }

    private static MailTimelineBrowser BrowserOver(
        InMemoryStoredEmailTimeline timeline,
        IStoredEmailPreviewReader? previewReader = null,
        ICallerMailAccountCatalog? accountCatalog = null,
        SensitiveContentEgressGuard? egressGuard = null,
        IMailboxReadTelemetry? readTelemetry = null,
        AccessAuthorization? authorization = null) => new(
        timeline,
        previewReader ?? new InMemoryStoredEmailPreviews(),
        new MailboxScopeResolver(
            accountCatalog ?? CatalogServing(EveryAccountTheSyntheticTimelineUses),
            StubMailFolderParticipation.Nothing,
            StubJunkMailFolderCatalog.None,
            StubMailFolderMappings.ResolvingNothing),
        egressGuard ?? SensitiveContentEgressGuards.Inactive(),
        readTelemetry ?? new RecordingMailboxReadTelemetry(),
        authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

    /// <summary>Builds a catalog that serves exactly the accounts named, in the order the port promises.</summary>
    private static ICallerMailAccountCatalog CatalogServing(params MailAccountId[] servedAccountIds)
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.OwnedAccounts.Returns(
        [
            .. servedAccountIds
                .OrderBy(accountId => accountId.Value, StringComparer.Ordinal)
                .Select(accountId => SyntheticServedAccount.Of(accountId)),
        ]);

        return catalog;
    }
}
