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
        {"service":"MailFathom","version":"0.8.0","permissions":["mailfathom.mail.read","mailfathom.mail.send"]}
        """;

    private const string ACallerGrantedNothing =
        """
        {"service":"MailFathom","version":"0.8.0","permissions":[]}
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
        Assert.Equal(["mailfathom.mail.read", "mailfathom.mail.send"], session.Permissions);
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
    public async Task ReadSessionAsync_AnAnswerRunningPastTheBufferWithoutDeclaringItsLength_IsUnusableRatherThanUnreachable()
    {
        // Arrange
        // What the transport raises when MaxResponseContentBufferSize is passed while the body is buffered, which is
        // the only signal an answer that declared no length ever gives.
        using var harness = new DeploymentHarness(
            _ => throw new HttpRequestException(
                HttpRequestError.ConfigurationLimitExceeded,
                "Cannot write more bytes to the buffer than the configured maximum buffer size."));

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

    /// <summary>A body is the one document here that carries a message's own pictures, so it reads past the ordinary bound.</summary>
    /// <remarks>
    /// Every other route reads a description of something and is bounded at a megabyte. Holding a body to that number
    /// would refuse a message carrying one ordinary photograph, and refuse it as an answer this client will not read
    /// rather than as a picture it will not draw — costing the reader the words as well.
    /// </remarks>
    [Fact]
    public async Task ReadMailBodyAsync_AnAnswerPastTheOrdinaryDocumentBound_IsStillRead()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            _ => StubTransport.JsonResponse(
                ABodyWhosePlainTextIs(new string('a', DeploymentExchange.MaxDocumentBytes))));

        // Act
        var body = await harness.Client.ReadMailBodyAsync(
            Guid.Parse("8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1"),
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DeploymentExchange.MaxDocumentBytes, body.PlainText.Text.Length);
    }

    /// <summary>Past the bound a body is held to, the answer is refused on its declared length before a byte is read.</summary>
    [Fact]
    public async Task ReadMailBodyAsync_AnAnswerLargerThanABodyMayBe_IsRefusedOnTheDeclaredLength()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ =>
        {
            var response = StubTransport.JsonResponse(ABodyWhosePlainTextIs("Words"));
            response.Content.Headers.ContentLength = DeploymentExchange.MaxMailBodyBytes + 1;

            return response;
        });

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.Client.ReadMailBodyAsync(
                Guid.Parse("8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1"),
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.Unusable, failure.Reason);
    }

    private static string ABodyWhosePlainTextIs(string text) =>
        $$"""
        {
          "storedEmailId": "8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1",
          "availability": "Readable",
          "plainText": { "text": "{{text}}", "originalCharacterCount": {{text.Length}}, "truncation": "None" },
          "document": null,
          "remoteImagesRequested": false
        }
        """;
}
