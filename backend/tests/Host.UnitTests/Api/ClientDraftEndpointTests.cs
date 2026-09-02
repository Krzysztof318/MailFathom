// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Screening;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the draft routes decide before the use case behind them is reached, and what an upload becomes.</summary>
/// <remarks>
/// <para>
/// Writing, revising, filing, and sending a draft are the application's, and are covered where those live. What is
/// left here is the part a request decides: an identifier that names no draft, text no account is named by, and an
/// upload — which is this surface's own shape, because the octets are the request body and what the file declares
/// itself to be is a header rather than a field somebody sent.
/// </para>
/// <para>
/// Every refusal is asserted for its status and for carrying nothing of what triggered it. A draft is a message
/// somebody is writing, so an answer echoing a rejected recipient or a rejected file name would put that text in a
/// place the request never asked for it.
/// </para>
/// </remarks>
public sealed class ClientDraftEndpointTests
{
    private static readonly DateTimeOffset Moment = new(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId Work = MailAccountId.Create("work");

    /// <summary>The paths a client appends to the address it was configured with, pinned because it composes them from constants of its own.</summary>
    [Fact]
    public void DraftRoutes_ArePathsAClientComposes()
    {
        // Arrange
        // Act
        // Assert
        Assert.Equal("/drafts", ClientDraftEndpoints.DraftsRoute);
        Assert.Equal("/drafts/{draftId:guid}", ClientDraftEndpoints.DraftRoute);
        Assert.Equal("/drafts/{draftId:guid}/send", ClientDraftEndpoints.DraftSendRoute);
        Assert.Equal("/drafts/{draftId:guid}/attachments", ClientDraftEndpoints.DraftAttachmentsRoute);
        Assert.Equal(
            "/drafts/{draftId:guid}/attachments/{attachmentId:guid}",
            ClientDraftEndpoints.DraftAttachmentRoute);
    }

    /// <summary>The three published answer values, pinned because a client sends one of them as written text.</summary>
    [Fact]
    public void AnswerValues_AreTheOnesAClientSends()
    {
        // Arrange
        // Act
        // Assert
        Assert.Equal("senderOnly", ClientDraftEndpoints.SenderOnlyAnswer);
        Assert.Equal("everyone", ClientDraftEndpoints.EveryoneAnswer);
        Assert.Equal("forward", ClientDraftEndpoints.ForwardAnswer);
    }

    /// <summary>Text no account of this system is spelled with is refused as a request, not carried into the reading.</summary>
    [Fact]
    public async Task ReadDraftsAsync_AnAccountNoNameIsSpelledWith_IsRefusedWithoutReading()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();

        // Act
        var result = await ClientDraftEndpoints.ReadDraftsAsync(
            new string('a', 512),
            DirectoryOver(drafts),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.DoesNotContain("aaaa", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>An identifier naming nothing answers as a draft this owner does not hold, which is what one nobody holds answers.</summary>
    [Fact]
    public async Task ReadDraftAsync_AnIdentifierNamingNoDraft_AnswersAsOneThisOwnerDoesNotHold()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();

        // Act
        var result = await ClientDraftEndpoints.ReadDraftAsync(
            Guid.Empty,
            DirectoryOver(drafts),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    /// <summary>A draft this owner holds is opened with the words its stored message carries.</summary>
    [Fact]
    public async Task ReadDraftAsync_ADraftThisOwnerHolds_AnswersWithTheDraftAndItsText()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();
        var contents = new InMemoryMailDraftContentStore();
        var draft = await OpenAsync(drafts, SyntheticMailOwner.Deployment);

        await contents.SaveMailDraftContentAsync(
            Substitute.For<IPersistenceSession>(),
            draft.Id,
            PlacedEmailContent.InDatabase(Encoding.ASCII.GetBytes("Subject: a draft\r\n\r\nHello.").AsMemory()),
            TestContext.Current.CancellationToken);

        // Act
        var result = await ClientDraftEndpoints.ReadDraftAsync(
            draft.Id.Value,
            DirectoryOver(drafts, contents),
            TestContext.Current.CancellationToken);

        // Assert
        var reading = Assert.IsType<Ok<ClientDraftReadingResponse>>(result.Result);
        Assert.Equal(draft.Id.Value, reading.Value!.Draft.DraftId);
        Assert.Equal("Hello.", reading.Value.PlainTextBody);
    }

    /// <summary>A draft another owner holds is opened as one nobody holds, so a refusal says nothing about who else writes mail here.</summary>
    [Fact]
    public async Task ReadDraftAsync_ADraftAnotherOwnerHolds_AnswersAsOneNobodyHolds()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();
        var contents = new InMemoryMailDraftContentStore();
        var theirs = await OpenAsync(drafts, SyntheticMailOwner.Another);

        await contents.SaveMailDraftContentAsync(
            Substitute.For<IPersistenceSession>(),
            theirs.Id,
            PlacedEmailContent.InDatabase(Encoding.ASCII.GetBytes("Subject: theirs\r\n\r\nPrivate.").AsMemory()),
            TestContext.Current.CancellationToken);

        // Act
        var result = await ClientDraftEndpoints.ReadDraftAsync(
            theirs.Id.Value,
            DirectoryOver(drafts, contents),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    /// <summary>An upload with no body attaches nothing, and says so rather than staging an empty file.</summary>
    [Fact]
    public async Task StageAttachmentAsync_ARequestCarryingNoOctets_IsRefusedAndStagesNothing()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();
        var draft = await OpenAsync(drafts, SyntheticMailOwner.Deployment);

        // Act
        var result = await ClientDraftEndpoints.StageAttachmentAsync(
            draft.Id.Value,
            "empty.txt",
            AttachmentsOver(drafts),
            Upload([], "text/plain"),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(drafts.Peek(draft.Id)!.Attachments);
    }

    /// <summary>An upload stages the request's own octets, described by what the request declared them to be.</summary>
    [Fact]
    public async Task StageAttachmentAsync_AFileDeclaringItsTypeWithParameters_KeepsTheTypeWithoutThem()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();
        var draft = await OpenAsync(drafts, SyntheticMailOwner.Deployment);

        // Act
        var result = await ClientDraftEndpoints.StageAttachmentAsync(
            draft.Id.Value,
            "note.txt",
            AttachmentsOver(drafts),
            Upload(Encoding.UTF8.GetBytes("Remember the milk."), "text/plain; charset=utf-8"),
            TestContext.Current.CancellationToken);

        // Assert
        var staged = Assert.IsType<Ok<ClientDraftAttachmentResponse>>(result.Result);
        Assert.Equal("note.txt", staged.Value!.FileName);
        Assert.Equal("text/plain", staged.Value.MediaType);
        Assert.Equal("Remember the milk.".Length, staged.Value.SizeOctets);
    }

    /// <summary>A request declaring nothing is read as the general binary type rather than having its octets examined.</summary>
    [Fact]
    public async Task StageAttachmentAsync_AFileDeclaringNoType_IsStagedAsTheGeneralBinaryType()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();
        var draft = await OpenAsync(drafts, SyntheticMailOwner.Deployment);

        // Act
        var result = await ClientDraftEndpoints.StageAttachmentAsync(
            draft.Id.Value,
            "opaque",
            AttachmentsOver(drafts),
            Upload([1, 2, 3], contentType: null),
            TestContext.Current.CancellationToken);

        // Assert
        var staged = Assert.IsType<Ok<ClientDraftAttachmentResponse>>(result.Result);
        Assert.Equal(AttachmentContentResponse.FallbackMediaType, staged.Value!.MediaType);
    }

    /// <summary>A file larger than this deployment composes is refused naming the bound, and echoes none of the file.</summary>
    [Fact]
    public async Task StageAttachmentAsync_AFileLargerThanThisDeploymentComposes_IsRefusedWithoutEchoingIt()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();
        var draft = await OpenAsync(drafts, SyntheticMailOwner.Deployment);

        // Act
        var result = await ClientDraftEndpoints.StageAttachmentAsync(
            draft.Id.Value,
            "huge.bin",
            AttachmentsOver(drafts),
            Upload(new byte[4096], "application/octet-stream"),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.DoesNotContain("huge.bin", refusal.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.Empty(drafts.Peek(draft.Id)!.Attachments);
    }

    /// <summary>An upload against a draft this owner does not hold answers as one nobody holds.</summary>
    [Fact]
    public async Task StageAttachmentAsync_ADraftAnotherOwnerHolds_AnswersAsOneNobodyHolds()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();
        var theirs = await OpenAsync(drafts, SyntheticMailOwner.Another);

        // Act
        var result = await ClientDraftEndpoints.StageAttachmentAsync(
            theirs.Id.Value,
            "note.txt",
            AttachmentsOver(drafts),
            Upload(Encoding.UTF8.GetBytes("Hello."), "text/plain"),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, refusal.StatusCode);
        Assert.Empty(drafts.Peek(theirs.Id)!.Attachments);
    }

    /// <summary>Taking a file off names the file, and a request naming none is refused rather than removing whatever is first.</summary>
    [Fact]
    public async Task UnstageAttachmentAsync_ARequestNamingNoStagedFile_IsRefused()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();
        var draft = await OpenAsync(drafts, SyntheticMailOwner.Deployment);

        // Act
        var result = await ClientDraftEndpoints.UnstageAttachmentAsync(
            draft.Id.Value,
            Guid.Empty,
            AttachmentsOver(drafts),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
    }

    /// <summary>Taking a file off a draft another owner holds answers as one nobody holds, and leaves their file where it is.</summary>
    [Fact]
    public async Task UnstageAttachmentAsync_ADraftAnotherOwnerHolds_AnswersAsOneNobodyHoldsAndRemovesNothing()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();
        var theirs = await OpenAsync(drafts, SyntheticMailOwner.Another);

        var staged = await drafts.StageAttachmentAsync(
            Substitute.For<IPersistenceSession>(),
            theirs.Id,
            new AuthoredEmailAttachment("theirs.txt", "text/plain", new byte[8].AsMemory()),
            Moment,
            TestContext.Current.CancellationToken);

        // Act
        var result = await ClientDraftEndpoints.UnstageAttachmentAsync(
            theirs.Id.Value,
            staged.Id.Value,
            AttachmentsOver(drafts),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status404NotFound, refusal.StatusCode);
        Assert.Equal([staged.Id], drafts.Peek(theirs.Id)!.Attachments.Select(attachment => attachment.Id));
    }

    /// <summary>Taking a file off answers the same whether or not the draft carried it, because the outcome asked for holds either way.</summary>
    [Fact]
    public async Task UnstageAttachmentAsync_AFileTakenOffTwice_AnswersTheSameBothTimes()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();
        var draft = await OpenAsync(drafts, SyntheticMailOwner.Deployment);
        var attachments = AttachmentsOver(drafts);

        var staged = await attachments.StageAsync(
            draft.Id,
            new AuthoredEmailAttachment("note.txt", "text/plain", new byte[8].AsMemory()),
            TestContext.Current.CancellationToken);

        // Act
        var first = await ClientDraftEndpoints.UnstageAttachmentAsync(
            draft.Id.Value,
            staged.Id.Value,
            attachments,
            TestContext.Current.CancellationToken);
        var second = await ClientDraftEndpoints.UnstageAttachmentAsync(
            draft.Id.Value,
            staged.Id.Value,
            attachments,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NoContent>(first.Result);
        Assert.IsType<NoContent>(second.Result);
        Assert.Empty(drafts.Peek(draft.Id)!.Attachments);
    }

    /// <summary>Builds the request an upload arrives as, whose body is the file and whose header is what it declares.</summary>
    private static DefaultHttpContext Upload(byte[] octets, string? contentType)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(octets);
        context.Request.ContentType = contentType;

        return context;
    }

