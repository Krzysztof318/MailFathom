// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Client.Backend;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Backend.Threads;

/// <summary>What the client makes of the conversation a deployment serves, and of the route it asks for one on.</summary>
public sealed class DeploymentMailThreadPageTests
{
    /// <summary>The conversation is a resource of its own, named in the path and narrowed by nothing.</summary>
    [Fact]
    public async Task ReadMailThreadAsync_AnyRequest_NamesTheConversationInThePathAndScopesItByNothing()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(MailThreads.Document(2)),
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await harness.Client.ReadMailThreadAsync(
            MailThreads.Identity,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var asked = Assert.Single(harness.Deployment.Requests).RequestUri;
        Assert.Equal($"/api/client/threads/{MailThreads.Identity:D}", asked.AbsolutePath);
        Assert.Empty(asked.Query);
    }

    /// <summary>How much is served and where the serving continues from are the query, and are written only when stated.</summary>
    [Fact]
    public async Task ReadMailThreadAsync_APageSizeAndACursor_WritesBothIntoTheQuery()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(MailThreads.Document(1)),
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await harness.Client.ReadMailThreadAsync(
            MailThreads.Identity,
            pageSize: 25,
            cursor: "after 3",
            TestContext.Current.CancellationToken);

        // Assert
        var asked = Assert.Single(harness.Deployment.Requests).RequestUri;
        Assert.Contains("pageSize=25", asked.Query, StringComparison.Ordinal);
        Assert.Contains("cursor=after%203", asked.Query, StringComparison.Ordinal);
    }

    /// <summary>Every field of the contract is read, because a header drawn from half of it would be wrong about the rest.</summary>
    [Fact]
    public async Task ReadMailThreadAsync_ADeploymentAnswering_ReadsEveryFieldOfTheContract()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(MailThreads.Document(2, "after-2")),
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var page = await harness.Client.ReadMailThreadAsync(
            MailThreads.Identity,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailThreads.Identity, page.ThreadId);
        Assert.Equal(2, page.MessageCount);
        Assert.Equal("after-2", page.NextCursor);
        Assert.Equal(50, page.PageSize);
        Assert.False(page.MoreMessagesNotAssembled);
        Assert.False(page.MoreParticipantsNotNamed);

        Assert.Equal([0, 1], page.Written.Select(static message => message.Position));
        Assert.Equal(MailMessages.Identity(1), page.Written[0].Email!.Id);
        Assert.Equal("What this one added", page.Written[0].Email!.Preview);

        var participant = Assert.Single(page.Authors);
        Assert.Equal("someone@example.test", participant.Address);
        Assert.Equal("Someone", participant.DisplayName);
        Assert.Equal(2, participant.MessageCount);
    }

    /// <summary>
    /// A document naming neither messages nor participants is a conversation holding none rather than a null every
    /// reader would have to answer for itself.
    /// </summary>
    [Fact]
    public async Task ReadMailThreadAsync_ADocumentNamingNeitherList_ReadsBothAsEmptyRatherThanAsNothing()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(
                $$"""{"threadId":"{{MailThreads.Identity}}","messageCount":0,"pageSize":50}"""),
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var page = await harness.Client.ReadMailThreadAsync(
            MailThreads.Identity,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(page.Written);
        Assert.Empty(page.Authors);
    }

    /// <summary>A cursor the deployment will not honour is this client's own value to stop sending rather than a defect.</summary>
    [Fact]
    public async Task ReadMailThreadAsync_ACursorTheDeploymentRefuses_ReportsTheCaseTheClientCanActOn()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse("{}", HttpStatusCode.BadRequest),
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.Client.ReadMailThreadAsync(
                MailThreads.Identity,
                cursor: "stale",
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.RequestRefused, failure.Reason);
    }

    /// <summary>A caller whose grant does not carry reading mail is refused rather than answered with nothing.</summary>
    [Fact]
    public async Task ReadMailThreadAsync_ARefusedCredential_ReportsTheOneCaseThePersonCanActOn()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse("{}", HttpStatusCode.Forbidden),
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => harness.Client.ReadMailThreadAsync(
                MailThreads.Identity,
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.CredentialRefused, failure.Reason);
    }
}
