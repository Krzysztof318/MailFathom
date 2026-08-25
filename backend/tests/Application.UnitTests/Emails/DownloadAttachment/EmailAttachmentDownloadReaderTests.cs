// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.DownloadAttachment;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Folders;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.DownloadAttachment;

/// <summary>Covers what a redeemed attachment capability is served with, and everything it is refused for.</summary>
/// <remarks>
/// The signature is verified before this use case is reached, so every test here is about what a signature cannot
/// establish: that the message still exists, that its account is still served, that the stored copy is intact, and that
/// the part the capability names is still there.
/// </remarks>
public sealed class EmailAttachmentDownloadReaderTests
{
    private const string ServedAccountId = "primary";

    /// <summary>The one attachment of the one email every capability in this suite is minted for.</summary>
    private const string AuthorizedObject = "/attachments/0198f0aa-0000-7000-8000-000000000000/0";

    private static readonly byte[] StoredRawMime = Encoding.UTF8.GetBytes("From: sender@example.test\r\n\r\nBody");

    /// <summary>The principal the download route states once it has verified a link, which is what this use case admits.</summary>
    private static readonly AuthorizedPrincipal RedeemedCapability =
        AuthorizedPrincipal.SignedCapability(SyntheticMailOwner.Deployment, AuthorizedObject);

    [Fact]
    public async Task OpenAsync_AttachmentOfAServedEmail_OpensItThroughTheStoredCopy()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(attachmentCount: 1);
        var contentReader = ContentReaderOpening("invoice.pdf");
        var reader = ReaderOver(summary, contentReader: contentReader);

        // Act
        await using var attachment = await reader.OpenAsync(
            new AttachmentDownloadTicket(summary.StoredEmailId, AttachmentPosition: 0),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(attachment);
        Assert.Equal("invoice.pdf", attachment.Description.FileName?.Value);
    }

    /// <summary>
    /// The capability names a position, and the use case passes it through untouched: a link minted for the second file
    /// must open the second file rather than whichever one the store happens to answer with.
    /// </summary>
    [Fact]
    public async Task OpenAsync_AttachmentPosition_IsThePositionThePartIsOpenedAt()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(attachmentCount: 3);
        var contentReader = Substitute.For<IEmailAttachmentContentReader>();
        contentReader
            .OpenAsync(Arg.Any<StoredEmailContent>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(
                OpenedEmailAttachmentResult.Opened(new StubOpenedEmailAttachment($"file-{call.Arg<int>()}.pdf"))));
        var reader = ReaderOver(summary, contentReader: contentReader);

        // Act
        await using var attachment = await reader.OpenAsync(
            new AttachmentDownloadTicket(summary.StoredEmailId, AttachmentPosition: 2),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("file-2.pdf", attachment?.Description.FileName?.Value);
    }

