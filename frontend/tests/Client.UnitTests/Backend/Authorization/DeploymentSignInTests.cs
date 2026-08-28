// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text;
using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Authorization;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Backend.Authorization;

/// <summary>Offering an owner's username and password to a deployment, and what each of its answers means.</summary>
public sealed class DeploymentSignInTests
{
    private const string SessionDocument =
        """{"service":"MailFathom","version":"0.8.0","permissions":["mailfathom.mail.read"]}""";

    /// <summary>The refusal a deployment that accepts passwords answers with, which is what invites one.</summary>
    private static HttpResponseMessage RefusedWithAPasswordChallenge()
    {
        var refusal = new HttpResponseMessage(HttpStatusCode.Unauthorized);

        refusal.Headers.TryAddWithoutValidation("WWW-Authenticate", "Bearer");
        refusal.Headers.TryAddWithoutValidation("WWW-Authenticate", "Basic realm=\"MailFathom\", charset=\"UTF-8\"");

        return refusal;
    }

    /// <summary>The refusal a deployment with no password method configured answers with.</summary>
    private static HttpResponseMessage RefusedWithoutOne()
    {
        var refusal = new HttpResponseMessage(HttpStatusCode.Unauthorized);

        refusal.Headers.TryAddWithoutValidation("WWW-Authenticate", "Bearer");

        return refusal;
    }

    [Fact]
    public async Task SignInAsync_WithACredentialTheDeploymentAccepts_SignsIn()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(SessionDocument),
            store: new StubOwnerCredentialStore(),
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var attempt = await harness.SignIn.SignInAsync(
            new OwnerCredential("ada", "a-long-password"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SignInResult.Accepted, attempt.Result);
        Assert.True(harness.Owner.IsSignedIn);
        Assert.Equal("ada", harness.Owner.Username);
    }

    /// <summary>RFC 7617: the two halves in one Base64 field, separated by the first colon and encoded as UTF-8.</summary>
    [Fact]
    public async Task SignInAsync_PresentsTheCredentialAsAnRfc7617BasicHeader()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(SessionDocument),
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await harness.SignIn.SignInAsync(
            new OwnerCredential("ada", "hasło:z:dwukropkami"),
            TestContext.Current.CancellationToken);

        // Assert
        var offered = Assert.Single(harness.SignedInAt.Requests);