    /// <summary>Builds the reading the routes narrow by owner, for a caller acting for the deployment's owner.</summary>
    private static MailDraftDirectory DirectoryOver(
        InMemoryMailDraftStore drafts,
        InMemoryMailDraftContentStore? contents = null)
    {
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailDraftsWrite);

        return new MailDraftDirectory(
            OwnedMailAccountCatalogs.For(authorization, SyntheticServedAccount.Of(Work)),
            drafts,
            contents ?? new InMemoryMailDraftContentStore(),
            new HeaderReadingOutgoingMailText(),
            authorization);
    }

    /// <summary>Builds the staging the upload route reaches, under bounds small enough for a test to exceed.</summary>
    private static MailDraftAttachments AttachmentsOver(InMemoryMailDraftStore drafts)
    {
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailDraftsWrite);
        var sessions = Substitute.For<IPersistenceSessionFactory>();
        sessions.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var session = Substitute.For<IPersistenceSession>();
            session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);

            return session;
        });

        return new MailDraftAttachments(
            DirectoryOver(drafts),
            drafts,
            new OptimisticConcurrencyRetryPolicy(
                sessions,
                new PersistenceConcurrencyOptions(),
                new FakeTimeProvider(Moment)),
            new OutgoingEmailBounds
            {
                MaxRecipientCount = 16,
                MaxBodyCharacters = 100_000,
                MaxAttachmentCount = 2,
                MaxAttachmentBytes = 2048,
                MaxMessageBytes = 1_000_000,
            },
            authorization,
            new FakeTimeProvider(Moment));
    }

    /// <summary>Writes one draft down for one owner, which is the arrangement every test here starts from.</summary>
    private static Task<MailDraftRecord> OpenAsync(InMemoryMailDraftStore drafts, MailOwnerId owner) =>
        drafts.OpenAsync(
            Substitute.For<IPersistenceSession>(),
            MailAccountIdentity.Create(owner, Work),
            OutgoingEmailRequester.Command("mfctl-4f2a"),
            [],
            "a draft",
            mimeByteLength: 64,
            Moment,
            TestContext.Current.CancellationToken);

    /// <summary>Reads a composed message the way the MIME adapter does, over the trivial messages these tests write.</summary>
    /// <remarks>
    /// A hand-written reader rather than a substitute, because what the opening test asserts is that the words come out
    /// of the stored bytes: a substituted reader would answer whatever the arrangement said and prove nothing about it.
    /// </remarks>
    private sealed class HeaderReadingOutgoingMailText : IOutgoingMailTextReader
    {
        public Task<OutgoingMailText> ReadAsync(ReadOnlyMemory<byte> rawMime, CancellationToken cancellationToken)
        {
            var message = Encoding.ASCII.GetString(rawMime.Span).Split("\r\n\r\n", 2);
            var subject = message[0].StartsWith("Subject: ", StringComparison.Ordinal)
                ? message[0]["Subject: ".Length..]
                : string.Empty;

            return Task.FromResult(new OutgoingMailText(subject, message[1], HtmlBody: null));
        }
    }
}
