// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Portraits;
using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Portraits;

/// <summary>
/// Covers the use case a person reads, replaces, and removes their own portrait through. What it has to hold is that
/// the user acted on is the one the credential authenticated rather than one a caller could name, that the grant
/// required is the one a signed-in person already holds rather than the grant over their mail configuration, that
/// having no picture is answered as such rather than as a failure, and that a replacement writes before it links and
/// links before it removes.
/// </summary>
public sealed class OwnPortraitTests
{
    private static readonly byte[] Png =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x0D, 0x0A];

    private static readonly StoredFileId Linked = StoredFileId.Create(Guid.Parse("0197a3c0-0000-7000-8000-000000000001"));

    private static readonly StoredFileId Written = StoredFileId.Create(Guid.Parse("0197a3c0-0000-7000-8000-000000000002"));

    [Fact]
    public async Task ReadAsync_APersonWhoSuppliedAPicture_AnswersItUnderTheKindItIs()
    {
        // Arrange
        var portraits = ReachedBy(FilesHolding(Png), LinksTo(Linked), MailFathomPermission.MailRead);

        // Act
        var read = await portraits.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("image/png", read!.Type.MediaType);
        Assert.Equal(Png, read.Content.ToArray());
    }

    /// <summary>A client draws the initials it already has, so an absent picture is a state of the screen rather than an error on it.</summary>
    [Fact]
    public async Task ReadAsync_ARecordLinkingNoPortrait_AnswersNothingWithoutReadingAFile()
    {
        // Arrange
        var files = Substitute.For<IStoredFileStore>();
        var portraits = ReachedBy(files, LinksTo(null), MailFathomPermission.MailRead);

        // Act
        var read = await portraits.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(read);
        await files.DidNotReceive().ReadAsync(Arg.Any<MailUserId>(), Arg.Any<StoredFileId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A file nothing here could have written is a file this use case has nothing to say about, so it answers as an absent picture rather than serving octets of an unknown kind.</summary>
    [Fact]
    public async Task ReadAsync_StoredOctetsOfNoKindThisBuildPublishes_AnswerAsNoPictureAtAll()
    {
        // Arrange
        var portraits = ReachedBy(FilesHolding("GIF89a"u8.ToArray()), LinksTo(Linked), MailFathomPermission.MailRead);

        // Assert
        Assert.Null(await portraits.ReadAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>The user is resolved from the principal, so a deployment serving two people reads the caller's own record and file and never the other's.</summary>
    [Fact]
    public async Task ReadAsync_ADeploymentServingSeveralPeople_ReadsTheFileOfTheUserTheCredentialAuthenticated()
    {
        // Arrange
        var files = FilesHolding(Png);
        var links = LinksTo(Linked);
        var portraits = ReachedBy(files, links, SyntheticMailUser.Another, MailFathomPermission.MailRead);

        // Act
        await portraits.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        await links.Received(1).FindPortraitAsync(SyntheticMailUser.Another, Arg.Any<CancellationToken>());
        await files.Received(1).ReadAsync(SyntheticMailUser.Another, Linked, Arg.Any<CancellationToken>());
        await files.DidNotReceive().ReadAsync(SyntheticMailUser.Deployment, Arg.Any<StoredFileId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadAsync_ACallerGrantedNothing_IsRefused()
    {
        // Arrange
        var portraits = ReachedBy(Substitute.For<IStoredFileStore>(), LinksTo(Linked));

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => portraits.ReadAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>An administrator acts for nobody's mail, so there is no picture of theirs to read here.</summary>
    [Fact]
    public async Task ReadAsync_ACallerActingForNoUser_IsRefused()
    {
        // Arrange
        var portraits = new OwnPortrait(
            AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.MailRead),
            Substitute.For<IStoredFileStore>(),
            LinksTo(Linked));

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => portraits.ReadAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The order is what makes every failure between two steps leave at most a file nothing links to: the record never
    /// names a file that is not written yet, and the displaced file goes only once nothing names it.
    /// </summary>
    [Fact]
    public async Task ReplaceAsync_APersonReplacingTheirPicture_WritesTheNewFileThenLinksItThenRemovesTheOldOne()
    {
        // Arrange
        var files = FilesWriting(Written);
        var links = Substitute.For<IUserRecordFileLinks>();
        links.RelinkOwnPortraitAsync(Arg.Any<StoredFileId?>(), Arg.Any<CancellationToken>())
            .Returns(new PortraitRelinking(UserHeld: true, Replaced: Linked));

        var portraits = ReachedBy(files, links, MailFathomPermission.MailRead);

        // Act
        var replaced = await portraits.ReplaceAsync(UserPortrait.Of(Png)!, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(replaced);
        Received.InOrder(() =>
        {
            _ = files.WriteAsync(SyntheticMailUser.Deployment, "image/png", Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
            _ = links.RelinkOwnPortraitAsync(Written, Arg.Any<CancellationToken>());
            _ = files.RemoveAsync(SyntheticMailUser.Deployment, Linked, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task ReplaceAsync_APersonSupplyingTheirFirstPicture_RemovesNothing()
    {
        // Arrange
        var files = FilesWriting(Written);
        var links = Substitute.For<IUserRecordFileLinks>();
        links.RelinkOwnPortraitAsync(Arg.Any<StoredFileId?>(), Arg.Any<CancellationToken>())
            .Returns(new PortraitRelinking(UserHeld: true, Replaced: null));

        var portraits = ReachedBy(files, links, MailFathomPermission.MailRead);

        // Act
        var replaced = await portraits.ReplaceAsync(UserPortrait.Of(Png)!, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(replaced);
        await files.DidNotReceive().RemoveAsync(Arg.Any<MailUserId>(), Arg.Any<StoredFileId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A link that failed leaves the new file unlinked for the sweep, and must never have taken the old one with it.</summary>
    [Fact]
    public async Task ReplaceAsync_ALinkThatFails_LeavesThePictureInForceWhereItWas()
    {
        // Arrange
        var files = FilesWriting(Written);
        var links = Substitute.For<IUserRecordFileLinks>();
        links.RelinkOwnPortraitAsync(Arg.Any<StoredFileId?>(), Arg.Any<CancellationToken>())
            .Returns<PortraitRelinking>(_ => throw new InvalidOperationException("The record refused the link."));

        var portraits = ReachedBy(files, links, MailFathomPermission.MailRead);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => portraits.ReplaceAsync(UserPortrait.Of(Png)!, TestContext.Current.CancellationToken));

        // Assert
        await files.DidNotReceive().RemoveAsync(Arg.Any<MailUserId>(), Arg.Any<StoredFileId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The record behind an authenticated caller can be gone, which is a user erased under a credential that has not yet been withdrawn.</summary>
    [Fact]
    public async Task ReplaceAsync_ACallerWhoseRecordHasGone_ReportsThatThereWasNobodyToWriteFor()
    {
        // Arrange
        var files = Substitute.For<IStoredFileStore>();
        files.WriteAsync(Arg.Any<MailUserId>(), Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns((StoredFileId?)null);

        var links = Substitute.For<IUserRecordFileLinks>();
        var portraits = ReachedBy(files, links, MailFathomPermission.MailRead);

        // Act
        var replaced = await portraits.ReplaceAsync(UserPortrait.Of(Png)!, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(replaced);
        await links.DidNotReceive().RelinkOwnPortraitAsync(Arg.Any<StoredFileId?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReplaceAsync_ARecordErasedBetweenTheWriteAndTheLink_ReportsThatThereWasNobodyToWriteFor()
    {
        // Arrange
        var links = Substitute.For<IUserRecordFileLinks>();
        links.RelinkOwnPortraitAsync(Arg.Any<StoredFileId?>(), Arg.Any<CancellationToken>())
            .Returns(PortraitRelinking.NoSuchUser);

        var portraits = ReachedBy(FilesWriting(Written), links, MailFathomPermission.MailRead);

        // Act
        var replaced = await portraits.ReplaceAsync(UserPortrait.Of(Png)!, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(replaced);
    }

    /// <summary>The write is the grant a signed-in person already holds, never the one that decides which mailboxes this deployment connects to.</summary>
    [Fact]
    public async Task ReplaceAsync_ACallerGrantedOnlyTheirMailConfiguration_IsRefused()
    {
        // Arrange
        var files = Substitute.For<IStoredFileStore>();
        var portraits = ReachedBy(files, LinksTo(Linked), MailFathomPermission.MailAccountsWrite);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => portraits.ReplaceAsync(UserPortrait.Of(Png)!, TestContext.Current.CancellationToken));

        await files.DidNotReceive().WriteAsync(Arg.Any<MailUserId>(), Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReplaceAsync_NoPictureAtAll_IsRefusedBeforeAnythingIsWritten()
    {
        // Arrange
        var files = Substitute.For<IStoredFileStore>();
        var portraits = ReachedBy(files, LinksTo(Linked), MailFathomPermission.MailRead);

        // Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => portraits.ReplaceAsync(null!, TestContext.Current.CancellationToken));

        await files.DidNotReceive().WriteAsync(Arg.Any<MailUserId>(), Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAsync_APersonTakingTheirPictureDown_UnlinksThenRemovesTheirOwnFile()
    {
        // Arrange
        var files = Substitute.For<IStoredFileStore>();
        var links = Substitute.For<IUserRecordFileLinks>();
        links.RelinkOwnPortraitAsync(Arg.Any<StoredFileId?>(), Arg.Any<CancellationToken>())
            .Returns(new PortraitRelinking(UserHeld: true, Replaced: Linked));

        var portraits = ReachedBy(files, links, SyntheticMailUser.Another, MailFathomPermission.MailRead);

        // Act
        await portraits.RemoveAsync(TestContext.Current.CancellationToken);

        // Assert
        Received.InOrder(() =>
        {
            _ = links.RelinkOwnPortraitAsync(null, Arg.Any<CancellationToken>());
            _ = files.RemoveAsync(SyntheticMailUser.Another, Linked, Arg.Any<CancellationToken>());
        });
        await files.DidNotReceive().RemoveAsync(SyntheticMailUser.Deployment, Arg.Any<StoredFileId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAsync_ACallerGrantedNothing_IsRefused()
    {
        // Arrange
        var files = Substitute.For<IStoredFileStore>();
        var links = Substitute.For<IUserRecordFileLinks>();
        var portraits = ReachedBy(files, links);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => portraits.RemoveAsync(TestContext.Current.CancellationToken));

        await links.DidNotReceive().RelinkOwnPortraitAsync(Arg.Any<StoredFileId?>(), Arg.Any<CancellationToken>());
        await files.DidNotReceive().RemoveAsync(Arg.Any<MailUserId>(), Arg.Any<StoredFileId>(), Arg.Any<CancellationToken>());
    }

    private static IStoredFileStore FilesHolding(byte[] content)
    {
        var files = Substitute.For<IStoredFileStore>();
        files.ReadAsync(Arg.Any<MailUserId>(), Arg.Any<StoredFileId>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<byte>(content));

        return files;
    }

    private static IStoredFileStore FilesWriting(StoredFileId written)
    {
        var files = Substitute.For<IStoredFileStore>();
        files.WriteAsync(Arg.Any<MailUserId>(), Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(written);

        return files;
    }

    private static IUserRecordFileLinks LinksTo(StoredFileId? portrait)
    {
        var links = Substitute.For<IUserRecordFileLinks>();
        links.FindPortraitAsync(Arg.Any<MailUserId>(), Arg.Any<CancellationToken>()).Returns(portrait);

        return links;
    }

    private static OwnPortrait ReachedBy(
        IStoredFileStore files,
        IUserRecordFileLinks links,
        params MailFathomPermission[] granted) =>
        ReachedBy(files, links, SyntheticMailUser.Deployment, granted);

    private static OwnPortrait ReachedBy(
        IStoredFileStore files,
        IUserRecordFileLinks links,
        MailUserId user,
        params MailFathomPermission[] granted) =>
        new(AccessAuthorizations.ForUserGranted(user, granted), files, links);
}
