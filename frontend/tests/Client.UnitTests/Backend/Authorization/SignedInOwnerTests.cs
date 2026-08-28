// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Authorization;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Backend.Authorization;

/// <summary>Who is signed in during a run, what outlives it, and the three rules that decide what is cleared.</summary>
public sealed class SignedInOwnerTests
{
    private static readonly Uri Deployment = new("https://mail.example/");

    [Fact]
    public void IsSignedIn_NobodyHasSignedIn_IsFalseAndNamesNobody()
    {
        // Arrange
        var owner = new SignedInOwner(new StubOwnerCredentialStore());

        // Act, Assert
        Assert.False(owner.IsSignedIn);
        Assert.Null(owner.Username);
    }

    /// <summary>The username is the half a screen may read; the password is not on this type's public surface at all.</summary>
    [Fact]
    public async Task AcceptAsync_ACredential_SignsThemInUnderTheirUsername()
    {
        // Arrange
        var owner = new SignedInOwner(new StubOwnerCredentialStore());

        // Act
        await owner.AcceptAsync(
            Deployment,
            new OwnerCredential("ada", "a-long-password"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(owner.IsSignedIn);
        Assert.Equal("ada", owner.Username);
    }

    /// <summary>Everything derived from what the deployment answered is stale the moment the identity changes.</summary>
    [Fact]
    public async Task AcceptAsync_ACredential_AnnouncesThatTheSessionChanged()
    {
        // Arrange
        var owner = new SignedInOwner(new StubOwnerCredentialStore());
        var announced = 0;

        owner.SignedInChanged += (_, _) => announced++;

        // Act
        await owner.AcceptAsync(
            Deployment,
            new OwnerCredential("ada", "a-long-password"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, announced);
    }

    /// <summary>Kept beside the deployment that accepted it, which is what a later start reconciles against.</summary>
    [Fact]
    public async Task AcceptAsync_OnAHeadThatKeepsOne_KeepsItForThatDeployment()
    {
        // Arrange
        var store = new StubOwnerCredentialStore();
        var owner = new SignedInOwner(store);

        // Act
        var persistence = await owner.AcceptAsync(
            Deployment,
            new OwnerCredential("ada", "a-long-password"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CredentialPersistence.Kept, persistence);
        Assert.Equal(Deployment, store.Held?.Deployment);
    }

    /// <summary>A store that refuses leaves somebody signed in for this run rather than not signed in at all.</summary>
    [Fact]
    public async Task AcceptAsync_WhereTheStoreRefuses_LeavesThemSignedInForThisRun()
    {
        // Arrange
        var store = new StubOwnerCredentialStore(CredentialPersistence.StoreUnavailable);
        var owner = new SignedInOwner(store);

        // Act
        var persistence = await owner.AcceptAsync(
            Deployment,
            new OwnerCredential("ada", "a-long-password"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CredentialPersistence.StoreUnavailable, persistence);
        Assert.True(owner.IsSignedIn);
        Assert.Null(store.Held);
    }

    [Fact]
    public async Task ForgetAsync_AfterSigningIn_HoldsNothingAndKeepsNothing()
    {
        // Arrange
        var store = new StubOwnerCredentialStore();
        var owner = new SignedInOwner(store);

        await owner.AcceptAsync(
            Deployment,
            new OwnerCredential("ada", "a-long-password"),
            TestContext.Current.CancellationToken);

        // Act
        await owner.ForgetAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(owner.IsSignedIn);
        Assert.Null(owner.Username);
        Assert.Null(store.Held);
    }

    /// <summary>
    /// A start that restored nothing may still be sitting on an item a previous run wrote, so the store is cleared
    /// whether or not anything was held here.
    /// </summary>
    [Fact]
    public async Task ForgetAsync_WithNobodySignedIn_StillClearsWhatWasKept()
    {
        // Arrange
        var store = new StubOwnerCredentialStore(
            held: new KeptOwnerCredential(Deployment, new OwnerCredential("ada", "a-long-password")));

        var owner = new SignedInOwner(store);

        // Act
        await owner.ForgetAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(store.Held);
        Assert.Equal(1, store.Cleared);
    }

    /// <summary>The announcement says a session ended, so one that never began announces nothing.</summary>
    [Fact]
    public async Task ForgetAsync_WithNobodySignedIn_AnnouncesNothing()
    {
        // Arrange
        var owner = new SignedInOwner(new StubOwnerCredentialStore());
        var announced = 0;

        owner.SignedInChanged += (_, _) => announced++;

        // Act
        await owner.ForgetAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, announced);
    }

    /// <summary>The point of keeping it: a second start on the same deployment opens already signed in.</summary>
    [Fact]
    public async Task RestoreAsync_ACredentialKeptForTheDeploymentItIsPointedAt_SignsThemIn()
    {
        // Arrange
        var store = new StubOwnerCredentialStore(
            held: new KeptOwnerCredential(Deployment, new OwnerCredential("ada", "a-long-password")));

        var owner = new SignedInOwner(store);

        // Act
        var restored = await owner.RestoreAsync(Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(restored);
        Assert.Equal("ada", owner.Username);
    }

    /// <summary>
    /// The address can move without this process seeing it, so a start reconciles what is stored against wherever the
    /// client came up pointed rather than trusting that the two still agree.
    /// </summary>
    [Fact]
    public async Task RestoreAsync_ACredentialKeptForAnotherDeployment_ClearsItAndSignsNobodyIn()
    {
        // Arrange
        var store = new StubOwnerCredentialStore(
            held: new KeptOwnerCredential(
                new Uri("https://elsewhere.example/"),
                new OwnerCredential("ada", "a-long-password")));

        var owner = new SignedInOwner(store);

        // Act
        var restored = await owner.RestoreAsync(Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(restored);
        Assert.False(owner.IsSignedIn);
        Assert.Null(store.Held);
    }

    /// <summary>A client pointed nowhere is pointed at no deployment, so a kept credential belongs to nowhere it is going.</summary>
    [Fact]
    public async Task RestoreAsync_WithNothingPointedAt_ClearsWhatWasKept()
    {
        // Arrange
        var store = new StubOwnerCredentialStore(
            held: new KeptOwnerCredential(Deployment, new OwnerCredential("ada", "a-long-password")));

        var owner = new SignedInOwner(store);

        // Act
        var restored = await owner.RestoreAsync(null, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(restored);
        Assert.Null(store.Held);
    }

    /// <summary>A head that keeps nothing restores nothing, and a start there is a start that asks.</summary>
    [Fact]
    public async Task RestoreAsync_OnAHeadThatKeepsNothing_SignsNobodyIn()
    {
        // Arrange
        var owner = new SignedInOwner(UnkeptOwnerCredentialStore.Instance);

        // Act
        var restored = await owner.RestoreAsync(Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(restored);
        Assert.False(owner.IsSignedIn);
    }

    /// <summary>The screen says a head will ask again by reading this rather than by knowing which head it is on.</summary>
    [Fact]
    public void Persistence_AHeadThatKeepsNothing_SaysSoRatherThanClaimingToKeepOne()
    {
        // Arrange
        var owner = new SignedInOwner(UnkeptOwnerCredentialStore.Instance);

        // Act, Assert
        Assert.Equal(CredentialPersistence.NotOfferedOnThisHead, owner.Persistence);
    }

    [Fact]
    public void Constructor_NoStore_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new SignedInOwner(null!));
    }
}
