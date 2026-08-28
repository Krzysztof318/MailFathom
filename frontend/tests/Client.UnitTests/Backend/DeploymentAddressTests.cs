// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Authorization;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Backend;

/// <summary>Where the client is pointed, and what moving it does to the session held against where it was.</summary>
/// <remarks>
/// The session claim is the one worth reading twice. A credential belongs to an owner on one deployment and means
/// nothing on another, so moving the client has to end it — and it has to end it <em>here</em> rather than at each
/// caller, because a caller that forgot would leave one deployment's password on a client now aimed at somebody else's
/// server.
/// </remarks>
public sealed class DeploymentAddressTests
{
    /// <summary>Somebody signed in, as a run that has already been through the sign-in screen would have them.</summary>
    private static async ValueTask<SignedInOwner> SignedInAt(Uri deployment, IOwnerCredentialStore store)
    {
        var owner = new SignedInOwner(store);

        await owner.AcceptAsync(
            deployment,
            new OwnerCredential("ada", "a-long-password"),
            TestContext.Current.CancellationToken);

        return owner;
    }

    [Fact]
    public void Current_NothingHasPointedIt_IsNowhereRatherThanADefault()
    {
        // Arrange
        var address = new DeploymentAddress(new SignedInOwner(UnkeptOwnerCredentialStore.Instance));

        // Act, Assert
        Assert.Null(address.Current);
        Assert.False(address.IsPointed);
    }

    [Fact]
    public async Task PointAtAsync_ADeployment_IsWhereTheClientIsPointed()
    {
        // Arrange
        var address = new DeploymentAddress(new SignedInOwner(UnkeptOwnerCredentialStore.Instance));

        // Act
        await address.PointAtAsync(new Uri("https://mail.example/"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new Uri("https://mail.example/"), address.Current);
        Assert.True(address.IsPointed);
    }

    [Fact]
    public async Task PointAtAsync_AnotherDeployment_EndsTheSessionHeldAgainstTheFirst()
    {
        // Arrange
        var store = new StubOwnerCredentialStore();
        var owner = await SignedInAt(new Uri("https://mail.example/"), store);
        var address = new DeploymentAddress(owner);

        await address.PointAtAsync(new Uri("https://mail.example/"), TestContext.Current.CancellationToken);

        // Act
        await address.PointAtAsync(new Uri("https://other.example/"), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(owner.IsSignedIn);
        Assert.Null(store.Held);
    }

    /// <summary>Every start re-reads what was kept, so pointing the client where it already is must not sign anybody out.</summary>
    [Fact]
    public async Task PointAtAsync_TheDeploymentItAlreadyReaches_LeavesTheSessionAlone()
    {
        // Arrange
        var store = new StubOwnerCredentialStore();
        var owner = await SignedInAt(new Uri("https://mail.example/"), store);
        var address = new DeploymentAddress(owner);

        await address.PointAtAsync(new Uri("https://mail.example/"), TestContext.Current.CancellationToken);

        // Act
        await address.PointAtAsync(new Uri("https://mail.example/"), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(owner.IsSignedIn);
        Assert.NotNull(store.Held);
    }

    /// <summary>The first pointing of a run is not a move, so a client composed and then pointed keeps whatever it was handed.</summary>
    [Fact]
    public async Task PointAtAsync_TheFirstDeploymentOfARun_LeavesTheSessionAlone()
    {
        // Arrange
        var store = new StubOwnerCredentialStore();
        var owner = await SignedInAt(new Uri("https://mail.example/"), store);
        var address = new DeploymentAddress(owner);

        // Act
        await address.PointAtAsync(new Uri("https://mail.example/"), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(owner.IsSignedIn);
        Assert.NotNull(store.Held);
    }

    [Theory]
    [InlineData("http://mail.example/")]
    [InlineData("https://mail.example/mailfathom/")]
    [InlineData("file:///home/somebody/mail")]
    public async Task PointAtAsync_AnAddressTheRuleRefuses_IsRefusedHereToo(string address)
    {
        // Arrange
        var pointed = new DeploymentAddress(new SignedInOwner(UnkeptOwnerCredentialStore.Instance));

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(
            "address",
            () => pointed.PointAtAsync(new Uri(address), TestContext.Current.CancellationToken).AsTask());

        Assert.False(pointed.IsPointed);
    }

    /// <summary>The judgement comes before anything is written, so a refused address moves nobody and signs nobody out.</summary>
    /// <remarks>
    /// The ordering is what this asserts rather than the refusal, which the theory above already covers. A client
    /// already reaching a deployment with somebody signed in against it is the state where getting the order wrong
    /// costs something: mutating first and judging afterwards would leave the client pointed at an address it just
    /// refused, and forgetting first would end a session over a value that never took effect.
    /// </remarks>
    [Fact]
    public async Task PointAtAsync_ARefusedAddressWhileAlreadyPointed_LeavesBothTheAddressAndTheSessionAlone()
    {
        // Arrange
        var store = new StubOwnerCredentialStore();
        var owner = await SignedInAt(new Uri("https://mail.example/"), store);
        var address = new DeploymentAddress(owner);

        await address.PointAtAsync(new Uri("https://mail.example/"), TestContext.Current.CancellationToken);

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(
            "address",
            () => address.PointAtAsync(new Uri("http://other.example/"), TestContext.Current.CancellationToken)
                .AsTask());

        Assert.Equal(new Uri("https://mail.example/"), address.Current);
        Assert.True(owner.IsSignedIn);
        Assert.NotNull(store.Held);
    }

    /// <summary>An exception message is read into a log, and the refusal most likely to carry a secret is the one for an address carrying one.</summary>
    [Fact]
    public async Task PointAtAsync_AnAddressCarryingEmbeddedCredentials_RefusesItWithoutNamingTheSecret()
    {
        // Arrange
        var address = new DeploymentAddress(new SignedInOwner(UnkeptOwnerCredentialStore.Instance));

        // Act
        var failure = await Assert.ThrowsAsync<ArgumentException>(
            "address",
            () => address.PointAtAsync(
                    new Uri("https://somebody:secret@mail.example/"),
                    TestContext.Current.CancellationToken)
                .AsTask());

        // Assert
        Assert.DoesNotContain("secret", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("somebody", failure.Message, StringComparison.Ordinal);
        Assert.Contains("https://mail.example", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Construct_NoSignedInOwner_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new DeploymentAddress(null!));
    }
}
