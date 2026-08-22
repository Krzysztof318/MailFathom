// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Client.Backend;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Backend.Authorization;

/// <summary>
/// The whole sign-in, with the one head-specific step stubbed: discovery, the authorization request, the proof key, and
/// the exchange. No browser, no socket, and no visual tree is involved in any of it.
/// </summary>
public sealed class DeploymentSignInTests
{
    private const string ResourceIdentifier = "https://mail.example/api/client";

    private const string Issuer = "https://issuer.example";

    private const string PublishedMetadata =
        $$"""
        {"resource":"{{ResourceIdentifier}}","authorization_servers":["{{Issuer}}"],"scopes_supported":["mail.read","mail.send"]}
        """;

    private const string PublishedDiscovery =
        $$"""
        {"issuer":"{{Issuer}}","authorization_endpoint":"{{Issuer}}/authorize","token_endpoint":"{{Issuer}}/token"}
        """;

    [Fact]
    public async Task SignInAsync_AnApprovedSignIn_HoldsTheIssuedTokenForThisRun()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            Publishing(PublishedMetadata),
            IssuingTokens(),
            StubSignInRedirectListener.Approved);

        // Act
        await harness.SignIn.SignInAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(harness.Tokens.IsSignedIn);
        Assert.True(harness.Listener.Disposed);
    }

    [Fact]
    public async Task SignInAsync_TheAuthorizationRequest_BindsTheGrantToAProofKeyAndToThisDeployment()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            Publishing(PublishedMetadata),
            IssuingTokens(),
            StubSignInRedirectListener.Approved);

        // Act
        await harness.SignIn.SignInAsync(TestContext.Current.CancellationToken);

        // Assert
        var query = harness.Listener.OpenedAuthorizationUrl!.Query;

        Assert.Contains("response_type=code", query, StringComparison.Ordinal);
        Assert.Contains("code_challenge_method=S256", query, StringComparison.Ordinal);
        Assert.Contains("code_challenge=", query, StringComparison.Ordinal);
        Assert.Contains("client_id=the-client", query, StringComparison.Ordinal);
        Assert.Contains($"resource={Uri.EscapeDataString(ResourceIdentifier)}", query, StringComparison.Ordinal);
        Assert.Contains(
            $"scope={Uri.EscapeDataString("mail.read mail.send")}",
            query,
            StringComparison.Ordinal);
        Assert.Contains(
            $"redirect_uri={Uri.EscapeDataString(harness.Listener.RedirectUri.AbsoluteUri)}",
            query,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignInAsync_TheAuthorizationRequest_NeverCarriesTheVerifierItself()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            Publishing(PublishedMetadata),
            IssuingTokens(),
            StubSignInRedirectListener.Approved);

        // Act
        await harness.SignIn.SignInAsync(TestContext.Current.CancellationToken);

        // Assert
        // The secret half of the pair goes to the token endpoint over a connection this process opened, never through
        // the person's browser — which is the whole of what makes an intercepted code useless.
        Assert.DoesNotContain(
            "code_verifier",
            harness.Listener.OpenedAuthorizationUrl!.Query,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignInAsync_TheExchange_RedeemsWithTheVerifierAndWithNoClientSecret()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            Publishing(PublishedMetadata),
            IssuingTokens(),
            StubSignInRedirectListener.Approved);

        // Act
        await harness.SignIn.SignInAsync(TestContext.Current.CancellationToken);

        // Assert
        var exchange = Assert.Single(harness.AuthorizationServer.Requests, request => request.Body is not null);

        Assert.Contains("grant_type=authorization_code", exchange.Body!, StringComparison.Ordinal);
        Assert.Contains($"code={StubSignInRedirectListener.ApprovedCode}", exchange.Body, StringComparison.Ordinal);
        Assert.Contains("code_verifier=", exchange.Body, StringComparison.Ordinal);
        Assert.Contains(
            $"resource={Uri.EscapeDataString(ResourceIdentifier)}",
            exchange.Body,
            StringComparison.Ordinal);
        Assert.DoesNotContain("client_secret", exchange.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignInAsync_ARedirectEchoingSomethingElse_IsNeverRedeemed()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            Publishing(PublishedMetadata),
            IssuingTokens(),
            StubSignInRedirectListener.ApprovedForSomeOtherRequest);

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.SignIn.SignInAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.Unusable, failure.Reason);
        Assert.False(harness.Tokens.IsSignedIn);
        Assert.DoesNotContain(harness.AuthorizationServer.Requests, request => request.Body is not null);
    }

    [Fact]
    public async Task SignInAsync_ARefusalForSomeOtherRequest_IsNotActedOnEither()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            Publishing(PublishedMetadata),
            IssuingTokens(),
            StubSignInRedirectListener.RefusedForSomeOtherRequest);

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.SignIn.SignInAsync(TestContext.Current.CancellationToken));

        // Assert
        // The state is compared before the error is read, so anything that can navigate a browser cannot end a sign-in
        // it did not start.
        Assert.Equal(DeploymentFailureReason.Unusable, failure.Reason);
    }

    [Fact]
    public async Task SignInAsync_AnApprovalTheServerRefused_ReportsARefusedCredential()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            Publishing(PublishedMetadata),
            IssuingTokens(),
            StubSignInRedirectListener.Refused);

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.SignIn.SignInAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.CredentialRefused, failure.Reason);
    }

    [Fact]
    public async Task SignInAsync_ATokenEndpointRefusingTheCode_ReadsTheBodyRatherThanTheStatus()
    {
        // Arrange
        // RFC 6749 requires a rejected grant to arrive as 400 with a machine-readable error, which is the shape here.
        using var harness = new DeploymentHarness(
            Publishing(PublishedMetadata),
            request => request.Method == HttpMethod.Post
                ? StubTransport.JsonResponse("""{"error":"invalid_grant"}""", HttpStatusCode.BadRequest)
                : StubTransport.JsonResponse(PublishedDiscovery),
            StubSignInRedirectListener.Approved);

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.SignIn.SignInAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.CredentialRefused, failure.Reason);
        Assert.False(harness.Tokens.IsSignedIn);
    }

    [Fact]
    public async Task SignInAsync_ATokenEndpointAnsweringWithoutAToken_LeavesNobodySignedIn()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            Publishing(PublishedMetadata),
            request => request.Method == HttpMethod.Post
                ? StubTransport.JsonResponse("""{"expires_in":3600}""")
                : StubTransport.JsonResponse(PublishedDiscovery),
            StubSignInRedirectListener.Approved);

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.SignIn.SignInAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.Unusable, failure.Reason);
        Assert.False(harness.Tokens.IsSignedIn);
    }

    [Fact]
    public async Task SignInAsync_ADeploymentPublishingNoMetadata_SaysSoRatherThanGuessingWhereToSignIn()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            _ => StubTransport.JsonResponse("{}", HttpStatusCode.NotFound),
            IssuingTokens(),
            StubSignInRedirectListener.Approved);

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.SignIn.SignInAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.Unusable, failure.Reason);
        Assert.Null(harness.Listener.OpenedAuthorizationUrl);
    }

    [Fact]
    public async Task SignInAsync_ADeploymentNamingSeveralAuthorizationServers_RefusesToChooseBetweenThem()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            Publishing(
                $$"""
                {"resource":"{{ResourceIdentifier}}","authorization_servers":["{{Issuer}}","https://other.example"],"scopes_supported":[]}
                """),
            IssuingTokens(),
            StubSignInRedirectListener.Approved);

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.SignIn.SignInAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.Unusable, failure.Reason);
    }

    [Fact]
    public async Task SignInAsync_ADeploymentNamingNoAuthorizationServer_SaysThereIsNowhereToSignIn()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            Publishing(
                $$"""
                {"resource":"{{ResourceIdentifier}}","authorization_servers":[],"scopes_supported":[]}
                """),
            IssuingTokens(),
            StubSignInRedirectListener.Approved);

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.SignIn.SignInAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.Unusable, failure.Reason);
    }

    [Fact]
    public async Task SignInAsync_ADiscoveryDocumentReportingADifferentIssuer_IsNotFollowed()
    {
        // Arrange
        // RFC 8414 section 3.3: a document that does not report the issuer that led to it is not that issuer's. Without
        // the check, a document served at a guessable address could move the sign-in to somebody else's login page.
        using var harness = new DeploymentHarness(
            Publishing(PublishedMetadata),
            _ => StubTransport.JsonResponse(
                """
                {"issuer":"https://impostor.example","authorization_endpoint":"https://impostor.example/authorize","token_endpoint":"https://impostor.example/token"}
                """),
            StubSignInRedirectListener.Approved);

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.SignIn.SignInAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.Unreachable, failure.Reason);
        Assert.Null(harness.Listener.OpenedAuthorizationUrl);
    }

    [Fact]
    public async Task SignInAsync_AnEndpointOverPlainHttp_IsReadAsAbsentRatherThanFollowed()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            Publishing(PublishedMetadata),
            _ => StubTransport.JsonResponse(
                $$"""
                {"issuer":"{{Issuer}}","authorization_endpoint":"{{Issuer}}/authorize","token_endpoint":"http://issuer.example/token"}
                """),
            StubSignInRedirectListener.Approved);

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.SignIn.SignInAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.Unusable, failure.Reason);
    }

    [Fact]
    public async Task SignInAsync_AnUnreachableDeployment_ReportsItAsUnreachableRatherThanAsARefusal()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            _ => throw new HttpRequestException("Connection refused."),
            IssuingTokens(),
            StubSignInRedirectListener.Approved);

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.SignIn.SignInAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.Unreachable, failure.Reason);
    }

    [Fact]
    public async Task SignInAsync_ADeploymentThatDoesNotAnswerInTime_ReportsATimeout()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            _ => throw new TaskCanceledException("The request timed out."),
            IssuingTokens(),
            StubSignInRedirectListener.Approved);

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.SignIn.SignInAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.TimedOut, failure.Reason);
    }

    [Fact]
    public async Task SignInAsync_AnAbandonedSignIn_ReleasesWhatTheHeadReservedForTheRedirect()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            Publishing(PublishedMetadata),
            IssuingTokens(),
            StubSignInRedirectListener.Refused);

        // Act
        await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.SignIn.SignInAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.True(harness.Listener.Disposed);
    }

    [Fact]
    public async Task SignInAsync_AnAuthorizationEndpointPublishingAQuery_KeepsItRatherThanReplacingIt()
    {
        // Arrange
        // A tenant that routes to a named policy through the endpoint address itself, which is how the largest
        // deployments of this grant publish it.
        const string PublishedWithAPolicy =
            $$"""
            {"issuer":"{{Issuer}}","authorization_endpoint":"{{Issuer}}/authorize?p=b2c_1_signin","token_endpoint":"{{Issuer}}/token"}
            """;

        using var harness = new DeploymentHarness(
            Publishing(PublishedMetadata),
            request => request.Method == HttpMethod.Post
                ? StubTransport.JsonResponse("""{"access_token":"the-token","expires_in":3600}""")
                : StubTransport.JsonResponse(PublishedWithAPolicy),
            StubSignInRedirectListener.Approved);

        // Act
        await harness.SignIn.SignInAsync(TestContext.Current.CancellationToken);

        // Assert
        var query = harness.Listener.OpenedAuthorizationUrl!.Query;

        Assert.Contains("p=b2c_1_signin", query, StringComparison.Ordinal);
        Assert.Contains("response_type=code", query, StringComparison.Ordinal);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Publishing(string metadata) =>
        _ => StubTransport.JsonResponse(metadata);

    private static Func<HttpRequestMessage, HttpResponseMessage> IssuingTokens() =>
        request => request.Method == HttpMethod.Post
            ? StubTransport.JsonResponse("""{"access_token":"the-token","expires_in":3600}""")
            : StubTransport.JsonResponse(PublishedDiscovery);
}
