// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.BrowseTimeline;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Observability;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the mail list route accepts, what it refuses, and what it puts on the wire.</summary>
/// <remarks>
/// The walk itself — the keyset boundaries, the cursors, and the preview — is covered where it is decided. What is
/// asserted here is the transport: that a parameter this deployment cannot honour is refused rather than ignored, that
/// each refusal says which one it is, and that a row reaches the wire as the fields a screen draws from.
/// </remarks>
public sealed class ClientMailTimelineEndpointTests
{
    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly IStoredEmailTimelineReader timeline = Substitute.For<IStoredEmailTimelineReader>();

    /// <summary>The path a client appends to the address it was configured with, pinned because the client composes it from a constant of its own.</summary>
    [Fact]
    public void MailTimelineRoute_IsThePathAClientComposes() =>
        Assert.Equal("/emails", ClientMailTimelineEndpoint.MailTimelineRoute);

    /// <summary>A screen that asked to be sorted by something this deployment cannot index would otherwise be handed the default order and no way to tell.</summary>
    [Fact]
    public async Task ReadTimelineAsync_ASortThisDeploymentCannotIndex_IsRefusedRatherThanIgnored()
    {
        // Act
        var result = await this.ReadAsync(sort: "subject");

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>The one sort this deployment can index is accepted when it is named as well as when it is left out.</summary>
    [Fact]
    public async Task ReadTimelineAsync_TheSortThisDeploymentIndexes_IsAccepted()
    {
        // Act
        var result = await this.ReadAsync(sort: ClientMailTimelineEndpoint.ReceivedAtSort);

        // Assert
        Assert.IsType<Ok<ClientMailTimelineResponse>>(result.Result);
    }

    [Theory]
    [InlineData("descending")]
    [InlineData("newestFirst,oldestFirst")]
    [InlineData("0")]
    public async Task ReadTimelineAsync_AnOrderThisSurfaceDoesNotPublish_IsRefused(string order)
    {
        // Act
        var result = await this.ReadAsync(order: order);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    [Theory]
    [InlineData("back")]
    [InlineData("forward,backward")]
    [InlineData("1")]
    public async Task ReadTimelineAsync_ADirectionThisSurfaceDoesNotPublish_IsRefused(string direction)
    {
        // Act
        var result = await this.ReadAsync(direction: direction);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>A field a screen has nothing to put in yet arrives empty, which is the same request as one that omits it.</summary>
    [Fact]
    public async Task ReadTimelineAsync_ParametersSentEmpty_AreReadAsParametersThatWereNotSent()
    {
        // Act
        var result = await this.ReadAsync(account: "", folder: "  ", sort: "", order: "", direction: "");

        // Assert
        Assert.IsType<Ok<ClientMailTimelineResponse>>(result.Result);
    }

    /// <summary>Case is not part of a published name here, the way it is not part of a folder role.</summary>
    [Fact]
    public async Task ReadTimelineAsync_APublishedNameWrittenInAnotherCase_IsAccepted()
    {
        // Act
        var result = await this.ReadAsync(order: "OldestFirst");

        // Assert
        Assert.IsType<Ok<ClientMailTimelineResponse>>(result.Result);
        await this.timeline.Received(1).ReadPageAsync(
            Arg.Is<EmailTimelineFilter>(filter =>
                filter != null && filter.Direction == EmailTimelineDirection.OldestFirst),
            Arg.Any<EmailTimelinePosition?>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Text naming neither an alias nor a role names no folder, and a page over every folder would be the wrong answer to it.</summary>
    [Fact]
    public async Task ReadTimelineAsync_AFolderNamingNeitherAnAliasNorARole_IsRefused()
    {
        // Act
        var result = await this.ReadAsync(folder: "role:NotARoleAnyServerAdvertises");

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>There is no page before the leading end, so the surface refuses rather than answering with the leading page.</summary>
    [Fact]
    public async Task ReadTimelineAsync_ABackwardPageWithNoCursor_IsRefused()
    {
        // Act
        var result = await this.ReadAsync(direction: ClientMailTimelineEndpoint.BackwardDirection);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>
    /// A cursor nobody issued and a cursor issued for another list are two mistakes with two repairs, so the refusals
    /// say which one it was rather than both reading as "try again".
    /// </summary>
    [Fact]
    public async Task ReadTimelineAsync_TheTwoWaysACursorCanBeWrong_AreRefusedDistinguishably()
    {
        // Arrange
        var issued = await this.ReadAsync(pageSize: 1);
        var carried = Assert.IsType<Ok<ClientMailTimelineResponse>>(issued.Result).Value?.NextCursor;

        // Act
        var invented = await this.ReadAsync(cursor: "not a cursor at all");
        var otherList = await this.ReadAsync(cursor: carried, order: ClientMailTimelineEndpoint.OldestFirstOrder);

        // Assert
        Assert.NotEqual(
            Assert.IsType<ProblemHttpResult>(invented.Result).ProblemDetails.Detail,
            Assert.IsType<ProblemHttpResult>(otherList.Result).ProblemDetails.Detail);
    }

    [Fact]
    public async Task ReadTimelineAsync_APageSizeOutsideTheAcceptedRange_IsRefused()
    {
        // Act
        var result = await this.ReadAsync(pageSize: MailboxQueryPageSize.MaximumValue + 1);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>A request that names nothing draws the newest mail of every folder the owner owns, which is what a screen opens on.</summary>
    [Fact]
    public async Task ReadTimelineAsync_NoParameters_ReadsTheNewestMailFirstFromTheLeadingEnd()
    {
        // Act
        var result = await this.ReadAsync();

        // Assert
        Assert.IsType<Ok<ClientMailTimelineResponse>>(result.Result);
        await this.timeline.Received(1).ReadPageAsync(
            Arg.Is<EmailTimelineFilter>(filter =>
                filter != null && filter.Direction == EmailTimelineDirection.NewestFirst),
            null,
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The unread toggle a screen shows is the absence of the server's own <c>\Seen</c> flag rather than a state of its own.</summary>
    [Fact]
    public async Task ReadTimelineAsync_TheUnreadFilter_AsksForMailTheServerDidNotReportAsSeen()
    {
        // Act
        await this.ReadAsync(unread: true);

        // Assert
        await this.timeline.Received(1).ReadPageAsync(
            Arg.Is<EmailTimelineFilter>(filter =>
                filter != null && filter.Selection.IsRemotelySeen == false),
            Arg.Any<EmailTimelinePosition?>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Every fact a list row draws reaches the wire from one request, which is what the route exists for.</summary>
    [Fact]
    public void For_ARowOfTheList_CarriesEveryFactAListDraws()
    {
        // Arrange
        var row = new BrowsedEmail(
            SyntheticListedEmail(subject: "the release is out", attachmentCount: 2),
            "it went out this morning");

        // Act
        var response = ClientMailTimelineEntryResponse.For(row);

        // Assert
        Assert.Equal("work", response.Account);
        Assert.Equal("INBOX", response.Folder);
        Assert.Equal("the release is out", response.Subject);
        Assert.Equal(FirstJuly, response.ReceivedAt);
        Assert.Equal("sender@example.test", response.SenderAddress);
        Assert.Equal(["someone@example.test"], response.ToAddresses);
        Assert.True(response.Unread);
        Assert.True(response.HasAttachments);
        Assert.Equal(2, response.AttachmentCount);
        Assert.Equal("it went out this morning", response.Preview);
    }

    /// <summary>A screen draws an unread badge, so the flag reaches it as the state it draws rather than as the one the protocol names.</summary>
    [Fact]
    public void For_ARowTheServerReportedAsSeen_IsNotUnread()
    {
        // Arrange
        var row = new BrowsedEmail(SyntheticListedEmail(isRemotelySeen: true), Preview: null);

        // Act
        var response = ClientMailTimelineEntryResponse.For(row);

        // Assert
        Assert.False(response.Unread);
        Assert.Null(response.Preview);
    }

    /// <summary>Both cursors reach the wire, because a list a screen can only scroll one way is half a list.</summary>
    [Fact]
    public void For_APageInTheMiddleOfTheList_CarriesTheCursorAtBothEnds()
    {
        // Arrange
        var page = new BrowsedTimelinePage(
            [new BrowsedEmail(SyntheticListedEmail(), Preview: null)],
            NextCursor: "after",
            PreviousCursor: "before",
            PageSize: 25);

        // Act
        var response = ClientMailTimelineResponse.For(page);

        // Assert
        Assert.Equal("after", response.NextCursor);
        Assert.Equal("before", response.PreviousCursor);
        Assert.Equal(25, response.PageSize);
        Assert.Single(response.Emails);
    }

    private static EmailSummary SyntheticListedEmail(
        string? subject = null,
        bool isRemotelySeen = false,
        int attachmentCount = 0) => new()
        {
            StoredEmailId = StoredEmailId.Create(Guid.CreateVersion7()),
            Account = MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work")),
            FolderAlias = MailFolderAlias.Create("INBOX"),
            Subject = subject,
            SentAt = FirstJuly,
            ReceivedAt = FirstJuly,
            SizeOctets = 2048,
            SenderAddress = "sender@example.test",
            ToAddresses = ["someone@example.test"],
            SenderVerification = SenderVerification.NotEstablished,
            SenderAuthenticationEvidence = SenderAuthenticationEvidence.None,
            MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
            Attachments = new StoredEmailAttachmentSummary(
                attachmentCount,
                TotalSizeOctets: attachmentCount * 1024L,
                InlineResourceCount: 0,
                IsEncrypted: false,
                CarriesUnverifiedSignature: false,
                ContainsUnexpandedTnefPart: false),
            ContentAvailability = StoredEmailContentAvailability.Available,
            RemoteFlags = new RemoteEmailFlagSnapshot(
                FirstJuly,
                isRemotelySeen,
                IsAnswered: false,
                IsFlagged: false,
                IsDraft: false,
                IsDeleted: false,
                RemoteEmailKeywords.None),
        };

    private Task<Results<Ok<ClientMailTimelineResponse>, ProblemHttpResult>> ReadAsync(
        string? account = null,
        string? folder = null,
        bool? unread = null,
        string? sort = null,
        string? order = null,
        string? direction = null,
        int? pageSize = null,
        string? cursor = null)
    {
        this.timeline
            .ReadPageAsync(
                Arg.Any<EmailTimelineFilter>(),
                Arg.Any<EmailTimelinePosition?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<EmailSummary>>([SyntheticListedEmail(), SyntheticListedEmail()]));

        return ClientMailTimelineEndpoint.ReadTimelineAsync(
            account,
            folder,
            includeJunk: null,
            unread,
            flagged: null,
            hasAttachments: null,
            receivedOnOrAfter: null,
            receivedBefore: null,
            sort,
            order,
            direction,
            pageSize,
            cursor,
            this.Browser(),
            TestContext.Current.CancellationToken);
    }

    /// <summary>Builds the use case behind the route over the real scope resolution, with storage and the instruments stood in for.</summary>
    private MailTimelineBrowser Browser()
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.OwnedAccounts.Returns([SyntheticServedAccount.Of(MailAccountId.Create("work"))]);

        var readTelemetry = Substitute.For<IMailboxReadTelemetry>();
        readTelemetry.BeginRead(Arg.Any<MailboxReadOperation>(), Arg.Any<CancellationToken>())
            .Returns(Substitute.For<IMailboxReadScope>());

        var previews = Substitute.For<IStoredEmailPreviewReader>();
        previews.ReadPreviewsAsync(Arg.Any<IReadOnlyList<StoredEmailId>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<StoredEmailId, string>>(
                new Dictionary<StoredEmailId, string>()));

        return new MailTimelineBrowser(
            this.timeline,
            previews,
            new MailboxScopeResolver(
                catalog,
                StubMailFolderParticipation.Nothing,
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            SensitiveContentEgressGuards.Inactive(),
            readTelemetry,
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));
    }
}
