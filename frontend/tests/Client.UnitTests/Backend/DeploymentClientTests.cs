// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Text;
using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Authorization;
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
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(AnAnsweringDeployment),
            cancellationToken: TestContext.Current.CancellationToken);

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
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(ACallerGrantedNothing),
            cancellationToken: TestContext.Current.CancellationToken);

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
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(ACallerGrantedNothing),
            cancellationToken: TestContext.Current.CancellationToken);

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
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse("{}", refusal),
            cancellationToken: TestContext.Current.CancellationToken);

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
        using var harness = await DeploymentHarness.CreateAsync(
            _ => throw new TaskCanceledException("The request timed out."),
            cancellationToken: TestContext.Current.CancellationToken);

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
        using var harness = await DeploymentHarness.CreateAsync(
            _ => throw new TaskCanceledException("Cancelled."),
            cancellationToken: TestContext.Current.CancellationToken);

        await cancellation.CancelAsync();

        // Act, Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Client.ReadSessionAsync(cancellation.Token));
    }

    [Fact]
    public async Task ReadSessionAsync_AnUnreachableDeployment_IsDistinguishedFromOneThatRefused()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => throw new HttpRequestException("Name or service not known."),
            cancellationToken: TestContext.Current.CancellationToken);

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
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse("<!DOCTYPE html><html><body>Sign in</body></html>"),
            cancellationToken: TestContext.Current.CancellationToken);

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
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(
                $$"""{"service":"MailFathom","version":"{{new string('0', DeploymentExchange.MaxDocumentBytes)}}"}"""),
            cancellationToken: TestContext.Current.CancellationToken);

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
        using var harness = await DeploymentHarness.CreateAsync(
            _ => throw new HttpRequestException(
                HttpRequestError.ConfigurationLimitExceeded,
                "Cannot write more bytes to the buffer than the configured maximum buffer size."),
            cancellationToken: TestContext.Current.CancellationToken);

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
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(ACallerGrantedNothing),
            throughCredentialHandler: true,
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await harness.Client.ReadSessionAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(harness.Owner.IsSignedIn);
        Assert.Null(Assert.Single(harness.Deployment.Requests).Authorization);
    }

    /// <summary>RFC 7617, and in the header alone: an address is written into a log and a credential in one would be too.</summary>
    [Fact]
    public async Task ReadSessionAsync_AfterSigningIn_PresentsTheCredentialInTheHeaderAndNowhereElse()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(AnAnsweringDeployment),
            throughCredentialHandler: true,
            cancellationToken: TestContext.Current.CancellationToken);

        await harness.Owner.AcceptAsync(
            DeploymentHarness.DeploymentAddress,
            new OwnerCredential("ada", "a-long-password"),
            TestContext.Current.CancellationToken);

        // Act
        await harness.Client.ReadSessionAsync(TestContext.Current.CancellationToken);

        // Assert
        var request = Assert.Single(harness.Deployment.Requests);

        Assert.Equal(
            $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes("ada:a-long-password"))}",
            request.Authorization);

        Assert.DoesNotContain("a-long-password", request.RequestUri.ToString(), StringComparison.Ordinal);
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
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(
                ABodyWhosePlainTextIs(new string('a', DeploymentExchange.MaxDocumentBytes))),
            cancellationToken: TestContext.Current.CancellationToken);

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
        using var harness = await DeploymentHarness.CreateAsync(_ =>
        {
            var response = StubTransport.JsonResponse(ABodyWhosePlainTextIs("Words"));
            response.Content.Headers.ContentLength = DeploymentExchange.MaxMailBodyBytes + 1;

            return response;
        },
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.Client.ReadMailBodyAsync(
                Guid.Parse("8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1"),
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.Unusable, failure.Reason);
    }

    [Fact]
    public async Task ReadMailMessageAsync_AStoredMessage_ReadsEverythingThePaneDrawsAroundItsBody()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(
                """
                {
                  "storedEmailId": "8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1",
                  "account": "personal",
                  "folder": "INBOX",
                  "threadId": "5de1bfc2-e8ca-4388-8e7f-1f304b07d671",
                  "sizeOctets": 2416381,
                  "headers": {
                    "subject": "Release 0.8.0",
                    "sentAt": "2026-08-27T09:14:00+00:00",
                    "receivedAt": "2026-08-27T09:14:06+00:00",
                    "participants": [
                      { "role": "From", "address": "release@example.test", "displayName": "Release notices" },
                      { "role": "To", "address": "reader@example.test", "displayName": null }
                    ],
                    "messageId": "release-0-8-0@example.test",
                    "inReplyTo": null,
                    "references": ["earlier@example.test"]
                  },
                  "body": { "availability": "Readable", "plainText": true, "html": true },
                  "sender": { "authorAuthentication": "Authenticated", "deploymentTrust": "Trusted" },
                  "attachments": [
                    {
                      "position": 0,
                      "fileName": "release-notes.pdf",
                      "wasFileNameNormalized": false,
                      "mediaType": "application/pdf",
                      "sizeOctets": 2401337
                    }
                  ],
                  "carried": {
                    "attachmentCount": 1,
                    "totalSizeOctets": 2401337,
                    "inlineResourceCount": 2,
                    "encrypted": false,
                    "unverifiedSignature": true,
                    "unexpandedTnefPart": false
                  },
                  "unread": true,
                  "flagged": false,
                  "answered": true
                }
                """),
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var message = await harness.Client.ReadMailMessageAsync(
            Guid.Parse("8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Release 0.8.0", message.Headers.Subject);
        Assert.Equal("release@example.test", message.Headers.Participants[0].Address);
        Assert.Equal("Authenticated", message.Sender.AuthorAuthentication);
        Assert.Equal("Trusted", message.Sender.DeploymentTrust);
        Assert.Equal("release-notes.pdf", Assert.Single(message.Attachments).FileName);
        Assert.Equal(2, message.Carried!.InlineResourceCount);
        Assert.True(message.Carried.UnverifiedSignature);
        Assert.True(message.Unread);
        Assert.True(message.Answered);
    }

    [Fact]
    public async Task DownloadMailAttachmentAsync_AFileSomebodyRequested_StreamsItToTheChosenDestination()
    {
        // Arrange
        byte[] octets = [4, 8, 15, 16, 23, 42];
        using var harness = await DeploymentHarness.CreateAsync(
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(octets) },
            cancellationToken: TestContext.Current.CancellationToken);
        await using var destination = new MemoryStream();

        // Act
        await harness.Client.DownloadMailAttachmentAsync(
            Guid.Parse("8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1"),
            position: 3,
            expectedSizeOctets: octets.Length,
            destination,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(octets, destination.ToArray());
        Assert.Equal(
            new Uri("https://mail.example/api/client/messages/8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1/attachments/3"),
            Assert.Single(harness.Deployment.Requests).RequestUri);
    }

    [Fact]
    public async Task DownloadMailAttachmentAsync_AResponseWhoseLengthChanged_RefusesItBeforeWriting()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) },
            cancellationToken: TestContext.Current.CancellationToken);
        await using var destination = new MemoryStream();

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.Client.DownloadMailAttachmentAsync(
                Guid.Parse("8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1"),
                position: 0,
                expectedSizeOctets: 4,
                destination,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.Unusable, failure.Reason);
        Assert.Empty(destination.ToArray());
    }

    [Fact]
    public async Task DownloadMailAttachmentAsync_AFileAboveTheClientCeiling_RefusesItWithoutARequest()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => throw new InvalidOperationException("No request should be sent."),
            cancellationToken: TestContext.Current.CancellationToken);
        await using var destination = new MemoryStream();

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.Client.DownloadMailAttachmentAsync(
                Guid.Parse("8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1"),
                position: 0,
                expectedSizeOctets: DeploymentExchange.MaxMailAttachmentBytes + 1,
                destination,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.Unusable, failure.Reason);
        Assert.Empty(harness.Deployment.Requests);
        Assert.Empty(destination.ToArray());
    }

    [Fact]
    public async Task DownloadMailAttachmentAsync_AStreamLongerThanItsMatchingHeader_RefusesItBeforeWritingTheOverrun()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => AttachmentResponse([1, 2, 3, 4], declaredLength: 3),
            cancellationToken: TestContext.Current.CancellationToken);
        await using var destination = new MemoryStream();

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.Client.DownloadMailAttachmentAsync(
                Guid.Parse("8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1"),
                position: 0,
                expectedSizeOctets: 3,
                destination,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.Unusable, failure.Reason);
        Assert.Empty(destination.ToArray());
    }

    [Fact]
    public async Task DownloadMailAttachmentAsync_AStreamShorterThanItsMatchingHeader_RefusesItAfterTheStagingWrite()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => AttachmentResponse([1, 2, 3], declaredLength: 4),
            cancellationToken: TestContext.Current.CancellationToken);
        await using var destination = new MemoryStream();

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.Client.DownloadMailAttachmentAsync(
                Guid.Parse("8f14e45f-ceea-467a-9f3e-1c3ecdf1e9a1"),
                position: 0,
                expectedSizeOctets: 4,
                destination,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.Unusable, failure.Reason);
        Assert.Equal([1, 2, 3], destination.ToArray());
    }

    private static HttpResponseMessage AttachmentResponse(byte[] octets, long declaredLength)
    {
        var content = new StreamContent(new MemoryStream(octets));
        content.Headers.ContentLength = declaredLength;

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
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
