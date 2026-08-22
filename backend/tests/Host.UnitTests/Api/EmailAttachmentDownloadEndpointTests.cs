// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Transport;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers the one route this process answers without a credential.</summary>
/// <remarks>
/// Two things are asserted throughout. The first is that a refusal says nothing: every reason a download is refused
/// produces the same status and the same body, because a caller holding a capability must not learn from the refusal
/// what became of the mail it points at. The second is that a served file is described in the encoding each header
/// defines, since both the media type and the file name are text a sender wrote.
/// </remarks>
[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The endpoint under test takes ownership of the opened attachment and disposes it, which is the contract these tests exercise.")]
public sealed class EmailAttachmentDownloadEndpointTests
{
    /// <summary>The response carries the attachment's octets and nothing else, described by what the parse measured.</summary>
    [Fact]
    public async Task DownloadAsync_ValidCapability_WritesTheAttachmentAndStatesWhatItIs()
    {
        // Arrange
        var context = RequestToTheRoute();
        var principals = PrincipalsFor(context);
        using var body = new MemoryStream();
        context.Response.Body = body;

        // Act
        var result = await EmailAttachmentDownloadEndpoint.DownloadAsync(
            "capability",
            TicketReaderRedeeming(new AttachmentDownloadTicket(StoredEmailId.Create(Guid.CreateVersion7()), 0)),
            AttachmentOpening(principals, new StubOpenedEmailAttachment("invoice.pdf", "application/pdf", "%PDF-1.7"u8.ToArray())),
            principals,
            context,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<EmptyHttpResult>(result.Result);
        Assert.Equal("%PDF-1.7"u8.ToArray(), body.ToArray());
        Assert.Equal("application/pdf", context.Response.ContentType);
        Assert.Equal("%PDF-1.7".Length, context.Response.ContentLength);
        Assert.Contains("filename=invoice.pdf", context.Response.Headers.ContentDisposition.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Sender-controlled bytes served from the deployment's own origin must never be rendered in place: a message
    /// carrying HTML would otherwise be a scripted page on the address the operator publishes MailFathom at.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ValidCapability_ServesTheFileAsADownloadRatherThanSomethingToRender()
    {
        // Arrange
        var context = RequestToTheRoute();
        var principals = PrincipalsFor(context);
        using var body = new MemoryStream();
        context.Response.Body = body;

        // Act
        await EmailAttachmentDownloadEndpoint.DownloadAsync(
            "capability",
            TicketReaderRedeeming(new AttachmentDownloadTicket(StoredEmailId.Create(Guid.CreateVersion7()), 0)),
            AttachmentOpening(principals, new StubOpenedEmailAttachment(
                "page.html",
                "text/html",
                "<script>alert(1)</script>"u8.ToArray())),
            principals,
            context,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.StartsWith("attachment", context.Response.Headers.ContentDisposition.ToString(), StringComparison.Ordinal);
        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions.ToString());
    }

    /// <summary>
    /// The response is mail content on an ordinary cacheable `GET`, and the deployments this route is documented for
    /// put a reverse proxy in front of it. An intermediary applying a default freshness lifetime would keep serving the
    /// file for that URL after the capability expired, which takes the expiry out of the revocation model it is the
    /// whole of and copies the octets somewhere MailFathom does not control.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_ValidCapability_ForbidsAnIntermediaryFromStoringTheResponse()
    {
        // Arrange
        var context = RequestToTheRoute();
        var principals = PrincipalsFor(context);
        using var body = new MemoryStream();
        context.Response.Body = body;

        // Act
        await EmailAttachmentDownloadEndpoint.DownloadAsync(
            "capability",
            TicketReaderRedeeming(new AttachmentDownloadTicket(StoredEmailId.Create(Guid.CreateVersion7()), 0)),
            AttachmentOpening(principals, new StubOpenedEmailAttachment("invoice.pdf", "application/pdf", "%PDF-1.7"u8.ToArray())),
            principals,
            context,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    /// <summary>
    /// A file name is text a sender chose, so it reaches the header through the type that encodes it rather than by
    /// being concatenated into one. A name that could otherwise close the quoting or open a second header must not.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_AttachmentNamedWithHeaderSyntax_EncodesTheNameRatherThanEmittingIt()
    {
        // Arrange
        var context = RequestToTheRoute();
        var principals = PrincipalsFor(context);
        using var body = new MemoryStream();
        context.Response.Body = body;

        // Act
        await EmailAttachmentDownloadEndpoint.DownloadAsync(
            "capability",
            TicketReaderRedeeming(new AttachmentDownloadTicket(StoredEmailId.Create(Guid.CreateVersion7()), 0)),
            AttachmentOpening(principals, new StubOpenedEmailAttachment(
                "faktura \"żółć\"; x=1.pdf",
                "application/pdf",
                "bytes"u8.ToArray())),
            principals,
            context,
            TestContext.Current.CancellationToken);

        // Assert
        var disposition = context.Response.Headers.ContentDisposition.ToString();
        Assert.DoesNotContain("\r", disposition, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", disposition, StringComparison.Ordinal);
        Assert.Contains("filename*=", disposition, StringComparison.Ordinal);
    }

    /// <summary>A media type the sender wrote is parsed before it is echoed; one that is not a media type is served as opaque bytes.</summary>
    [Theory]
    [InlineData("application/pdf", "application/pdf")]
    [InlineData("not a media type at all", "application/octet-stream")]
    [InlineData("text/plain\r\nX-Injected: 1", "application/octet-stream")]
    public async Task DownloadAsync_MediaTypeTheSenderDeclared_IsEchoedOnlyWhenItParses(
        string declared,
        string expected)
    {
        // Arrange
        var context = RequestToTheRoute();
        var principals = PrincipalsFor(context);
        using var body = new MemoryStream();
        context.Response.Body = body;

        // Act
        await EmailAttachmentDownloadEndpoint.DownloadAsync(
            "capability",
            TicketReaderRedeeming(new AttachmentDownloadTicket(StoredEmailId.Create(Guid.CreateVersion7()), 0)),
            AttachmentOpening(principals, new StubOpenedEmailAttachment("file.bin", declared, "bytes"u8.ToArray())),
            principals,
            context,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(expected, context.Response.ContentType);
    }

    /// <summary>An unnamed part is served without a name rather than under one MailFathom invented.</summary>
    [Fact]
    public async Task DownloadAsync_UnnamedAttachment_StatesTheDispositionWithoutAFileName()
    {
        // Arrange
        var context = RequestToTheRoute();
        var principals = PrincipalsFor(context);
        using var body = new MemoryStream();
        context.Response.Body = body;

        // Act
        await EmailAttachmentDownloadEndpoint.DownloadAsync(
            "capability",
            TicketReaderRedeeming(new AttachmentDownloadTicket(StoredEmailId.Create(Guid.CreateVersion7()), 0)),
            AttachmentOpening(principals, new StubOpenedEmailAttachment(fileName: null, "image/png", "png"u8.ToArray())),
            principals,
            context,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("attachment", context.Response.Headers.ContentDisposition.ToString());
    }

    /// <summary>
    /// A capability that does not verify and one whose attachment cannot be served are the same answer. Distinguishing
    /// them would tell whoever presented a forgery which half of it was wrong, and would tell a holder whether the mail
    /// their expired link pointed at still exists.
    /// </summary>
    [Fact]
    public async Task DownloadAsync_CapabilityThatDoesNotVerifyAndOneWhoseMailIsGone_AreRefusedIdentically()
    {
        // Arrange
        var ticket = new AttachmentDownloadTicket(StoredEmailId.Create(Guid.CreateVersion7()), 0);
        var context = RequestToTheRoute();
        var principals = PrincipalsFor(context);
        using var body = new MemoryStream();
        context.Response.Body = body;

        // Act
        var refusedCapability = await EmailAttachmentDownloadEndpoint.DownloadAsync(
            "forged",
            TicketReaderRedeeming(null),
            AttachmentOpening(principals, null),
            principals,
            context,
            TestContext.Current.CancellationToken);

        var refusedMail = await EmailAttachmentDownloadEndpoint.DownloadAsync(
            "capability",
            TicketReaderRedeeming(ticket),
            AttachmentOpening(principals, null),
            principals,
            context,
            TestContext.Current.CancellationToken);

        // Assert
        var forgery = Assert.IsType<NotFound<ProblemDetails>>(refusedCapability.Result);
        var missingMail = Assert.IsType<NotFound<ProblemDetails>>(refusedMail.Result);
        Assert.Equal(EmailAttachmentDownloadEndpoint.RefusalDetail, forgery.Value?.Detail);
        Assert.Equal(forgery.Value?.Detail, missingMail.Value?.Detail);
        Assert.Equal(forgery.Value?.Title, missingMail.Value?.Title);
        Assert.Equal(StatusCodes.Status404NotFound, missingMail.Value?.Status);
        Assert.Empty(body.ToArray());
    }

    /// <summary>A refusal must say nothing about the mail it refused, so no part of a message may reach the body.</summary>
    [Fact]
    public async Task DownloadAsync_RefusedRequest_NamesNeitherTheEmailNorTheCapability()
    {
        // Arrange
        var storedEmailId = StoredEmailId.Create(Guid.CreateVersion7());
        var context = RequestToTheRoute();
        var principals = PrincipalsFor(context);

        // Act
        var result = await EmailAttachmentDownloadEndpoint.DownloadAsync(
            "a-capability-somebody-presented",
            TicketReaderRedeeming(new AttachmentDownloadTicket(storedEmailId, 3)),
            AttachmentOpening(principals, null),
            principals,
            context,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<NotFound<ProblemDetails>>(result.Result);
        var body = $"{refusal.Value?.Title} {refusal.Value?.Detail}";
        Assert.DoesNotContain(storedEmailId.Value.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("a-capability-somebody-presented", body, StringComparison.Ordinal);
    }

    /// <summary>Composes a request to this route's own path, which is what decides the principal the scope reports.</summary>
    /// <remarks>An empty path would leave the arrangement below deciding nothing, because a path neither surface serves is refused for that reason instead of for being this route's.</remarks>
    private static DefaultHttpContext RequestToTheRoute() =>
        new() { Request = { Path = EmailAttachmentDownloadEndpoint.RoutePrefix + "/a-capability-somebody-presented" } };

    /// <summary>The one scope a request is served in, which the route states its own principal onto.</summary>
    /// <remarks>
    /// The endpoint and the use case behind it are handed the same instance, exactly as a request scope hands them one.
    /// That is what makes these tests about the route establishing what authorized it rather than about a value passed
    /// along beside the call.
    /// </remarks>
    private static TransportAuthorizedPrincipalSource PrincipalsFor(HttpContext context)
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(context);

        // No endpoint configures a credential, which is the posture whose whole-surface grant would otherwise reach
        // this route. Nothing about the transport hands it a caller even so, so what the use case is told is only what
        // the route states once the ticket has verified.
        return new TransportAuthorizedPrincipalSource(
            httpContextAccessor,
            Options.Create(new McpEndpointOptions()),
            Options.Create(new AdminEndpointOptions()),
            Options.Create(new ClientEndpointOptions()));
    }

    private static IAttachmentDownloadTicketReader TicketReaderRedeeming(AttachmentDownloadTicket? ticket)
    {
        var ticketReader = Substitute.For<IAttachmentDownloadTicketReader>();
        ticketReader
            .RedeemAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ticket));

        return ticketReader;
    }

    /// <summary>Builds the real use case over ports that answer with the attachment a test wants served, or with nothing.</summary>
    /// <remarks>
    /// The use case is a concrete type rather than a port, so it is composed here instead of substituted. That is the
    /// honest shape as well: what this endpoint has to get right is how an opened attachment becomes a response and how
    /// an absent one becomes a refusal, and both travel through the real reader either way.
    /// </remarks>
    private static EmailAttachmentDownloadReader AttachmentOpening(
        IAuthorizedPrincipalSource principals,
        IOpenedEmailAttachment? attachment)
    {
        var summary = SummaryOf();

        var summaryReader = Substitute.For<IStoredEmailSummaryReader>();
        summaryReader
            .FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EmailSummary?>(summary));

        var contentStore = Substitute.For<IEmailContentStore>();
        contentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoredEmailContent?>(IntactContent()));

        var contentReader = Substitute.For<IEmailAttachmentContentReader>();
        contentReader
            .OpenAsync(Arg.Any<StoredEmailContent>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(attachment is null
                ? OpenedEmailAttachmentResult.NoSuchAttachment()
                : OpenedEmailAttachmentResult.Opened(attachment)));

        var accountCatalog = Substitute.For<IMailAccountCatalog>();
        accountCatalog.ServedAccounts.Returns([SyntheticServedAccount.Of(summary.AccountId)]);

        return new EmailAttachmentDownloadReader(
            summaryReader,
            contentStore,
            contentReader,
            Substitute.For<IEmailContentRepairRequestStore>(),
            new MailboxScopeResolver(
                accountCatalog,

                // The folder the summary was stored in is mapped, because a folder no mapping names does not exist as
                // far as MailFathom is concerned and every download of it would be refused before the endpoint is
                // reached — which is not the refusal any test here is about.
                StubMailFolderParticipation.Mapping(
                    new MailFolderIdentity(summary.AccountId, summary.FolderAlias)),
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            new AccessAuthorization(principals));
    }

    private static EmailSummary SummaryOf() => new()
    {
        StoredEmailId = StoredEmailId.Create(Guid.CreateVersion7()),
        AccountId = MailAccountId.Create("primary"),
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
}