        Assert.Equal(
            $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes("ada:hasło:z:dwukropkami"))}",
            offered.Authorization);
    }

    /// <summary>The route it offers the credential to is the session one, which answers about whatever was presented.</summary>
    [Fact]
    public async Task SignInAsync_OffersTheCredentialToTheSessionRouteOfTheDeploymentItIsPointedAt()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(SessionDocument),
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await harness.SignIn.SignInAsync(
            new OwnerCredential("ada", "a-long-password"),
            TestContext.Current.CancellationToken);

        // Assert
        var offered = Assert.Single(harness.SignedInAt.Requests);

        Assert.Equal(new Uri("https://mail.example/api/client/session"), offered.RequestUri);
    }

    /// <summary>A refused credential leaves whoever was signed in exactly as they were.</summary>
    [Fact]
    public async Task SignInAsync_WithACredentialTheDeploymentRefuses_LeavesNobodySignedIn()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => RefusedWithAPasswordChallenge(),
            store: new StubOwnerCredentialStore(),
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var attempt = await harness.SignIn.SignInAsync(
            new OwnerCredential("ada", "the-wrong-password"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SignInResult.CredentialRefused, attempt.Result);
        Assert.False(harness.Owner.IsSignedIn);
    }

    /// <summary>
    /// A deployment whose operator never enabled password sign-in is not a wrong password, and the challenge is what
    /// says so: a surface with the method configured names it, and one without it does not.
    /// </summary>
    [Fact]
    public async Task SignInAsync_WithARefusalThatInvitesNoPassword_ReportsThatTheDeploymentOffersNone()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => RefusedWithoutOne(),
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var attempt = await harness.SignIn.SignInAsync(
            new OwnerCredential("ada", "a-long-password"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SignInResult.PasswordSignInNotOffered, attempt.Result);
    }

    /// <summary>Anything can answer 200 on a port, so the answer has to be MailFathom's own document.</summary>
    [Fact]
    public async Task SignInAsync_WithSomethingThatIsNotMailFathomAnswering_RefusesTheAnswer()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse("""{"service":"Something Else","version":"1","permissions":[]}"""),
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var refusal = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.SignIn.SignInAsync(
                new OwnerCredential("ada", "a-long-password"),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.Unusable, refusal.Reason);
        Assert.False(harness.Owner.IsSignedIn);
    }

    /// <summary>A client pointed nowhere has nothing to sign in to, and says so rather than composing an address.</summary>
    [Fact]
    public async Task SignInAsync_WithNothingPointedAt_Refuses()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(SessionDocument),
            pointed: false,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.SignIn.SignInAsync(
                new OwnerCredential("ada", "a-long-password"),
                TestContext.Current.CancellationToken));
    }

    /// <summary>Where the head keeps a credential, an accepted one is kept beside the deployment it was accepted by.</summary>
    [Fact]
    public async Task SignInAsync_OnAHeadThatKeepsACredential_KeepsItForTheDeploymentItWasAcceptedBy()
    {
        // Arrange
        var store = new StubOwnerCredentialStore();

        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(SessionDocument), store: store,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var attempt = await harness.SignIn.SignInAsync(
            new OwnerCredential("ada", "a-long-password"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CredentialPersistence.Kept, attempt.Persistence);
        Assert.Equal(DeploymentHarness.DeploymentAddress, store.Held?.Deployment);
        Assert.Equal("ada", store.Held?.Credential.Username);
    }

    /// <summary>A store that refuses is reported rather than thrown: the sign-in succeeded, and the next start will ask again.</summary>
    [Fact]
    public async Task SignInAsync_WhereTheStoreRefuses_SignsInAndSaysTheNextStartWillAsk()
    {
        // Arrange
        var store = new StubOwnerCredentialStore(CredentialPersistence.StoreUnavailable);

        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(SessionDocument), store: store,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var attempt = await harness.SignIn.SignInAsync(
            new OwnerCredential("ada", "a-long-password"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SignInResult.Accepted, attempt.Result);
        Assert.Equal(CredentialPersistence.StoreUnavailable, attempt.Persistence);
        Assert.True(harness.Owner.IsSignedIn);
        Assert.Null(store.Held);
    }

    /// <summary>Signing out ends the session here and clears what the head kept, without asking the deployment anything.</summary>
    [Fact]
    public async Task SignOutAsync_ClearsWhatIsHeldAndWhatWasKept()
    {
        // Arrange
        var store = new StubOwnerCredentialStore();

        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(SessionDocument), store: store,
            cancellationToken: TestContext.Current.CancellationToken);

        await harness.SignIn.SignInAsync(
            new OwnerCredential("ada", "a-long-password"),
            TestContext.Current.CancellationToken);

        var asked = harness.SignedInAt.Requests.Count;

        // Act
        await harness.SignIn.SignOutAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(harness.Owner.IsSignedIn);
        Assert.Null(store.Held);
        Assert.Equal(asked, harness.SignedInAt.Requests.Count);
    }

    /// <summary>A start whose head kept a credential for the deployment it comes up pointed at opens already signed in.</summary>
    [Fact]
    public async Task RestoreAsync_WithACredentialKeptForThisDeployment_SignsInWithoutAsking()
    {
        // Arrange
        var store = new StubOwnerCredentialStore(
            held: new KeptOwnerCredential(
                DeploymentHarness.DeploymentAddress,
                new OwnerCredential("ada", "a-long-password")));

        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(SessionDocument), store: store,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var restored = await harness.SignIn.RestoreAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(restored);
        Assert.Equal("ada", harness.Owner.Username);
        Assert.Empty(harness.SignedInAt.Requests);
    }

    /// <summary>A credential kept for a deployment the client is no longer pointed at is cleared rather than presented.</summary>
    [Fact]
    public async Task RestoreAsync_WithACredentialKeptForAnotherDeployment_ClearsItAndSignsNobodyIn()
    {
        // Arrange
        var store = new StubOwnerCredentialStore(
            held: new KeptOwnerCredential(
                new Uri("https://elsewhere.example/"),
                new OwnerCredential("ada", "a-long-password")));

        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(SessionDocument), store: store,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var restored = await harness.SignIn.RestoreAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(restored);
        Assert.False(harness.Owner.IsSignedIn);
        Assert.Null(store.Held);
    }

    /// <summary>A start that resolved no address at all clears whatever was kept, for the same reason.</summary>
    [Fact]
    public async Task RestoreAsync_WithNothingPointedAt_ClearsWhatWasKept()
    {
        // Arrange
        var store = new StubOwnerCredentialStore(
            held: new KeptOwnerCredential(
                DeploymentHarness.DeploymentAddress,
                new OwnerCredential("ada", "a-long-password")));

        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(SessionDocument),
            store: store,
            pointed: false,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var restored = await harness.SignIn.RestoreAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(restored);
        Assert.Null(store.Held);
    }
}
