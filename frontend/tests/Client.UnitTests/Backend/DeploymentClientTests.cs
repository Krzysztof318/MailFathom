// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Client.Backend;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Backend;

/// <summary>What the client makes of what a deployment answers, and of a deployment that answers nothing.</summary>
public sealed class DeploymentClientTests
{
    private const string AnAnsweringDeployment =
        """
        {"service":"MailFathom","version":"0.8.0","credential":"the-client","permissions":["mail.read","mail.send"]}
        """;

    private const string ACallerGrantedNothing =
        """
        {"service":"MailFathom","version":"0.8.0","credential":"anonymous","permissions":[]}
        """;

    [Fact]
    public async Task ReadSessionAsync_ADeploymentAnswering_ReadsEveryFieldOfTheContract()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(AnAnsweringDeployment));

        // Act
        var session = await harness.Client.ReadSessionAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("MailFathom", session.Service);
        Assert.Equal("0.8.0", session.Version);
        Assert.Equal("the-client", session.Credential);
        Assert.Equal(["mail.read", "mail.send"], session.Permissions);
    }

    [Fact]
    public async Task ReadSessionAsync_AnyRequest_GoesToTheClientSurfaceRatherThanTheAdministrativeOne()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(ACallerGrantedNothing));

        // Act
        await harness.Client.ReadSessionAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            new Uri("https://mail.example/api/client/session"),
            Assert.Single(harness.Deployment.Requests).RequestUri);
    }

    [Fact]
    public async Task ReadSessionAsync_ACallerGrantedNothing_ReadsTheEmptyGrantRatherThanFailing()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(ACallerGrantedNothing));

        // Act
        var session = await harness.Client.ReadSessionAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(session.Permissions);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ReadSessionAsync_ARefusedCredential_ReportsTheOneCaseThePersonCanActOn(HttpStatusCode refusal)
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse("{}", refusal));

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.Client.ReadSessionAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.CredentialRefused, failure.Reason);
    }

    [Fact]
    public async Task ReadSessionAsync_TheClientsOwnTimeoutElapsing_IsReportedAsATimeoutRatherThanACancellation()
    {
        // Arrange
        // What HttpClient raises when its own timeout elapses: a cancellation nobody asked for.
        using var harness = new DeploymentHarness(_ => throw new TaskCanceledException("The request timed out."));

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.Client.ReadSessionAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.TimedOut, failure.Reason);
    }

    [Fact]
    public async Task ReadSessionAsync_ACallerCancelling_IsNotReportedAsATimeout()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        using var harness = new DeploymentHarness(_ => throw new TaskCanceledException("Cancelled."));

        await cancellation.CancelAsync();

        // Act, Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Client.ReadSessionAsync(cancellation.Token));
    }

    [Fact]
    public async Task ReadSessionAsync_AnUnreachableDeployment_IsDistinguishedFromOneThatRefused()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => throw new HttpRequestException("Name or service not known."));

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.Client.ReadSessionAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.Unreachable, failure.Reason);
    }

    [Fact]
    public async Task ReadSessionAsync_SomethingElseAnsweringThePort_ReportsAnUnusableAnswerRatherThanParsingIt()
    {
        // Arrange
        // A captive portal, a proxy, or a login page — the shape a mistyped address actually produces.
        using var harness = new DeploymentHarness(
            _ => StubTransport.JsonResponse("<!DOCTYPE html><html><body>Sign in</body></html>"));

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.Client.ReadSessionAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.Unusable, failure.Reason);
    }

    [Fact]
    public async Task ReadSessionAsync_AnAnswerLargerThanThisClientWillRead_IsRefusedOnTheDeclaredLength()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            _ => StubTransport.JsonResponse(
                $$"""{"service":"MailFathom","version":"{{new string('0', DeploymentExchange.MaxDocumentBytes)}}"}"""));

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.Client.ReadSessionAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.Unusable, failure.Reason);
    }

    [Fact]
    public async Task ReadSessionAsync_NobodySignedIn_PresentsNoCredentialRatherThanRefusingToAsk()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            _ => StubTransport.JsonResponse(ACallerGrantedNothing),
            throughTokenHandler: true);

        // Act
        await harness.Client.ReadSessionAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(harness.Tokens.IsSignedIn);
        Assert.Null(Assert.Single(harness.Deployment.Requests).Authorization);
    }

    [Fact]
    public async Task ReadSessionAsync_AfterSigningIn_PresentsTheTokenInTheHeaderAndNowhereElse()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            _ => StubTransport.JsonResponse(AnAnsweringDeployment),
            throughTokenHandler: true);

        harness.Tokens.Accept("the-token");

        // Act
        await harness.Client.ReadSessionAsync(TestContext.Current.CancellationToken);

        // Assert
        var request = Assert.Single(harness.Deployment.Requests);

        Assert.Equal("Bearer the-token", request.Authorization);
        Assert.DoesNotContain("the-token", request.RequestUri.ToString(), StringComparison.Ordinal);
    }
}
