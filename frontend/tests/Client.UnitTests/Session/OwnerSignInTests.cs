// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Client.Backend.Authorization;
using MailFathom.Client.Session;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Session;

/// <summary>The one place everything a sign-in can produce becomes a case the screen has a sentence for.</summary>
public sealed class OwnerSignInTests
{
    private const string SessionDocument =
        """{"service":"MailFathom","version":"0.8.0","permissions":["mailfathom.mail.read"]}""";

    /// <summary>The refusal a deployment offering password sign-in answers a wrong credential with.</summary>
    private static HttpResponseMessage RefusedWithAPasswordChallenge()
    {
        var refusal = new HttpResponseMessage(HttpStatusCode.Unauthorized);

        refusal.Headers.TryAddWithoutValidation("WWW-Authenticate", "Basic realm=\"MailFathom\", charset=\"UTF-8\"");

        return refusal;
    }

    [Fact]
    public async Task SignInAsync_ACredentialTheDeploymentAccepts_IsAccepted()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(SessionDocument),
            store: new StubOwnerCredentialStore());

        var signIn = new OwnerSignIn(harness.SignIn, harness.Owner);

        // Act
        var attempt = await signIn.SignInAsync("ada", "a-long-password", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SignInOutcome.Accepted, attempt.Outcome);
        Assert.Equal(CredentialPersistence.Kept, attempt.Persistence);
        Assert.Equal("ada", signIn.Username);
    }

    /// <summary>
    /// Refused before anything is sent, so what nobody finished typing costs the deployment nothing and reaches the
    /// person while they are still looking at what they wrote.
    /// </summary>
    [Theory]
    [InlineData(null, "a-long-password")]
    [InlineData("ada", null)]
    [InlineData("", "a-long-password")]
    [InlineData("   ", "a-long-password")]
    [InlineData("ada", "")]
    [InlineData("ada:lovelace", "a-long-password")]
    public async Task SignInAsync_SomethingThatIsNotACredential_SaysSoAndContactsNothing(
        string? username,
        string? password)
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(_ => StubTransport.JsonResponse(SessionDocument));

        var signIn = new OwnerSignIn(harness.SignIn, harness.Owner);

        // Act
        var attempt = await signIn.SignInAsync(username, password, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SignInOutcome.NotACredential, attempt.Outcome);
        Assert.Empty(harness.SignedInAt.Requests);
    }

    /// <summary>
    /// One case rather than two: the deployment refuses an unknown username and a wrong password identically, and a
    /// client that guessed which had happened would be inventing a distinction the service deliberately does not make.
    /// </summary>
    [Fact]
    public async Task SignInAsync_ACredentialTheDeploymentRefuses_SaysOneThingAboutBothHalves()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(_ => RefusedWithAPasswordChallenge());

        var signIn = new OwnerSignIn(harness.SignIn, harness.Owner);

        // Act
        var attempt = await signIn.SignInAsync("ada", "the-wrong-password", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SignInOutcome.CredentialRefused, attempt.Outcome);
    }

    /// <summary>A deployment whose operator never enabled password sign-in is not a wrong password.</summary>
    [Fact]
    public async Task SignInAsync_ADeploymentOfferingNoPassword_IsNotAWrongCredential()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var signIn = new OwnerSignIn(harness.SignIn, harness.Owner);

        // Act
        var attempt = await signIn.SignInAsync("ada", "a-long-password", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SignInOutcome.PasswordSignInNotOffered, attempt.Outcome);
    }

    [Fact]
    public async Task SignInAsync_ADeploymentNothingAnswersAt_SaysItWasNotReached()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(_ => throw new HttpRequestException("no route to host"));

        var signIn = new OwnerSignIn(harness.SignIn, harness.Owner);

        // Act
        var attempt = await signIn.SignInAsync("ada", "a-long-password", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SignInOutcome.Unreachable, attempt.Outcome);
    }

    [Fact]
    public async Task SignInAsync_SomethingThatIsNotAMailFathom_SaysSoRatherThanBlamingTheCredential()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse("""{"service":"SomebodyElse","version":"3.1","permissions":[]}"""));

        var signIn = new OwnerSignIn(harness.SignIn, harness.Owner);

        // Act
        var attempt = await signIn.SignInAsync("ada", "a-long-password", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SignInOutcome.NotADeployment, attempt.Outcome);
    }

    /// <summary>Nothing this seam produces carries either half of what was typed, whichever way the attempt went.</summary>
    [Fact]
    public async Task SignInAsync_ARefusedCredential_ReportsNeitherHalfOfWhatWasTyped()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(_ => RefusedWithAPasswordChallenge());

        var signIn = new OwnerSignIn(harness.SignIn, harness.Owner);

        // Act
        var attempt = await signIn.SignInAsync("ada", "a-long-password", TestContext.Current.CancellationToken);

        // Assert
        var rendered = $"{attempt}";

        Assert.DoesNotContain("a-long-password", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("ada", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignOutAsync_AfterSigningIn_LeavesNobodySignedInAndNothingKept()
    {
        // Arrange
        var store = new StubOwnerCredentialStore();

        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(SessionDocument),
            store: store);

        var signIn = new OwnerSignIn(harness.SignIn, harness.Owner);

        await signIn.SignInAsync("ada", "a-long-password", TestContext.Current.CancellationToken);

        // Act
        await signIn.SignOutAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(signIn.Username);
        Assert.Null(store.Held);
    }

    [Fact]
    public async Task Constructor_AMissingService_IsRefused()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(_ => StubTransport.JsonResponse(SessionDocument));

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new OwnerSignIn(null!, harness.Owner));
        Assert.Throws<ArgumentNullException>(() => new OwnerSignIn(harness.SignIn, null!));
    }
}