    /// <summary>
    /// A capability must not outlive the deletion of its own message. The store is read afresh on every redemption
    /// rather than against anything staged when the link was minted, which is what makes that structural.
    /// </summary>
    [Fact]
    public async Task OpenAsync_EmailThisMailboxCopyNoLongerHolds_RefusesWithoutReadingAnyContent()
    {
        // Arrange
        var contentStore = ContentStores.Substituted();
        var reader = ReaderOver(summary: null, contentStore: contentStore);

        // Act
        await using var attachment = await reader.OpenAsync(
            new AttachmentDownloadTicket(StoredEmailId.Create(Guid.CreateVersion7()), AttachmentPosition: 0),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(attachment);
        await contentStore.DidNotReceive().FindStoredContentAsync(
            Arg.Any<StoredEmailId>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An account this deployment no longer serves leaves its rows in place, so the row existing is not enough. It is
    /// answered exactly as a deleted message is, because telling the two apart would let a capability report what
    /// became of mail its holder can no longer read.
    /// </summary>
    [Fact]
    public async Task OpenAsync_EmailOfAnAccountThisDeploymentNoLongerServes_Refuses()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(accountId: "retired");
        var reader = ReaderOver(summary, accountCatalog: CatalogServing(MailAccountId.Create(ServedAccountId)));

        // Act
        await using var attachment = await reader.OpenAsync(
            new AttachmentDownloadTicket(summary.StoredEmailId, AttachmentPosition: 0),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(attachment);
    }

    /// <summary>
    /// The use case answers for whichever owner the redeeming principal names, so an account that owner does not own
    /// reaches nothing, and the refusal is the one a deleted message gets.
    /// </summary>
    /// <remarks>
    /// Which owner a redemption names is the route's decision rather than the ticket's, and today it is the
    /// deployment's own: <c>AttachmentDownloadTicket</c> records no owner, and
    /// <c>EmailAttachmentDownloadEndpoint</c> states the one owner a deployment declaring its accounts in
    /// configuration holds. ADR 0014's ticket-borne ownership is what replaces that once an account can belong to a
    /// second owner. So this holds the use case to the owner it is handed rather than asserting that a redemption can
    /// hand it somebody else's.
    /// </remarks>
    [Fact]
    public async Task OpenAsync_EmailOfAnAccountTheRedeemingPrincipalsOwnerDoesNotOwn_Refuses()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var authorization = AuthorizationOver(
            AuthorizedPrincipal.SignedCapability(SyntheticMailOwner.Another, AuthorizedObject));
        var reader = ReaderOver(
            summary,
            accountCatalog: OwnedMailAccountCatalogs.For(authorization, SyntheticServedAccount.Of(summary.AccountId)),
            authorization: authorization);

        // Act
        await using var attachment = await reader.OpenAsync(
            new AttachmentDownloadTicket(summary.StoredEmailId, AttachmentPosition: 0),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(attachment);
    }

    /// <summary>
    /// A ticket outlives the configuration it was minted under, so a folder withheld from tools after the link was
    /// issued stops the download as well. It is refused exactly as an unserved account is, for the same reason.
    /// </summary>
    [Fact]
    public async Task OpenAsync_EmailOfAFolderWithheldFromTools_Refuses()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var reader = ReaderOver(
            summary,
            folderParticipation: StubMailFolderParticipation
                .Mapping(new MailFolderIdentity(summary.AccountId, summary.FolderAlias))
                .Hiding(new MailFolderIdentity(summary.AccountId, summary.FolderAlias)));

        // Act
        await using var attachment = await reader.OpenAsync(
            new AttachmentDownloadTicket(summary.StoredEmailId, AttachmentPosition: 0),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(attachment);
    }

    /// <summary>
    /// A mapping an operator removed withdraws the folder, so a capability minted while it was mapped opens nothing
    /// afterwards. It is refused exactly as a withheld folder is, because the deployment no longer has the folder at
    /// all.
    /// </summary>
    [Fact]
    public async Task OpenAsync_EmailOfAFolderNoMappingNames_Refuses()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create();
        var reader = ReaderOver(summary, folderParticipation: StubMailFolderParticipation.Nothing);

        // Act
        await using var attachment = await reader.OpenAsync(
            new AttachmentDownloadTicket(summary.StoredEmailId, AttachmentPosition: 0),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(attachment);
    }

    /// <summary>
    /// A defect discovered through a link is the same defect a read would have found, so it is recorded rather than
    /// discarded because of the door the request came through.
    /// </summary>
    [Theory]
    [InlineData(nameof(EmailContentDefect.Missing))]
    [InlineData(nameof(EmailContentDefect.ByteLengthMismatch))]
    [InlineData(nameof(EmailContentDefect.Unreadable))]
    public async Task OpenAsync_DamagedOrMissingLocalCopy_RefusesAndRecordsARepairRequest(string defect)
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(attachmentCount: 1);
        var repairRequests = new RecordingEmailContentRepairRequestStore();
        var reader = ReaderOver(
            summary,
            contentStore: ContentStoreReturning(StoredContentFor(defect)),
            contentReader: defect == nameof(EmailContentDefect.Unreadable)
                ? ContentReaderReporting(OpenedEmailAttachmentResult.Unreadable())
                : ContentReaderOpening("invoice.pdf"),
            repairRequestStore: repairRequests);

        // Act
        await using var attachment = await reader.OpenAsync(
            new AttachmentDownloadTicket(summary.StoredEmailId, AttachmentPosition: 0),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(attachment);
        var recorded = Assert.Single(repairRequests.Recorded);
        Assert.Equal(summary.StoredEmailId, recorded.StoredEmailId);
        Assert.Equal(defect, recorded.Defect.ToString());
    }

    /// <summary>
    /// An object that could not be vouched for is recorded through every door onto the same mail, so a message reached
    /// only by downloading its files says so as loudly as one somebody read.
    /// </summary>
    [Fact]
    public async Task OpenAsync_ContentServedFromTheRetainedCopy_OpensTheAttachmentAndRecordsARepairRequest()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(attachmentCount: 1);
        var repairRequests = new RecordingEmailContentRepairRequestStore();
        var reader = ReaderOver(
            summary,
            contentStore: ContentStoreReturning(IntactContent() with { WasServedFromRetainedCopy = true }),
            repairRequestStore: repairRequests);

        // Act
        await using var attachment = await reader.OpenAsync(
            new AttachmentDownloadTicket(summary.StoredEmailId, AttachmentPosition: 0),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(attachment);
        var recorded = Assert.Single(repairRequests.Recorded);
        Assert.Equal(summary.StoredEmailId, recorded.StoredEmailId);
        Assert.Equal(EmailContentDefect.ObjectUnreadable, recorded.Defect);
    }

    /// <summary>
    /// A position the message does not carry is an ordinary refusal rather than a damaged copy, so it must not put a
    /// healthy message into the queue of copies waiting to be fetched again.
    /// </summary>
    [Fact]
    public async Task OpenAsync_PositionTheMessageDoesNotCarry_RefusesAndRecordsNoRepairRequest()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(attachmentCount: 1);
        var repairRequests = new RecordingEmailContentRepairRequestStore();
        var reader = ReaderOver(
            summary,
            contentReader: ContentReaderReporting(OpenedEmailAttachmentResult.NoSuchAttachment()),
            repairRequestStore: repairRequests);

        // Act
        await using var attachment = await reader.OpenAsync(
            new AttachmentDownloadTicket(summary.StoredEmailId, AttachmentPosition: 7),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(attachment);
        Assert.Empty(repairRequests.Recorded);
    }

    /// <summary>
    /// The transport is what usually states the principal, so this is the case where an entrypoint reached the use case
    /// without saying what admitted it. It refuses rather than serving the attachment the ticket names, which is what
    /// makes the check the authority instead of a second opinion.
    /// </summary>
    [Fact]
    public async Task OpenAsync_ReachedUnderNoPrincipal_RefusesWithoutReadingStoredContent()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(attachmentCount: 1);
        var contentStore = ContentStoreReturning(IntactContent());
        var reader = ReaderOver(summary, contentStore, authorization: AuthorizationOver(principal: null));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(async () =>
            await reader.OpenAsync(
                new AttachmentDownloadTicket(summary.StoredEmailId, AttachmentPosition: 0),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.False(refusal.RequiredPermission.IsSpecified);
        await contentStore
            .DidNotReceive()
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A capability is the authorization here, so holding a mailbox grant is not a second way in. A caller granted
    /// everything the mail surface publishes is refused exactly as one granted nothing is.
    /// </summary>
    [Fact]
    public async Task OpenAsync_ReachedByACallerRatherThanACapability_Refuses()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(attachmentCount: 1);
        var reader = ReaderOver(
            summary,
            authorization: AuthorizationOver(AuthorizedPrincipal.Caller(
                "mcp-key",
                MailFathomPermission.PublishedFor(ProtectedSurface.Mail))));

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(async () =>
            await reader.OpenAsync(
                new AttachmentDownloadTicket(summary.StoredEmailId, AttachmentPosition: 0),
                TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Serving a file must never fetch one, which is the acceptance criterion the whole content path exists under. The
    /// use case holds no mailbox port, so the guarantee is structural rather than a rule somebody has to keep.
    /// </summary>
    [Fact]
    public void EmailAttachmentDownloadReader_ItsDependencies_IncludeNoMailboxPort()
    {
        // Arrange
        Type[] mailboxPorts =
        [
            typeof(IMailboxSessionFactory),
            typeof(IMailboxSession),
            typeof(IMailboxNotificationSessionFactory),
        ];

        // Act
        var dependencies = typeof(EmailAttachmentDownloadReader)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType);

        // Assert
        Assert.Empty(dependencies.Intersect(mailboxPorts));
    }

    private static EmailAttachmentDownloadReader ReaderOver(
        EmailSummary? summary,
        IEmailContentStore? contentStore = null,
        IEmailAttachmentContentReader? contentReader = null,
        IEmailContentRepairRequestStore? repairRequestStore = null,
        ICallerMailAccountCatalog? accountCatalog = null,
        IMailFolderParticipationReader? folderParticipation = null,
        AccessAuthorization? authorization = null) => new(
        SummaryReaderReturning(summary),
        contentStore ?? ContentStoreReturning(IntactContent()),
        contentReader ?? ContentReaderOpening("invoice.pdf"),
        repairRequestStore ?? new RecordingEmailContentRepairRequestStore(),
        new MailboxScopeResolver(
            accountCatalog ?? CatalogServing(MailAccountId.Create(summary?.AccountId.Value ?? ServedAccountId)),
            folderParticipation ?? MappingFolderOf(summary),
            StubJunkMailFolderCatalog.None,
            StubMailFolderMappings.ResolvingNothing),
        authorization ?? AuthorizationOver(RedeemedCapability));

    /// <summary>Composes the authorization a use case asks, over whichever principal a test says reached it.</summary>
    private static AccessAuthorization AuthorizationOver(AuthorizedPrincipal? principal)
    {
        var principals = Substitute.For<IAuthorizedPrincipalSource>();
        principals.Current.Returns(principal);

        return new AccessAuthorization(principals);
    }

    /// <summary>Maps the folder this email was stored from, which is what a deployment holding it has configured.</summary>
    /// <remarks>
    /// A folder no mapping names does not exist as far as MailFathom is concerned, so a reader arranged without one
    /// refuses every download. Stating the mapping is therefore part of arranging stored mail at all.
    /// </remarks>
    private static StubMailFolderParticipation MappingFolderOf(EmailSummary? summary) => summary is null
        ? StubMailFolderParticipation.Nothing
        : StubMailFolderParticipation.Mapping(new MailFolderIdentity(summary.AccountId, summary.FolderAlias));

    private static IStoredEmailSummaryReader SummaryReaderReturning(EmailSummary? summary)
    {
        var reader = Substitute.For<IStoredEmailSummaryReader>();
        reader
            .FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(summary));

        return reader;
    }

    private static IEmailContentStore ContentStoreReturning(StoredEmailContent? storedContent)
    {
        var contentStore = ContentStores.Substituted();
        contentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(storedContent));

        return contentStore;
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The opened attachment is what the reader hands back, and the test under it disposes what it receives.")]
    private static IEmailAttachmentContentReader ContentReaderOpening(string fileName) =>
        ContentReaderReporting(OpenedEmailAttachmentResult.Opened(new StubOpenedEmailAttachment(fileName)));

    private static IEmailAttachmentContentReader ContentReaderReporting(OpenedEmailAttachmentResult result)
    {
        var contentReader = Substitute.For<IEmailAttachmentContentReader>();
        contentReader
            .OpenAsync(Arg.Any<StoredEmailContent>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(result));

        return contentReader;
    }

    private static ICallerMailAccountCatalog CatalogServing(params MailAccountId[] servedAccountIds)
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.OwnedAccounts.Returns([.. servedAccountIds.Select(SyntheticServedAccount.Of)]);

        return catalog;
    }

    private static StoredEmailContent IntactContent() =>
        new(StoredRawMime, StoredRawMime.Length, SHA256.HashData(StoredRawMime));

    /// <summary>Builds the stored copy that produces one defect, or the intact one for a defect found further down.</summary>
    private static StoredEmailContent? StoredContentFor(string defect) => defect switch
    {
        nameof(EmailContentDefect.Missing) => null,
        nameof(EmailContentDefect.ByteLengthMismatch) =>
            new StoredEmailContent(StoredRawMime, StoredRawMime.Length + 1, SHA256.HashData(StoredRawMime)),
        _ => IntactContent(),
    };

    /// <summary>An opened attachment that describes a file and writes nothing, because what it writes is not this use case's claim.</summary>
    private sealed class StubOpenedEmailAttachment(string fileName) : IOpenedEmailAttachment
    {
        public ExtractedEmailAttachment Description { get; } = new(
            AttachmentFileName.TryNormalize(fileName, out var normalized) ? normalized : null,
            "application/pdf",
            DecodedSizeOctets: 16);

        public Task WriteContentToAsync(Stream destination, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(destination);

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
