// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Portraits;
using MailFathom.Domain.Access;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Portraits;

/// <summary>
/// Covers the use case a person reads, replaces, and removes their own portrait through. What it has to hold is that
/// the owner acted on is the one the credential authenticated rather than one a caller could name, that the grant
/// required is the one a signed-in person already holds rather than the grant over their mail configuration, and that
/// having no picture is answered as such rather than as a failure.
/// </summary>
public sealed class OwnPortraitTests
{
    private static readonly byte[] Png =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x0D, 0x0A];

    [Fact]
    public async Task ReadAsync_APersonWhoSuppliedAPicture_AnswersItUnderTheKindItIs()
    {
        // Arrange
        var portraits = ReachedBy(StoreHolding(Png), MailFathomPermission.MailRead);

        // Act
        var read = await portraits.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("image/png", read!.Type.MediaType);
        Assert.Equal(Png, read.Content.ToArray());
    }

    /// <summary>A client draws the initials it already has, so an absent picture is a state of the screen rather than an error on it.</summary>
    [Fact]
    public async Task ReadAsync_APersonWhoSuppliedNone_AnswersNothing()
    {
        // Arrange
        var store = Substitute.For<IOwnerPortraitStore>();
        store.ReadAsync(Arg.Any<MailOwnerId>(), Arg.Any<CancellationToken>())
            .Returns((ReadOnlyMemory<byte>?)null);

        var portraits = ReachedBy(store, MailFathomPermission.MailRead);

        // Assert
        Assert.Null(await portraits.ReadAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>A row nothing here could have written is a row this use case has nothing to say about, so it answers as an absent picture rather than serving octets of an unknown kind.</summary>
    [Fact]
    public async Task ReadAsync_StoredOctetsOfNoKindThisBuildPublishes_AnswerAsNoPictureAtAll()
    {
        // Arrange
        var portraits = ReachedBy(StoreHolding("GIF89a"u8.ToArray()), MailFathomPermission.MailRead);

        // Assert
        Assert.Null(await portraits.ReadAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>The owner is resolved from the principal, so a deployment serving two people reads the caller's own row and never the other.</summary>
    [Fact]
    public async Task ReadAsync_ADeploymentServingSeveralPeople_ReadsTheRowOfTheOwnerTheCredentialAuthenticated()
    {
        // Arrange
        var store = Substitute.For<IOwnerPortraitStore>();
        var portraits = ReachedBy(store, SyntheticMailOwner.Another, MailFathomPermission.MailRead);

        // Act
        await portraits.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        await store.Received(1).ReadAsync(SyntheticMailOwner.Another, Arg.Any<CancellationToken>());
        await store.DidNotReceive().ReadAsync(SyntheticMailOwner.Deployment, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadAsync_ACallerGrantedNothing_IsRefused()
    {
        // Arrange
        var portraits = ReachedBy(Substitute.For<IOwnerPortraitStore>());

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => portraits.ReadAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>An administrator acts for nobody's mail, so there is no picture of theirs to read here.</summary>
    [Fact]
    public async Task ReadAsync_ACallerActingForNoOwner_IsRefused()
    {
        // Arrange
        var portraits = new OwnPortrait(
            AccessAuthorizations.ForAdministratorGranted(MailFathomPermission.MailRead),
            Substitute.For<IOwnerPortraitStore>());

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => portraits.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceAsync_APersonSupplyingAPicture_WritesItForTheOwnerTheCredentialAuthenticated()
    {
        // Arrange
        var store = Substitute.For<IOwnerPortraitStore>();
        store.SaveAsync(Arg.Any<MailOwnerId>(), Arg.Any<OwnerPortrait>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var portraits = ReachedBy(store, MailFathomPermission.MailRead);
        var portrait = OwnerPortrait.Of(Png)!;

        // Act
        var written = await portraits.ReplaceAsync(portrait, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(written);
        await store.Received(1)
            .SaveAsync(SyntheticMailOwner.Deployment, portrait, Arg.Any<CancellationToken>());
    }

    /// <summary>The row behind an authenticated caller can be gone, which is an owner erased under a credential that has not yet been withdrawn.</summary>
    [Fact]
    public async Task ReplaceAsync_ACallerWhoseRowHasGone_ReportsThatThereWasNobodyToWriteFor()
    {
        // Arrange
        var store = Substitute.For<IOwnerPortraitStore>();
        store.SaveAsync(Arg.Any<MailOwnerId>(), Arg.Any<OwnerPortrait>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var portraits = ReachedBy(store, MailFathomPermission.MailRead);

        // Act
        var written = await portraits.ReplaceAsync(OwnerPortrait.Of(Png)!, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(written);
    }

    /// <summary>The write is the grant a signed-in person already holds, never the one that decides which mailboxes this deployment connects to.</summary>
    [Fact]
    public async Task ReplaceAsync_ACallerGrantedOnlyTheirMailConfiguration_IsRefused()
    {
        // Arrange
        var store = Substitute.For<IOwnerPortraitStore>();
        var portraits = ReachedBy(store, MailFathomPermission.MailAccountsWrite);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => portraits.ReplaceAsync(OwnerPortrait.Of(Png)!, TestContext.Current.CancellationToken));

        await store.DidNotReceive()
            .SaveAsync(Arg.Any<MailOwnerId>(), Arg.Any<OwnerPortrait>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReplaceAsync_NoPictureAtAll_IsRefusedBeforeAnythingIsWritten()
    {
        // Arrange
        var store = Substitute.For<IOwnerPortraitStore>();
        var portraits = ReachedBy(store, MailFathomPermission.MailRead);

        // Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => portraits.ReplaceAsync(null!, TestContext.Current.CancellationToken));

        await store.DidNotReceive()
            .SaveAsync(Arg.Any<MailOwnerId>(), Arg.Any<OwnerPortrait>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAsync_APersonTakingTheirPictureDown_RemovesTheirOwnAndNobodyElses()
    {
        // Arrange
        var store = Substitute.For<IOwnerPortraitStore>();
        var portraits = ReachedBy(store, SyntheticMailOwner.Another, MailFathomPermission.MailRead);

        // Act
        await portraits.RemoveAsync(TestContext.Current.CancellationToken);

        // Assert
        await store.Received(1).RemoveAsync(SyntheticMailOwner.Another, Arg.Any<CancellationToken>());
        await store.DidNotReceive().RemoveAsync(SyntheticMailOwner.Deployment, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAsync_ACallerGrantedNothing_IsRefused()
    {
        // Arrange
        var store = Substitute.For<IOwnerPortraitStore>();
        var portraits = ReachedBy(store);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => portraits.RemoveAsync(TestContext.Current.CancellationToken));

        await store.DidNotReceive().RemoveAsync(Arg.Any<MailOwnerId>(), Arg.Any<CancellationToken>());
    }

    private static IOwnerPortraitStore StoreHolding(byte[] content)
    {
        var store = Substitute.For<IOwnerPortraitStore>();
        store.ReadAsync(Arg.Any<MailOwnerId>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<byte>(content));

        return store;
    }

    private static OwnPortrait ReachedBy(IOwnerPortraitStore store, params MailFathomPermission[] granted) =>
        ReachedBy(store, SyntheticMailOwner.Deployment, granted);

    private static OwnPortrait ReachedBy(
        IOwnerPortraitStore store,
        MailOwnerId owner,
        params MailFathomPermission[] granted) =>
        new(AccessAuthorizations.ForOwnerGranted(owner, granted), store);
}
