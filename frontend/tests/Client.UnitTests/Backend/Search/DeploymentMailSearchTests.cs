// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Search;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Backend.Search;

/// <summary>What the client makes of one page of ranked mail a deployment serves.</summary>
public sealed class DeploymentMailSearchTests
{
    private const string OnePage =
        """
        {
          "results": [
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
              "preview": "The numbers for the quarter are",
              "snippets": [ "The **quarter** closed well" ],
              "matchedBy": "BothRankings"
            }
          ],
          "nextCursor": "after-the-page",
          "pageSize": 20,
          "retrievalMode": "Hybrid",
          "semanticSearch": "Available",
          "includedJunkMail": false
        }
        """;

    /// <summary>Every field of the contract is read, because the list and its trust statement draw all of them.</summary>
    [Fact]
    public async Task SearchMailAsync_ADeploymentAnswering_ReadsEveryFieldOfTheContract()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(OnePage),
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var page = await harness.Client.SearchMailAsync(
            new MailSearchQuery { Query = "quarter" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("after-the-page", page.NextCursor);
        Assert.Equal(20, page.PageSize);
        Assert.Equal(MailSearchRetrievalMode.Hybrid, page.Ranking);
        Assert.Equal(SemanticSearchStanding.Available, page.SemanticStanding);
        Assert.False(page.IncludedJunkMail);

        var result = Assert.Single(page.Rows);
        Assert.Equal(Guid.Parse("8a1e0f24-2b3c-4d5e-9f60-112233445566"), result.Id);
        Assert.Equal("work", result.Account);
        Assert.Equal("INBOX", result.Folder);
        Assert.Equal(Guid.Parse("0d2f5ab1-7c88-4e11-92aa-556677889900"), result.ThreadId);
        Assert.Equal("Quarterly review", result.Subject);
        Assert.Equal(new DateTimeOffset(2026, 8, 25, 11, 50, 0, TimeSpan.Zero), result.ReceivedAt);
        Assert.Equal("Someone", result.SenderDisplayName);
        Assert.Equal(["owner@example.test"], result.Recipients);
        Assert.True(result.Unread);
        Assert.True(result.Flagged);
        Assert.False(result.Answered);
        Assert.True(result.HasAttachments);
        Assert.Equal(2, result.AttachmentCount);
        Assert.Equal(40960, result.SizeOctets);
        Assert.Equal("The numbers for the quarter are", result.Preview);
        Assert.Equal(["The **quarter** closed well"], result.Extracts);
        Assert.Equal(MailSearchMatchOrigin.BothRankings, result.Origin);
    }

    /// <summary>The request states every constraint, escapes personal text, and carries the cursor under the same search.</summary>
    [Fact]
    public async Task SearchMailAsync_ARequestWithEveryFilter_GoesToTheClientSurfaceCarryingIt()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(OnePage),
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await harness.Client.SearchMailAsync(
            new MailSearchQuery
            {
                Query = "quarter & review",
                Account = "work account",
                Folder = "role:Inbox",
                Sender = "sender@example.test",
                Recipient = "owner@example.test",
                ReceivedOnOrAfter = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                ReceivedBefore = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Unread = false,
                Flagged = true,
                HasAttachments = false,
                PageSize = 20,
                Cursor = "next page",
            },
            TestContext.Current.CancellationToken);

        // Assert
        var asked = Assert.Single(harness.Deployment.Requests).RequestUri;
        Assert.NotNull(asked);
        Assert.Equal("/api/client/emails/search", asked.AbsolutePath);
        Assert.Equal(
            "?query=quarter%20%26%20review&account=work%20account&folder=role%3AInbox&sender=sender%40example.test"
            + "&recipient=owner%40example.test&unread=false&flagged=true&hasAttachments=false"
            + "&receivedOnOrAfter=2026-01-01T00%3A00%3A00.0000000%2B00%3A00"
            + "&receivedBefore=2027-01-01T00%3A00%3A00.0000000%2B00%3A00&pageSize=20&cursor=next%20page",
            asked.Query);
    }

    /// <summary>A query the deployment refuses is the client's own request to repair rather than an unavailable deployment.</summary>
    [Fact]
    public async Task SearchMailAsync_AQueryTheDeploymentRefuses_ReachesTheCallerAsARefusedRequest()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse("{}", HttpStatusCode.BadRequest),
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.Client.SearchMailAsync(
                new MailSearchQuery { Query = "quarter" },
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.RequestRefused, failure.Reason);
    }

    /// <summary>A search with nothing to describe would be a request composed from nowhere.</summary>
    [Fact]
    public async Task SearchMailAsync_NoQuery_IsRefused()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(OnePage),
            cancellationToken: TestContext.Current.CancellationToken);

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => harness.Client.SearchMailAsync(null!, TestContext.Current.CancellationToken));
    }
}
