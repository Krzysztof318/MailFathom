// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.BrowseThread;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Emails.Threads;
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

/// <summary>Covers what the conversation route accepts, what it refuses, and what it puts on the wire.</summary>
/// <remarks>
/// The conversation itself — its order, its bounds, its participants and its cursors — is covered where it is decided.
/// What is asserted here is the transport: that an identifier naming no conversation this caller may see is a
/// <c>404</c> rather than an empty document, that each cursor refusal says which one it is, and that a message reaches
/// the wire as the row a screen draws from.
/// </remarks>
public sealed class ClientMailThreadEndpointTests
{
    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly EmailThreadId Conversation =
        EmailThreadId.Create(new Guid("11111111-1111-1111-1111-111111111111"));

    private static readonly EmailThreadId OtherConversation =
        EmailThreadId.Create(new Guid("22222222-2222-2222-2222-222222222222"));

    private readonly IEmailThreadReader threadReader = Substitute.For<IEmailThreadReader>();

    private readonly IStoredEmailSummaryReader summaryReader = Substitute.For<IStoredEmailSummaryReader>();

    /// <summary>The path a client appends to the address it was configured with, pinned because the client composes it from a constant of its own.</summary>
    [Fact]
    public void MailThreadRoute_IsThePathAClientComposes() =>
        Assert.Equal("/threads/{threadId:guid}", ClientMailThreadEndpoint.MailThreadRoute);

    /// <summary>A conversation the caller may see arrives as one document, header and messages together.</summary>
    [Fact]
    public async Task ReadThreadAsync_AConversationThisCallerMaySee_AnswersWithTheDocument()
    {
        // Arrange
        var messages = this.Holding(2);

        // Act
        var result = await this.ReadAsync();

        // Assert
        var page = Assert.IsType<Ok<ClientMailThreadResponse>>(result.Result).Value;

        Assert.NotNull(page);
        Assert.Equal(Conversation.Value, page.ThreadId);
        Assert.Equal(
            messages.Select(message => message.StoredEmailId.Value),
            page.Messages.Select(message => message.Email.Id));
        Assert.Equal(2, page.MessageCount);
        Assert.Equal(["sender@example.test"], page.Participants.Select(participant => participant.Address));
    }

