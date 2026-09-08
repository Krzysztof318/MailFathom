// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.BrowseThread;
using MailFathom.Application.Emails.Enrichment;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Emails.Threads;
using MailFathom.Application.Observability;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
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

    private static readonly Guid Message = new("33333333-3333-3333-3333-333333333333");

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

    /// <summary>A conversation nobody holds and one this user may not see answer identically, so neither discloses the other.</summary>
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

    /// <summary>A page asking for more messages than a content read composes is refused, rather than served half-drawn.</summary>
    /// <remarks>
    /// The ceiling is the content read's own rather than one invented here, so a conversation drawn out costs exactly
    /// what reading that many messages costs. A longer correspondence is read on with the cursor.
    /// </remarks>
    [Fact]
    public async Task ReadThreadAsync_APageLargerThanAContentReadComposes_IsRefused()
    {
        // Arrange
        this.Holding(2);

        // Act
        var result = await this.ReadAsync(pageSize: GetEmailContentRequest.MaximumEmails + 1, content: true);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>That same page is served where the caller asked for a list of the messages rather than for the messages.</summary>
    [Fact]
    public async Task ReadThreadAsync_APageLargerThanAContentReadComposes_IsServedWhereTheMessagesAreNotDrawn()
    {
        // Arrange
        this.Holding(2);

        // Act
        var result = await this.ReadAsync(pageSize: GetEmailContentRequest.MaximumEmails + 1);

        // Assert
        Assert.IsType<Ok<ClientMailThreadResponse>>(result.Result);
    }

    /// <summary>A page of zero is refused in the words of the read it was asked for, rather than in the general range.</summary>
    /// <remarks>
    /// The general bound would refuse this too, and would name one to a hundred while the request that was made holds
    /// one to ten. A caller acting on that refusal would ask again for a page this route refuses a second time.
    /// </remarks>
    [Fact]
    public async Task ReadThreadAsync_ADrawnPageOfNoMessages_IsRefusedInTheRangeADrawnReadHolds()
    {
        // Arrange
        this.Holding(2);

        // Act
        var result = await this.ReadAsync(pageSize: 0, content: true);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Contains(
            $"between 1 and {GetEmailContentRequest.MaximumEmails}",
            refusal.ProblemDetails.Detail,
            StringComparison.Ordinal);
    }

    /// <summary>The whole of a drawn read, from the parameter through the content read and back onto the page it belongs to.</summary>
    /// <remarks>
    /// Every other test of the drawing branch stops at a refusal or asserts the response factories directly, so this is
    /// the one that proves the route reaches the content read at all and pairs what it answered with the right row.
    /// </remarks>
    [Fact]
    public async Task ReadThreadAsync_WithTheMessagesAsked_CarriesEachRowsOwnMessageAndWords()
    {
        // Arrange
        var summaries = this.Holding(2);

        // Act
        var result = await this.ReadDrawingAsync(summaries);

        // Assert
        var page = Assert.IsType<Ok<ClientMailThreadResponse>>(result.Result).Value;

        Assert.NotNull(page);
        Assert.All(page.Messages, message =>
        {
            Assert.NotNull(message.Message);
            Assert.NotNull(message.Body);
            Assert.Equal(message.Email.Id, message.Message.StoredEmailId);
            Assert.Equal(message.Email.Id, message.Body.StoredEmailId);
        });
    }

    /// <summary>A conversation read as a list of messages carries neither the message nor its words, so nothing is paid for what nobody asked for.</summary>
    [Fact]
    public async Task ReadThreadAsync_WithoutTheMessagesAsked_CarriesNeitherTheMessageNorItsWords()
    {
        // Arrange
        this.Holding(2);

        // Act
        var result = await this.ReadAsync();

        // Assert
        var page = Assert.IsType<Ok<ClientMailThreadResponse>>(result.Result).Value;

        Assert.NotNull(page);
        Assert.All(page.Messages, message =>
        {
            Assert.Null(message.Message);
            Assert.Null(message.Body);
        });
    }

    /// <summary>A drawn conversation carries each message as the two message routes answer for it, which is what makes it one request.</summary>
    [Fact]
    public void For_AMessageTheReadOpened_CarriesWhatAReadingPaneDrawsAndWhatTheMessageSays()
    {
        // Arrange
        var email = SyntheticListedEmail(Message);
        var message = new BrowsedThreadEmail(email, Position: 0, AnsweredStoredEmailId: null, "what I added", Enrichment: null);

        // Act
        var response = ClientMailThreadEmailResponse.For(message, threadMessageCount: 1, drawn: DrawnMessage());

        // Assert
        Assert.NotNull(response.Message);
        Assert.NotNull(response.Body);
        Assert.Equal(Message, response.Message.StoredEmailId);
        Assert.Equal("Quarterly invoice", response.Message.Headers.Subject);
        Assert.Equal("Just words.", response.Body.PlainText?.Text);
        Assert.Equal(Message, response.Email.Id);
    }

    /// <summary>A message the read could not open is published as its row alone, so the correspondence is drawn with a gap rather than short.</summary>
    [Fact]
    public void For_AConversationOneOfWhoseMessagesCouldNotBeOpened_CarriesThatMessageAsItsRowAlone()
    {
        // Arrange
        var opened = SyntheticListedEmail(Message);
        var thread = new BrowsedThread(
            Conversation,
            [
                new BrowsedThreadEmail(opened, Position: 0, AnsweredStoredEmailId: null, Contribution: null, Enrichment: null),
                new BrowsedThreadEmail(SyntheticListedEmail(), Position: 1, AnsweredStoredEmailId: null, Contribution: null, Enrichment: null),
            ],
            [],
            MessageCount: 2,
            MoreMessagesNotAssembled: false,
            MoreParticipantsNotNamed: false,
            NextCursor: null,
            PageSize: 10);

        // Act
        var response = ClientMailThreadResponse.For(thread, [DrawnMessage()]);

        // Assert
        Assert.NotNull(response.Messages[0].Message);
        Assert.NotNull(response.Messages[0].Body);
        Assert.Null(response.Messages[1].Message);
        Assert.Null(response.Messages[1].Body);
    }

    /// <summary>The read a drawn conversation makes asks for the reduced tree and declines both of the per-message asks.</summary>
    /// <remarks>
    /// The sender's own markup is a surface a reader opens one message at a time, and a download link is a bearer
    /// credential this answer would otherwise mint once per message of a conversation.
    /// </remarks>
    [Fact]
    public void ContentRequestFor_TheMessagesOfAConversation_AsksForTheDocumentAndForNeitherMarkupNorALink()
    {
        // Act
        var request = ClientMailThreadEndpoint.ContentRequestFor([StoredEmailId.Create(Message)]);

        // Assert
        Assert.True(request.IncludeMailDocument);
        Assert.False(request.IncludeSelfContainedHtml);
        Assert.False(request.IncludeAttachmentDownloadLinks);
        Assert.False(request.RetainRemoteImageReferences);
    }

    /// <summary>The header describes the whole conversation, so a client draws it from the first page and keeps it.</summary>
    [Fact]
    public void For_AConversationCutAtBothItsBounds_CarriesWhatWasCutAndTheCursorThatContinuesIt()
    {
        // Arrange
        var email = SyntheticListedEmail();
        var thread = new BrowsedThread(
            Conversation,
            [new BrowsedThreadEmail(email, Position: 3, AnsweredStoredEmailId: null, Contribution: "what I added", Enrichment: null)],
            [new ThreadParticipant("anna@example.test", "Anna", MessageCount: 4)],
            MessageCount: 500,
            MoreMessagesNotAssembled: true,
            MoreParticipantsNotNamed: true,
            NextCursor: "after",
            PageSize: 25);

        // Act
        var response = ClientMailThreadResponse.For(thread, []);

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
        var message = new BrowsedThreadEmail(SyntheticListedEmail(), Position: 2, answered, "what I added", Enrichment: null);

        // Act
        var response = ClientMailThreadEmailResponse.For(message, threadMessageCount: 4, drawn: null);

        // Assert
        Assert.Equal(2, response.Position);
        Assert.Equal(answered.Value, response.AnsweredId);
        Assert.Equal("what I added", response.Email.Preview);
        Assert.Equal(message.Email.StoredEmailId.Value, response.Email.Id);
        Assert.Equal(4, response.Email.ThreadMessageCount);
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
            Contribution: null,
            Enrichment: null);

        // Act
        var response = ClientMailThreadEmailResponse.For(message, threadMessageCount: 1, drawn: null);

        // Assert
        Assert.Null(response.AnsweredId);
        Assert.Null(response.Email.Preview);
    }

    /// <summary>One message as a content read opened it, which is what a drawn conversation carries beside each row.</summary>
    private static ReadEmailContent DrawnMessage() => new()
    {
        StoredEmailId = StoredEmailId.Create(Message),
        AccountId = MailAccountId.Create("work"),
        FolderAlias = MailFolderAlias.Create("INBOX"),
        SizeOctets = 2048,
        Headers = new EmailContentHeaders(
            "Quarterly invoice",
            SentAt: FirstJuly,
            ReceivedAt: FirstJuly,
            [Participant(EmailAddressRole.From, "Billing", "sender@example.test")],
            EmailThreadReferences.Create("abc@example.test", inReplyTo: null, references: null)),
        Body = EmailContentBody.Readable(
            new EmailBodyRepresentation("Just words.", 11, EmailBodyTruncation.None),
            sanitizedHtml: null,
            document: null,
            selfContainedHtml: null,
            new EmailBodyForms(PlainText: true, Html: false)),
        AttachmentSummary = new StoredEmailAttachmentSummary(
            AttachmentCount: 0,
            TotalSizeOctets: 0,
            InlineResourceCount: 0,
            IsEncrypted: false,
            CarriesUnverifiedSignature: false,
            ContainsUnexpandedTnefPart: false),
        Attachments = [],
        RemoteFlags = RemoteEmailFlagSnapshot.NeverObserved,
        SenderVerification = new SenderVerification
        {
            AuthorAuthentication = AuthorAuthenticationOutcome.Authenticated,
            DeploymentTrust = SenderTrustLevel.Trusted,
        },
        SenderAuthenticationEvidence = SenderAuthenticationEvidence.None,
        MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
        Thread = new ReadEmailThread
        {
            ThreadId = Conversation,
            EmailCount = 2,
            MoreEmailsNotNamed = false,
            OtherEmails = [],
        },
    };

    private static EmailParticipant Participant(EmailAddressRole role, string? displayName, string address) =>
        new(role, EmailAddress.TryCreate(displayName, address, out var parsed) ? parsed : default);

    private static EmailSummary SyntheticListedEmail(Guid? storedEmailId = null) => new()
    {
        StoredEmailId = StoredEmailId.Create(storedEmailId ?? Guid.CreateVersion7()),
        Account = MailAccountIdentity.Create(SyntheticMailUser.Deployment, MailAccountId.Create("work")),
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
        string? cursor = null,
        bool? content = null) =>
        ClientMailThreadEndpoint.ReadThreadAsync(
            threadId ?? Conversation.Value,
            pageSize,
            cursor,
            content,
            this.Browser(),
            ReadingNothing(),
            TestContext.Current.CancellationToken);

    /// <summary>Reads the conversation with its messages drawn, over a content read that answers for the summaries given.</summary>
    private Task<Results<Ok<ClientMailThreadResponse>, NotFound, ProblemHttpResult>> ReadDrawingAsync(
        EmailSummary[] summaries) =>
        ClientMailThreadEndpoint.ReadThreadAsync(
            Conversation.Value,
            null,
            null,
            content: true,
            this.Browser(),
            this.ReadingDrawnMessages(summaries),
            TestContext.Current.CancellationToken);

    /// <summary>A content read that answers, so the route's own composition is exercised rather than only its refusals.</summary>
    /// <remarks>
    /// The folder every summary sits in is mapped here, which is what makes the mail readable at all: a read scoped
    /// over an unmapped alias answers with nothing, and a test arranged that way would pass while proving the opposite
    /// of what it claims.
    /// </remarks>
    private EmailContentReader ReadingDrawnMessages(EmailSummary[] summaries)
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.OwnedAccounts.Returns([SyntheticServedAccount.Of(MailAccountId.Create("work"))]);

        var readTelemetry = Substitute.For<IMailboxReadTelemetry>();
        readTelemetry.BeginRead(Arg.Any<MailboxReadOperation>(), Arg.Any<CancellationToken>())
            .Returns(Substitute.For<IMailboxReadScope>());

        foreach (var summary in summaries)
        {
            this.summaryReader.FindAsync(summary.StoredEmailId, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<EmailSummary?>(summary));
        }

        var rawMime = "From: sender@example.test\r\n\r\nBody"u8.ToArray();
        var stored = new StoredEmailContent(rawMime, rawMime.Length, SHA256.HashData(rawMime));

        var contentStore = Substitute.For<IEmailContentStore>();
        contentStore.FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoredEmailContent?>(stored));

        var renderer = Substitute.For<IEmailContentRenderer>();
        renderer.RenderAsync(
                Arg.Any<StoredEmailContent>(),
                Arg.Any<EmailContentRenderingBounds>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailContentRenderingResult.Rendered(new EmailContentRendering(
                new EmailContentHeaders("a subject", SentAt: null, ReceivedAt: null, [], EmailThreadReferences.None),
                new EmailBodyRepresentation("Body", 4, EmailBodyTruncation.None),
                SanitizedHtmlBody: null,
                new EmailBodyForms(PlainText: true, Html: false),
                BodyIsEncrypted: false,
                EmailAttachmentSummary.Create(
                    [],
                    inlineResourceCount: 0,
                    isEncrypted: false,
                    carriesUnverifiedSignature: false,
                    containsUnexpandedTnefPart: false),
                []))));

        return new EmailContentReader(
            this.summaryReader,
            this.threadReader,
            contentStore,
            renderer,
            Substitute.For<IEmailContentRepairRequestStore>(),
            new MailboxScopeResolver(
                catalog,
                StubMailFolderParticipation.Mapping(
                    new MailFolderIdentity(MailAccountId.Create("work"), MailFolderAlias.Create("INBOX"))),
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            Substitute.For<IAttachmentDownloadLinkIssuer>(),
            SensitiveContentEgressGuards.Inactive(),
            new EmailContentReadOptions(),
            readTelemetry,
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));
    }

    /// <summary>A content read standing in for the one behind the route, which the test reaching it never lets answer.</summary>
    /// <remarks>
    /// Every test given this one is a refusal taken before the read runs, so a read that answered would prove nothing
    /// they claim. <see cref="ReadingDrawnMessages" /> is the one that answers, and it is what the drawn read itself is
    /// asserted through.
    /// </remarks>
    private static EmailContentReader ReadingNothing()
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.OwnedAccounts.Returns([SyntheticServedAccount.Of(MailAccountId.Create("work"))]);

        var readTelemetry = Substitute.For<IMailboxReadTelemetry>();
        readTelemetry.BeginRead(Arg.Any<MailboxReadOperation>(), Arg.Any<CancellationToken>())
            .Returns(Substitute.For<IMailboxReadScope>());

        return new EmailContentReader(
            Substitute.For<IStoredEmailSummaryReader>(),
            Substitute.For<IEmailThreadReader>(),
            Substitute.For<IEmailContentStore>(),
            Substitute.For<IEmailContentRenderer>(),
            Substitute.For<IEmailContentRepairRequestStore>(),
            new MailboxScopeResolver(
                catalog,
                StubMailFolderParticipation.Nothing,
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            Substitute.For<IAttachmentDownloadLinkIssuer>(),
            SensitiveContentEgressGuards.Inactive(),
            new EmailContentReadOptions(),
            readTelemetry,
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));
    }

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

        var enrichments = Substitute.For<IStoredEmailEnrichmentReader>();
        enrichments.ReadEnrichmentsAsync(Arg.Any<IReadOnlyList<StoredEmailId>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<StoredEmailId, EmailEnrichment>>(
                new Dictionary<StoredEmailId, EmailEnrichment>()));

        return new MailThreadBrowser(
            this.threadReader,
            this.summaryReader,
            previews,
            enrichments,
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
