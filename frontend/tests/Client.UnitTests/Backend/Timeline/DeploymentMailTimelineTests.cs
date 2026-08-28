// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Timeline;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Backend.Timeline;

/// <summary>What the client makes of one page of the message list a deployment serves.</summary>
public sealed class DeploymentMailTimelineTests
{
    private const string OnePage =
        """
        {
          "emails": [
            {
              "id": "8a1e0f24-2b3c-4d5e-9f60-112233445566",
              "account": "work",
              "folder": "INBOX",
              "threadId": "0d2f5ab1-7c88-4e11-92aa-556677889900",
              "subject": "Quarterly review",
              "receivedAt": "2026-08-25T11:50:00+00:00",
              "sentAt": "2026-08-25T11:49:00+00:00",
              "senderAddress": "someone@example.test",
              "senderDisplayName": "Someone",
              "toAddresses": [ "owner@example.test" ],
              "unread": true,
              "flagged": true,
              "answered": false,
              "hasAttachments": true,
              "attachmentCount": 2,
              "sizeOctets": 40960,
              "preview": "The numbers for the quarter are"
            }
          ],
          "nextCursor": "after-the-page",
          "previousCursor": null,
          "pageSize": 50
        }
        """;

    /// <summary>Every field of the contract is read, because a row is drawn out of each of them and out of nothing else.</summary>
    [Fact]
    public async Task ReadMailTimelineAsync_ADeploymentAnswering_ReadsEveryFieldOfTheContract()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(OnePage));

        // Act
        var page = await harness.Client.ReadMailTimelineAsync(
            new MailTimelineQuery(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("after-the-page", page.NextCursor);
        Assert.Null(page.PreviousCursor);
        Assert.Equal(50, page.PageSize);

        var message = Assert.Single(page.Rows);
        Assert.Equal(Guid.Parse("8a1e0f24-2b3c-4d5e-9f60-112233445566"), message.Id);
        Assert.Equal("work", message.Account);
        Assert.Equal("INBOX", message.Folder);
        Assert.Equal(Guid.Parse("0d2f5ab1-7c88-4e11-92aa-556677889900"), message.ThreadId);
        Assert.Equal("Quarterly review", message.Subject);
        Assert.Equal(new DateTimeOffset(2026, 8, 25, 11, 50, 0, TimeSpan.Zero), message.ReceivedAt);
        Assert.Equal(new DateTimeOffset(2026, 8, 25, 11, 49, 0, TimeSpan.Zero), message.SentAt);
        Assert.Equal("someone@example.test", message.SenderAddress);
        Assert.Equal("Someone", message.SenderDisplayName);
        Assert.Equal(["owner@example.test"], message.Recipients);
        Assert.True(message.Unread);
        Assert.True(message.Flagged);
        Assert.False(message.Answered);
        Assert.True(message.HasAttachments);
        Assert.Equal(2, message.AttachmentCount);
        Assert.Equal(40960, message.SizeOctets);
        Assert.Equal("The numbers for the quarter are", message.Preview);
    }

    /// <summary>The route is the client surface's own, and it carries what the query narrowed to.</summary>
    [Fact]
    public async Task ReadMailTimelineAsync_ARequestNarrowingSomewhere_GoesToTheClientSurfaceCarryingIt()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(OnePage));

        // Act
        await harness.Client.ReadMailTimelineAsync(
            new MailTimelineQuery { Account = "work", Folder = "INBOX", PageSize = 50 },
            TestContext.Current.CancellationToken);

        // Assert
        var asked = Assert.Single(harness.Deployment.Requests).RequestUri;
        Assert.NotNull(asked);
        Assert.Equal("/api/client/emails", asked.AbsolutePath);
        Assert.Equal("?account=work&folder=INBOX&order=newestFirst&direction=forward&pageSize=50", asked.Query);
    }

    /// <summary>A document naming no row reads as a page holding nothing rather than as a shape a caller has to remember.</summary>
    [Fact]
    public async Task ReadMailTimelineAsync_ADocumentNamingNoRow_ReadsAsEmptyRatherThanFailing()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse("""{"pageSize":25}"""));

        // Act
        var page = await harness.Client.ReadMailTimelineAsync(
            new MailTimelineQuery(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(page.Rows);
        Assert.Null(page.NextCursor);
        Assert.Null(page.PreviousCursor);
    }

    /// <summary>
    /// A cursor the deployment will not honour is the client's own request being refused rather than a defect, which
    /// is what lets a list give up a remembered position instead of showing somebody an error about a value they
    /// never typed.
    /// </summary>
    [Fact]
    public async Task ReadMailTimelineAsync_ACursorTheDeploymentRefuses_ReachesTheCallerAsARefusedRequest()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            _ => StubTransport.JsonResponse("{}", HttpStatusCode.BadRequest));

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.Client.ReadMailTimelineAsync(
                new MailTimelineQuery { Cursor = "stale" },
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.RequestRefused, failure.Reason);
    }

    /// <summary>A credential the deployment will not serve is kept apart from a place that holds no mail.</summary>
    [Fact]
    public async Task ReadMailTimelineAsync_ACredentialWithoutTheGrant_IsRefusedRatherThanAnsweredWithNothing()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            _ => StubTransport.JsonResponse("{}", HttpStatusCode.Forbidden));

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.Client.ReadMailTimelineAsync(
                new MailTimelineQuery(),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.CredentialRefused, failure.Reason);
    }

    /// <summary>A read with nothing to describe would be a request composed from nowhere.</summary>
    [Fact]
    public async Task ReadMailTimelineAsync_NoQuery_IsRefused()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(OnePage));

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => harness.Client.ReadMailTimelineAsync(null!, TestContext.Current.CancellationToken));
    }

    /// <summary>A message reported with no recipients reads as a message addressed to nobody this row draws.</summary>
    [Fact]
    public void Recipients_AMessageReportedWithNone_FallsBackToNothingRatherThanFailing()
    {
        // Arrange
        var message = new DeploymentMailMessage(
            Guid.Empty,
            "work",
            "INBOX",
            ThreadId: null,
            Subject: null,
            ReceivedAt: null,
            SentAt: null,
            SenderAddress: null,
            SenderDisplayName: null,
            ToAddresses: null!,
            Unread: false,
            Flagged: false,
            Answered: false,
            HasAttachments: false,
            AttachmentCount: 0,
            SizeOctets: 0,
            Preview: null);

        // Act, Assert
        Assert.Empty(message.Recipients);
    }
}