    /// <summary>A conversation nobody holds and one this owner may not see answer identically, so neither discloses the other.</summary>
    [Fact]
    public async Task ReadThreadAsync_AnIdentifierNamingNoConversationThisCallerMaySee_IsNotFound()
    {
        // Arrange
        this.Holding(0);

        // Act
        var result = await this.ReadAsync();

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    /// <summary>An empty identifier names no conversation this system ever issued, and answering it as one would be a refusal of its own.</summary>
    [Fact]
    public async Task ReadThreadAsync_AnEmptyIdentifier_IsNotFound()
    {
        // Act
        var result = await this.ReadAsync(threadId: Guid.Empty);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    /// <summary>A cursor this deployment never issued is refused rather than read as the beginning of the conversation.</summary>
    [Fact]
    public async Task ReadThreadAsync_ACursorThisDeploymentNeverIssued_IsRefused()
    {
        // Arrange
        this.Holding(2);

        // Act
        var result = await this.ReadAsync(cursor: "not-a-cursor");

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>A cursor from another conversation and one whose message has left this one are two mistakes with two repairs.</summary>
    [Fact]
    public async Task ReadThreadAsync_ACursorIssuedForAnotherConversation_IsRefused()
    {
        // Arrange
        this.Holding(2);

        var elsewhere = EmailThreadCursor
            .After(StoredEmailId.Create(Guid.CreateVersion7()), EmailThreadCursor.FingerprintOf(OtherConversation))
            .Encode();

        // Act
        var result = await this.ReadAsync(cursor: elsewhere);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>Answering a boundary that has left the conversation with its first page would read as the thread jumping to the top.</summary>
    [Fact]
    public async Task ReadThreadAsync_ACursorWhoseMessageTheConversationNoLongerShows_IsRefused()
    {
        // Arrange
        this.Holding(2);

        var gone = EmailThreadCursor
            .After(StoredEmailId.Create(Guid.CreateVersion7()), EmailThreadCursor.FingerprintOf(Conversation))
            .Encode();

        // Act
        var result = await this.ReadAsync(cursor: gone);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>A page size outside the range is refused rather than clamped, so a screen learns the bound it asked past.</summary>
    [Fact]
    public async Task ReadThreadAsync_APageSizeOutsideTheRange_IsRefused()
    {
        // Arrange
        this.Holding(2);

        // Act
        var result = await this.ReadAsync(pageSize: MailboxQueryPageSize.MaximumValue + 1);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>The header describes the whole conversation, so a client draws it from the first page and keeps it.</summary>
    [Fact]
    public void For_AConversationCutAtBothItsBounds_CarriesWhatWasCutAndTheCursorThatContinuesIt()
    {
        // Arrange
        var email = SyntheticListedEmail();
        var thread = new BrowsedThread(
            Conversation,
            [new BrowsedThreadEmail(email, Position: 3, AnsweredStoredEmailId: null, Contribution: "what I added")],
            [new ThreadParticipant("anna@example.test", "Anna", MessageCount: 4)],
            MessageCount: 500,
            MoreMessagesNotAssembled: true,
            MoreParticipantsNotNamed: true,
            NextCursor: "after",
            PageSize: 25);

        // Act
        var response = ClientMailThreadResponse.For(thread);

        // Assert
        Assert.Equal(500, response.MessageCount);
        Assert.True(response.MoreMessagesNotAssembled);
        Assert.True(response.MoreParticipantsNotNamed);
        Assert.Equal("after", response.NextCursor);
        Assert.Equal(25, response.PageSize);

        var participant = Assert.Single(response.Participants);

        Assert.Equal("anna@example.test", participant.Address);
        Assert.Equal("Anna", participant.DisplayName);
        Assert.Equal(4, participant.MessageCount);
    }

    /// <summary>A message reaches the wire as a list row plus where it sits, and what it added is that row's own preview.</summary>
    [Fact]
    public void For_AMessageAnsweringAnother_CarriesItsPlaceItsAncestorAndWhatItAdded()
    {
        // Arrange
        var answered = StoredEmailId.Create(Guid.CreateVersion7());
        var message = new BrowsedThreadEmail(SyntheticListedEmail(), Position: 2, answered, "what I added");

        // Act
        var response = ClientMailThreadEmailResponse.For(message);

        // Assert
        Assert.Equal(2, response.Position);
        Assert.Equal(answered.Value, response.AnsweredId);
        Assert.Equal("what I added", response.Email.Preview);
        Assert.Equal(message.Email.StoredEmailId.Value, response.Email.Id);
    }

    /// <summary>A root of what the caller is shown names no ancestor, which is also the answer for a withheld parent.</summary>
    [Fact]
    public void For_ARootOfWhatIsShown_NamesNoAncestor()
    {
        // Arrange
        var message = new BrowsedThreadEmail(
            SyntheticListedEmail(),
            Position: 0,
            AnsweredStoredEmailId: null,
            Contribution: null);

        // Act
        var response = ClientMailThreadEmailResponse.For(message);

        // Assert
        Assert.Null(response.AnsweredId);
        Assert.Null(response.Email.Preview);
    }

    private static EmailSummary SyntheticListedEmail(Guid? storedEmailId = null) => new()
    {
        StoredEmailId = StoredEmailId.Create(storedEmailId ?? Guid.CreateVersion7()),
        Account = MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work")),
        FolderAlias = MailFolderAlias.Create("INBOX"),
        ThreadId = Conversation,
        Subject = "a subject",
        SentAt = FirstJuly,
        ReceivedAt = FirstJuly,
        SizeOctets = 2048,
        SenderAddress = "sender@example.test",
        ToAddresses = ["someone@example.test"],
        SenderVerification = SenderVerification.NotEstablished,
        SenderAuthenticationEvidence = SenderAuthenticationEvidence.None,
        MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
        Attachments = new StoredEmailAttachmentSummary(
            AttachmentCount: 0,
            TotalSizeOctets: 0,
            InlineResourceCount: 0,
            IsEncrypted: false,
            CarriesUnverifiedSignature: false,
            ContainsUnexpandedTnefPart: false),
        ContentAvailability = StoredEmailContentAvailability.Available,
        RemoteFlags = new RemoteEmailFlagSnapshot(
            FirstJuly,
            IsSeen: false,
            IsAnswered: false,
            IsFlagged: false,
            IsDraft: false,
            IsDeleted: false,
            RemoteEmailKeywords.None),
    };

    /// <summary>Arranges a conversation of the given length, held by both the membership read and the summary read.</summary>
    private EmailSummary[] Holding(int length)
    {
        var summaries = Enumerable
            .Range(1, length)
            .Select(ordinal => SyntheticListedEmail(new Guid($"00000000-0000-0000-0000-{ordinal:D12}")))
            .ToArray();

        this.threadReader
            .ReadEmailsAsync(Arg.Any<EmailThreadId>(), Arg.Any<MailboxScope>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ThreadedEmailSummary>>(
            [
                .. summaries.Select(summary => new ThreadedEmailSummary
                {
                    StoredEmailId = summary.StoredEmailId,
                    AccountId = summary.AccountId,
                    FolderAlias = summary.FolderAlias,
                    Subject = summary.Subject,
                    SentAt = summary.SentAt,
                    SenderAddress = summary.SenderAddress,
                }),
            ]));

        this.summaryReader
            .ReadSummariesAsync(Arg.Any<IReadOnlyList<StoredEmailId>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<StoredEmailId, EmailSummary>>(
                summaries.ToDictionary(summary => summary.StoredEmailId)));

        return summaries;
    }

    private Task<Results<Ok<ClientMailThreadResponse>, NotFound, ProblemHttpResult>> ReadAsync(
        Guid? threadId = null,
        int? pageSize = null,
        string? cursor = null) =>
        ClientMailThreadEndpoint.ReadThreadAsync(
            threadId ?? Conversation.Value,
            pageSize,
            cursor,
            this.Browser(),
            TestContext.Current.CancellationToken);

    /// <summary>Builds the use case behind the route over the real scope resolution, with storage and the instruments stood in for.</summary>
    private MailThreadBrowser Browser()
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

        return new MailThreadBrowser(
            this.threadReader,
            this.summaryReader,
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
