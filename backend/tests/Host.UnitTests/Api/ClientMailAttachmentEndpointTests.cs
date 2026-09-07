// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.DownloadAttachment;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Host.Api;
using MailFathom.Host.Security.Endpoints;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the client's own attachment route serves, and everything it refuses in one refusal.</summary>
/// <remarks>
/// The route is the client half of a download: a reader who authenticated follows it, so no capability is minted and no
/// link expires. What is asserted here is that the octets arrive described in the encoding each header defines, that the
/// file is served as something to save rather than something to render, and that every reason there is nothing to serve
/// answers identically — a caller must not learn what became of mail they cannot read by asking about it. A file this
/// deployment screened is the stated exception and is asserted as one, `409` with a code of its own: this reader is
/// already authenticated for this message and this position, so the only thing left to withhold is what is in the
/// file.
/// </remarks>
[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The endpoint under test takes ownership of the opened attachment and disposes it, which is the contract these tests exercise.")]
public sealed class ClientMailAttachmentEndpointTests
{
    /// <summary>The literal a screened deployment in this suite stops at, which never reaches a response.</summary>
    private const string ScreenedMarker = "sk-live-000111222333";

    private static readonly Guid Message = new("44444444-4444-4444-4444-444444444444");

    /// <summary>
    /// The client route meets the same screen the signed link does, because both resolve through one use case. It says
    /// so rather than answering the uniform refusal, and it still says nothing about what was found.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_AttachmentTheScreenStopped_RefusesWithItsOwnCodeAndNamesNothing()
    {
        // Arrange
        var context = new DefaultHttpContext();
        using var egress = ScanningSensitiveContentEgress.Finding(ScreenedMarker, TimeProvider.System);

        // Act
        var result = await ClientMailAttachmentEndpoint.DownloadAsync(
            Message,
            position: 0,
            AttachmentOpening(
                new StubOpenedEmailAttachment("invoice.pdf", "application/pdf", "%PDF-1.7"u8.ToArray()),
                screen: egress.Screen),
            context,
            TestContext.Current.CancellationToken);

        // Assert
        var refused = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status409Conflict, refused.StatusCode);
        Assert.Equal(
            MailFathomErrorCode.AttachmentDownloadScreened.Value,
            refused.ProblemDetails.Extensions[RouteAuthorization.ErrorCodeExtension]);
        Assert.DoesNotContain(ScreenedMarker, refused.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    /// <summary>The path a client appends to the address it was configured with, pinned because the client composes it from a constant of its own.</summary>
    [Fact]
    public void MailAttachmentRoute_IsThePathAClientComposes() =>
        Assert.Equal(
            "/messages/{storedEmailId:guid}/attachments/{position:int}",
            ClientMailAttachmentEndpoint.MailAttachmentRoute);

    /// <summary>The response carries the file's octets and nothing else, described by what the parse measured.</summary>
    [Fact]
    public async Task DownloadAsync_AttachmentOfAReadableMessage_WritesTheFileAndStatesWhatItIs()
    {
        // Arrange
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;

        // Act
        var result = await ClientMailAttachmentEndpoint.DownloadAsync(
            Message,
            position: 0,
            AttachmentOpening(new StubOpenedEmailAttachment("invoice.pdf", "application/pdf", "%PDF-1.7"u8.ToArray())),
            context,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<EmptyHttpResult>(result.Result);
        Assert.Equal("%PDF-1.7"u8.ToArray(), body.ToArray());
        Assert.Equal("application/pdf", context.Response.ContentType);
        Assert.Equal("%PDF-1.7".Length, context.Response.ContentLength);
        Assert.Contains(
            "filename=invoice.pdf",
            context.Response.Headers.ContentDisposition.ToString(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The octets are what a sender attached and they are served from the origin the client itself is served from, so a
    /// message carrying markup must arrive as a file to save rather than as a page the browser would run.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_AttachmentOfAReadableMessage_ServesItAsADownloadRatherThanSomethingToRender()
    {
        // Arrange
        var context = new DefaultHttpContext();
        using var body = new MemoryStream();
        context.Response.Body = body;

        // Act
        await ClientMailAttachmentEndpoint.DownloadAsync(
            Message,
            position: 0,
            AttachmentOpening(new StubOpenedEmailAttachment(
                "page.html",
                "text/html",
                "<script>alert(1)</script>"u8.ToArray())),
            context,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.StartsWith("attachment", context.Response.Headers.ContentDisposition.ToString(), StringComparison.Ordinal);
        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions.ToString());
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    /// <summary>A position the message carries no part at is nothing to serve, and says nothing more than that.</summary>
    [Fact]
    public async Task DownloadAsync_PositionTheMessageCarriesNoPartAt_Refuses()
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act
        var result = await ClientMailAttachmentEndpoint.DownloadAsync(
            Message,
            position: 7,
            AttachmentOpening(attachment: null),
            context,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    /// <summary>
    /// A position no walk of a message ever produces is refused before the mailbox is read at all, because there is
    /// nothing for it to name and asking would be a read performed on a caller's arithmetic.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task DownloadAsync_PositionBelowTheFirstPart_RefusesWithoutOpeningAnything(int position)
    {
        // Arrange
        var context = new DefaultHttpContext();
        var contentStore = Substitute.For<IEmailContentStore>();

        // Act
        var result = await ClientMailAttachmentEndpoint.DownloadAsync(
            Message,
            position,
            AttachmentOpening(attachment: null, contentStore),
            context,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
        await contentStore
            .DidNotReceive()
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>An identifier naming no message at all is the same refusal as a message this user does not hold.</summary>
    [Fact]
    public async Task DownloadAsync_EmptyIdentifier_RefusesWithoutOpeningAnything()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var contentStore = Substitute.For<IEmailContentStore>();

        // Act
        var result = await ClientMailAttachmentEndpoint.DownloadAsync(
            Guid.Empty,
            position: 0,
            AttachmentOpening(attachment: null, contentStore),
            context,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
        await contentStore
            .DidNotReceive()
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Builds the real use case over ports that answer with the attachment a test wants served, or with nothing.</summary>
    /// <remarks>
    /// The use case is a concrete type rather than a port, so it is composed here instead of substituted — and that is
    /// the honest shape as well, because what this endpoint has to get right is how an opened attachment becomes a
    /// response and how an absent one becomes a refusal, and both travel through the real reader either way.
    /// </remarks>
    private static EmailAttachmentDownloadReader AttachmentOpening(
        IOpenedEmailAttachment? attachment,
        IEmailContentStore? contentStore = null,
        SensitiveContentEgressScreen? screen = null)
    {
        var summary = SummaryOf();

        var summaryReader = Substitute.For<IStoredEmailSummaryReader>();
        summaryReader
            .FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EmailSummary?>(summary));

        var resolvedContentStore = contentStore ?? Substitute.For<IEmailContentStore>();
        resolvedContentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoredEmailContent?>(IntactContent()));

        var contentReader = Substitute.For<IEmailAttachmentContentReader>();
        contentReader
            .OpenAsync(Arg.Any<StoredEmailContent>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(attachment is null
                ? OpenedEmailAttachmentResult.NoSuchAttachment()
                : OpenedEmailAttachmentResult.Opened(attachment)));

        var accountCatalog = Substitute.For<ICallerMailAccountCatalog>();
        accountCatalog.OwnedAccounts.Returns([SyntheticServedAccount.Of(summary.AccountId)]);

        var principals = Substitute.For<IAuthorizedPrincipalSource>();
        principals.Current.Returns(AuthorizedPrincipal.Caller("client-key", [MailFathomPermission.MailRead]));

        return new EmailAttachmentDownloadReader(
            summaryReader,
            resolvedContentStore,
            contentReader,
            Substitute.For<IEmailContentRepairRequestStore>(),
            AttachmentTextReading(screening: screen is not null),
            screen ?? ScreensNothing(),
            new MailboxScopeResolver(
                accountCatalog,
                StubMailFolderParticipation.Mapping(
                    new MailFolderIdentity(summary.AccountId, summary.FolderAlias)),
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            new AccessAuthorization(principals));
    }

    private static EmailSummary SummaryOf() => new()
    {
        StoredEmailId = StoredEmailId.Create(Message),
        Account = MailAccountIdentity.Create(SyntheticMailUser.Deployment, MailAccountId.Create("primary")),
        FolderAlias = MailFolderAlias.Create("INBOX"),
        InternetMessageId = "<abc@example.test>",
        Subject = "Quarterly invoice",
        SenderAddress = "billing@example.test",
        ToAddresses = ["reader@example.test"],
        SizeOctets = 1024,
        RemoteFlags = RemoteEmailFlagSnapshot.NeverObserved,
        SenderVerification = SenderVerification.NotEstablished,
        MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
        SenderAuthenticationEvidence = SenderAuthenticationEvidence.None,
        ContentAvailability = StoredEmailContentAvailability.Available,
        Attachments = new StoredEmailAttachmentSummary(
            AttachmentCount: 1,
            TotalSizeOctets: 16,
            InlineResourceCount: 0,
            IsEncrypted: false,
            CarriesUnverifiedSignature: false,
            ContainsUnexpandedTnefPart: false),
    };

    private static StoredEmailContent IntactContent()
    {
        var rawMime = "From: sender@example.test\r\n\r\nBody"u8.ToArray();

        return new StoredEmailContent(rawMime, rawMime.LongLength, SHA256.HashData(rawMime));
    }

    /// <summary>An opened attachment that writes fixed octets, standing in for one parse of a stored message.</summary>
    private sealed class StubOpenedEmailAttachment(string? fileName, string mediaType, byte[] octets)
        : IOpenedEmailAttachment
    {
        public ExtractedEmailAttachment Description { get; } = new(
            fileName is not null && AttachmentFileName.TryNormalize(fileName, out var normalized) ? normalized : null,
            mediaType,
            octets.LongLength);

        public async Task WriteContentToAsync(Stream destination, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(destination);

            await destination.WriteAsync(octets, cancellationToken);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Reads the attachment as one carrying whatever a screened deployment in this suite stops at.</summary>
    private static IAttachmentTextExtractor AttachmentTextReading(bool screening)
    {
        var extractor = Substitute.For<IAttachmentTextExtractor>();
        extractor
            .ExtractTextAsync(Arg.Any<IOpenedEmailAttachment>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(screening
                ? AttachmentTextExtractionResult.Extracted(
                    new ExtractedAttachmentText(ScreenedMarker, PageCount: 1, [], []))
                : AttachmentTextExtractionResult.FormatNotRecognized()));

        return extractor;
    }

    /// <summary>Builds the screen of a deployment that screens nothing, which is what every test but one is arranged with.</summary>
    /// <remarks>
    /// A deployment that <em>does</em> screen is not built here, because building one owns a concurrency bound its
    /// author has to release: <see cref="ScanningSensitiveContentEgress" /> is disposable and a helper handing back
    /// only its screen would drop the instance holding it. The one test that needs it binds it with <c>using</c> and
    /// passes the screen in.
    /// </remarks>
    private static SensitiveContentEgressScreen ScreensNothing() => new(
        FixedSensitiveContentPostures.ScanningNothing(),
        new RecordingSensitiveContentEgressTelemetry(),
        TimeProvider.System);

}
