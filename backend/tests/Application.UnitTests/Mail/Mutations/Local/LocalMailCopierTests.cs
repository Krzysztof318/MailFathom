// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Mutations.Local;

/// <summary>Covers the two phases a copy on a held account is made of, and what each of them refuses.</summary>
public sealed class LocalMailCopierTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("personal");

    private static readonly MailFolderResolution Inbox =
        MailFolderResolution.FirstBindingOf(MailFolderAlias.Create("INBOX"), RemoteFolderPath.Create("INBOX"));

    private static readonly StoredEmailId Source = StoredEmailId.Create(Guid.CreateVersion7());

    private static readonly StoredEmailId Copy = StoredEmailId.Create(Guid.CreateVersion7());

    private static readonly LocalMailFolderId Target = LocalMailFolderId.Create(Guid.CreateVersion7());

    private static readonly ReadOnlyMemory<byte> RawMime = "Subject: copied\r\n\r\nbody"u8.ToArray();

    private static readonly CopiedMailFlags Flags = new(IsSeen: true, IsFlagged: false, RemoteEmailKeywords.Create([]));

    private readonly IEmailContentStore contents = ContentStores.Substituted();

    private readonly IEmailMetadataRepository emails = Substitute.For<IEmailMetadataRepository>();

    private readonly InMemoryLocalMailFolderStore folders = new(Account, MailAccountCustodyPhase.Held);

    private readonly IPersistenceSession session = Substitute.For<IPersistenceSession>();

    /// <summary>The payload is placed under an object of its own, because two rows never share one.</summary>
    [Fact]
    public async Task PrepareAsync_AMessageWhosePayloadIsStored_PlacesAPayloadOfItsOwn()
    {
        // Arrange
        this.StoreContent();

        // Act
        var prepared = await this.Copier().PrepareAsync(Account, Source, Token);

        // Assert
        Assert.NotNull(prepared);
        Assert.Equal(Source, prepared.Source);
        Assert.Equal(RawMime.Length, prepared.Content.ByteLength);
        await this.contents.Received(1).PlaceContentAsync(
            EmailContentKind.IncomingMessage,
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>There is nothing to copy where the payload was never stored, so nothing is placed.</summary>
    [Fact]
    public async Task PrepareAsync_AMessageWithNoStoredPayload_PlacesNothing()
    {
        // Act
        var prepared = await this.Copier().PrepareAsync(Account, Source, Token);

        // Assert
        Assert.Null(prepared);
        await this.contents.DidNotReceive().PlaceContentAsync(
            Arg.Any<EmailContentKind>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The commit writes the row, points it at the placed payload, and files it where the copy was asked for.</summary>
    [Fact]
    public async Task CommitAsync_APreparedCopy_WritesTheRowSavesTheContentAndFilesIt()
    {
        // Arrange
        this.StoreContent();
        this.StoreCopyAs(Copy);
        var copier = this.Copier();
        var prepared = await copier.PrepareAsync(Account, Source, Token);

        // Act
        var copied = await copier.CommitAsync(this.session, prepared!, Inbox.Id, Target, Flags, Token);

        // Assert
        Assert.Equal(Copy, copied);
        Assert.Equal(Target, this.folders.Placements[Copy]);
        await this.contents.Received(1).SaveContentAsync(
            this.session,
            Copy,
            null,
            prepared!.Content,
            Arg.Any<CancellationToken>());
    }

    /// <summary>A binding the drain removed after the payload was placed leaves the copy unwritten rather than raising.</summary>
    [Fact]
    public async Task CommitAsync_ABindingThatIsGone_WritesNothing()
    {
        // Arrange
        this.StoreContent();
        this.StoreCopyAs(null);
        var copier = this.Copier();
        var prepared = await copier.PrepareAsync(Account, Source, Token);

        // Act
        var copied = await copier.CommitAsync(this.session, prepared!, Inbox.Id, Target, Flags, Token);

        // Assert
        Assert.Null(copied);
        Assert.Empty(this.folders.Placements);
        await this.contents.DidNotReceive().SaveContentAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<StoredEmailId>(),
            Arg.Any<EmailOccurrenceId?>(),
            Arg.Any<PlacedEmailContent>(),
            Arg.Any<CancellationToken>());
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private LocalMailCopier Copier() =>
        LocalMailCopiers.Over(this.folders, this.contents, this.emails, MimeReadingNothing());

    private static IEmailMimeReader MimeReadingNothing()
    {
        var mimeReader = Substitute.For<IEmailMimeReader>();

        mimeReader
            .ReadMetadataAsync(Arg.Any<MailAccountId>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailMimeExtractionResult.MalformedContent()));

        return mimeReader;
    }

    private void StoreContent() =>
        this.contents
            .FindStoredContentAsync(Source, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoredEmailContent?>(
                new StoredEmailContent(RawMime, RawMime.Length, ReadOnlyMemory<byte>.Empty)));

    private void StoreCopyAs(StoredEmailId? written) =>
        this.emails
            .StoreLocalCopyAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolutionId>(),
                Arg.Any<ExtractedEmailMetadata?>(),
                Arg.Any<long>(),
                Arg.Any<CopiedMailFlags>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(written));
}
