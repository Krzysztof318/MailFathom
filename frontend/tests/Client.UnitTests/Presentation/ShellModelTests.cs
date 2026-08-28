// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Authorization;
using MailFathom.Client.Presentation;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Presentation;

/// <summary>The rule no single screen could hold: a session that has ended puts the person in front of the sign-in.</summary>
/// <remarks>
/// Three different things end one and none of them belongs to a screen — signing out, a deployment that stops accepting
/// a credential it once accepted, and the client being pointed elsewhere. All three are the same announcement, which is
/// why one test of each would be three tests of one thing; what is asserted here is the answer to the announcement.
/// </remarks>
public sealed class ShellModelTests
{
    private static readonly Uri Deployment = new("https://mail.example/");

    /// <summary>Waits for what the shell starts rather than awaits, which is one turn of the scheduler.</summary>
    private static async Task<NavigationRequest?> WhereItPut(StubNavigator navigator)
    {
        for (var attempt = 0; attempt < 20 && navigator.Requests.Count == 0; attempt++)
        {
            await Task.Yield();
        }

        return navigator.Requests.FirstOrDefault();
    }

    [Fact]
    public async Task SignedInChanged_ASessionThatHasEnded_PutsThePersonInFrontOfTheSignIn()
    {
        // Arrange
        var owner = new SignedInOwner(new StubOwnerCredentialStore());
        var address = new DeploymentAddress(owner);
        var navigator = new StubNavigator();

        await address.PointAtAsync(Deployment, TestContext.Current.CancellationToken);

        await owner.AcceptAsync(
            Deployment,
            new OwnerCredential("ada", "a-long-password"),
            TestContext.Current.CancellationToken);

        await using var shell = new ShellModel(owner, address, navigator);

        // Act
        await owner.ForgetAsync(TestContext.Current.CancellationToken);

        // Assert
        var request = await WhereItPut(navigator);

        Assert.NotNull(request);
        Assert.Equal(ClientRoutes.SignIn, request.Route.Base);
    }

    /// <summary>What is behind the sign-in is a session that no longer exists, so the back gesture may not reach it.</summary>
    [Fact]
    public async Task SignedInChanged_ASessionThatHasEnded_LeavesNothingBehindToGoBackTo()
    {
        // Arrange
        var owner = new SignedInOwner(new StubOwnerCredentialStore());
        var address = new DeploymentAddress(owner);
        var navigator = new StubNavigator();

        await address.PointAtAsync(Deployment, TestContext.Current.CancellationToken);

        await owner.AcceptAsync(
            Deployment,
            new OwnerCredential("ada", "a-long-password"),
            TestContext.Current.CancellationToken);

        await using var shell = new ShellModel(owner, address, navigator);

        // Act
        await owner.ForgetAsync(TestContext.Current.CancellationToken);

        // Assert
        var request = await WhereItPut(navigator);

        Assert.NotNull(request);
        Assert.Equal(Qualifiers.ClearBackStack, request.Route.Qualifier);
    }

    /// <summary>A completed sign-in raises the same announcement, and it is the one case that must move nothing.</summary>
    [Fact]
    public async Task SignedInChanged_SomebodySigningIn_MovesNothing()
    {
        // Arrange
        var owner = new SignedInOwner(new StubOwnerCredentialStore());
        var address = new DeploymentAddress(owner);
        var navigator = new StubNavigator();

        await address.PointAtAsync(Deployment, TestContext.Current.CancellationToken);

        await using var shell = new ShellModel(owner, address, navigator);

        // Act
        await owner.AcceptAsync(
            Deployment,
            new OwnerCredential("ada", "a-long-password"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(navigator.Requests);
    }

    /// <summary>
    /// A client pointed nowhere has no deployment to sign in to, and whoever pointed it away is already on the screen
    /// that asks for one.
    /// </summary>
    [Fact]
    public async Task SignedInChanged_AClientPointedNowhere_LeavesThePersonWhereTheyAre()
    {
        // Arrange
        var owner = new SignedInOwner(new StubOwnerCredentialStore());
        var address = new DeploymentAddress(owner);
        var navigator = new StubNavigator();

        await owner.AcceptAsync(
            Deployment,
            new OwnerCredential("ada", "a-long-password"),
            TestContext.Current.CancellationToken);

        await using var shell = new ShellModel(owner, address, navigator);

        // Act
        await owner.ForgetAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(navigator.Requests);
    }

    [Fact]
    public void Constructor_AMissingService_IsRefused()
    {
        // Arrange
        var owner = new SignedInOwner(UnkeptOwnerCredentialStore.Instance);
        var address = new DeploymentAddress(owner);
        var navigator = new StubNavigator();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new ShellModel(null!, address, navigator));
        Assert.Throws<ArgumentNullException>(() => new ShellModel(owner, null!, navigator));
        Assert.Throws<ArgumentNullException>(() => new ShellModel(owner, address, null!));
    }
}
