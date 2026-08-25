// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Client.Backend;
using MailFathom.Client.Session;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Session;

/// <summary>The session as state: fetched once, read by everything, and asked again when it stops describing this run.</summary>
public sealed class DeploymentClientSessionTests
{
    private const string ACallerWhoMayRead =
        """
        {"service":"MailFathom","version":"0.8.0","permissions":["mailfathom.mail.read"]}
        """;

    /// <summary>What the deployment reported is what the client offers, without any screen having asked for it.</summary>
    [Fact]
    public async Task Standing_ADeploymentAnswering_IsWhatTheClientOffers()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(ACallerWhoMayRead));
        using var session = SessionOver(harness);

        // Act
        var standing = await session.Standing;

        // Assert
        Assert.NotNull(standing);
        Assert.Equal("0.8.0", standing.DeploymentVersion);
        Assert.True(standing.Offers(ClientCapability.Mail));
        Assert.False(standing.Offers(ClientCapability.Discover));
    }

    /// <summary>
    /// One fetch for the whole application, which is the reason this is a state rather than a feed: a feed is read
    /// from the start by whoever subscribes, so a session read by five screens would be five requests for one answer.
    /// </summary>
    [Fact]
    public async Task Standing_ReadMoreThanOnce_AsksTheDeploymentOnce()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(ACallerWhoMayRead));
        using var session = SessionOver(harness);

        // Act
        await session.Standing;
        await session.Standing;

        // Assert
        Assert.Single(harness.Deployment.Requests);
    }

    /// <summary>
    /// The deployment answers about the credential presented to it, so an answer held from before somebody signed in
    /// describes a caller who is no longer the one asking.
    /// </summary>
    [Fact]
    public async Task Standing_TheSignedInIdentityChanging_IsAskedAgain()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(ACallerWhoMayRead));
        using var session = SessionOver(harness);
        await session.Standing;

        // Act
        harness.Tokens.Accept("the-token");
        await session.Standing;

        // Assert
        Assert.Equal(2, harness.Deployment.Requests.Count);
    }

    /// <summary>An answer describes the deployment it came from, so pointing the client at another one ends it.</summary>
    [Fact]
    public async Task Standing_TheClientBeingPointedElsewhere_IsAskedAgain()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(ACallerWhoMayRead));
        var address = new DeploymentAddress(harness.Tokens);
        address.PointAt(new Uri("https://mail.example/"));

        using var session = new DeploymentClientSession(harness.Client, harness.Tokens, address);
        await session.Standing;

        // Act
        address.PointAt(new Uri("https://other.example/"));
        await session.Standing;

        // Assert
        Assert.Equal(2, harness.Deployment.Requests.Count);
    }

    /// <summary>Asking again is what a person presses after a fetch that failed, and it is the same fetch rather than a second path.</summary>
    [Fact]
    public async Task Refresh_AfterAnAnswer_AsksTheDeploymentAgain()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(ACallerWhoMayRead));
        using var session = SessionOver(harness);
        await session.Standing;

        // Act
        session.Refresh();
        await session.Standing;

        // Assert
        Assert.Equal(2, harness.Deployment.Requests.Count);
    }

    /// <summary>
    /// A session that cannot be fetched reaches a screen as the feed's error axis rather than as an empty answer, so
    /// the shell can say what it means instead of showing an empty frame.
    /// </summary>
    [Fact]
    public async Task Standing_ADeploymentRefusingTheCredential_ReachesAScreenAsAFailure()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            _ => StubTransport.JsonResponse("{}", HttpStatusCode.Unauthorized));
        using var session = SessionOver(harness);

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(async () => await session.Standing);

        // Assert
        Assert.Equal(DeploymentFailureReason.CredentialRefused, failure.Reason);
    }

    /// <summary>A session that stopped listening would go on offering what a credential no longer carries.</summary>
    [Fact]
    public async Task Dispose_AfterTheSessionIsGone_LeavesNothingListeningToTheCredential()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(ACallerWhoMayRead));
        var session = SessionOver(harness);
        await session.Standing;

        // Act
        session.Dispose();
        harness.Tokens.Accept("the-token");

        // Assert
        Assert.Single(harness.Deployment.Requests);
    }

    /// <summary>A session that could be built without one of its collaborators would be one nothing ever refreshed.</summary>
    [Fact]
    public void Constructor_AMissingService_IsRefused()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(ACallerWhoMayRead));
        var address = new DeploymentAddress(harness.Tokens);

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new DeploymentClientSession(null!, harness.Tokens, address));
        Assert.Throws<ArgumentNullException>(() => new DeploymentClientSession(harness.Client, null!, address));
        Assert.Throws<ArgumentNullException>(() => new DeploymentClientSession(harness.Client, harness.Tokens, null!));
    }

    private static DeploymentClientSession SessionOver(DeploymentHarness harness) =>
        new(harness.Client, harness.Tokens, new DeploymentAddress(harness.Tokens));
}
