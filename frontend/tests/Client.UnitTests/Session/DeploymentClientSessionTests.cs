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

        using var session = SessionOver(harness, address);
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

    /// <summary>A deployment that answered was reached, whatever it answered, so a refusal is never a lost connection.</summary>
    [Fact]
    public async Task Reach_ADeploymentAnswering_IsReached()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(ACallerWhoMayRead));
        using var session = SessionOver(harness);

        // Act
        await session.Standing;

        // Assert
        var reach = await session.Connection;
        Assert.NotNull(reach);
        Assert.True(reach.IsReached);
        Assert.False(reach.IsLost);
        Assert.False(reach.IsRetrying);
    }

    /// <summary>
    /// A credential the deployment refused is a deployment that was reached. Reporting a lost connection there would
    /// send somebody after their network about a permission their operator has to widen.
    /// </summary>
    [Fact]
    public async Task Reach_ADeploymentRefusingTheCredential_IsStillReached()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            _ => StubTransport.JsonResponse("{}", HttpStatusCode.Unauthorized));
        using var session = SessionOver(harness);

        // Act
        await Assert.ThrowsAsync<DeploymentFailure>(async () => await session.Standing);

        // Assert
        var reach = await session.Connection;
        Assert.NotNull(reach);
        Assert.True(reach.IsReached);
        Assert.False(reach.IsLost);
    }

    /// <summary>
    /// A connection that dropped is recovered from without the person restarting anything, which is the whole reason
    /// the retry is inside the fetch rather than behind a button.
    /// </summary>
    [Fact]
    public async Task Standing_ADeploymentAnsweringOnASecondAttempt_RecoversWithoutBeingAsked()
    {
        // Arrange
        var answers = 0;
        using var harness = new DeploymentHarness(_ =>
            ++answers is 1
                ? throw new HttpRequestException("nothing is answering")
                : StubTransport.JsonResponse(ACallerWhoMayRead));
        using var session = SessionOver(harness, attempts: 3);

        // Act
        var standing = await session.Standing;

        // Assert
        Assert.NotNull(standing);
        Assert.Equal("0.8.0", standing.DeploymentVersion);
        Assert.Equal(2, harness.Deployment.Requests.Count);

        var reach = await session.Connection;
        Assert.NotNull(reach);
        Assert.True(reach.IsReached);
    }

    /// <summary>
    /// A deployment that took too long is recovered from on the same terms as one nothing answered from. Both are the
    /// connection rather than the answer, so the curve treats them alike, and asserting only the unreachable one would
    /// leave the other free to stop being retried without a test saying so.
    /// </summary>
    [Fact]
    public async Task Standing_ADeploymentThatTookTooLongAndThenAnswered_RecoversOnTheSameTerms()
    {
        // Arrange
        var answers = 0;
        using var harness = new DeploymentHarness(_ =>
            ++answers is 1
                ? throw new TaskCanceledException("this client's own timeout elapsed")
                : StubTransport.JsonResponse(ACallerWhoMayRead));
        using var session = SessionOver(harness, attempts: 3);

        // Act
        var standing = await session.Standing;

        // Assert
        Assert.NotNull(standing);
        Assert.Equal(2, harness.Deployment.Requests.Count);

        var reach = await session.Connection;
        Assert.NotNull(reach);
        Assert.True(reach.IsReached);
    }

    /// <summary>A deployment that never answers in time is given up on where one that never answers at all is.</summary>
    [Fact]
    public async Task Standing_ADeploymentThatNeverAnswersInTime_StopsAfterTheAttemptsAndSaysSo()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            _ => throw new TaskCanceledException("this client's own timeout elapsed"));
        using var session = SessionOver(harness, attempts: 3);

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(async () => await session.Standing);

        // Assert
        Assert.Equal(DeploymentFailureReason.TimedOut, failure.Reason);
        Assert.Equal(3, harness.Deployment.Requests.Count);

        var reach = await session.Connection;
        Assert.NotNull(reach);
        Assert.True(reach.IsLost);
        Assert.Equal(3, reach.Attempt);
    }

    /// <summary>The attempts are bounded, so a deployment that is genuinely gone is said to be gone rather than waited on forever.</summary>
    [Fact]
    public async Task Standing_ADeploymentNothingAnswersFrom_StopsAfterTheAttemptsAndSaysSo()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            _ => throw new HttpRequestException("nothing is answering"));
        using var session = SessionOver(harness, attempts: 3);

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(async () => await session.Standing);

        // Assert
        Assert.Equal(DeploymentFailureReason.Unreachable, failure.Reason);
        Assert.Equal(3, harness.Deployment.Requests.Count);

        var reach = await session.Connection;
        Assert.NotNull(reach);
        Assert.True(reach.IsLost);
        Assert.Equal(3, reach.Attempt);
        Assert.Equal(3, reach.Attempts);
    }

    /// <summary>
    /// An answer this version does not understand is a defect rather than a moment to wait out, so it is not retried:
    /// asking again would spend a person's time proving what the first attempt already said.
    /// </summary>
    [Fact]
    public async Task Standing_ADeploymentAnsweringSomethingElse_IsNotAskedAgain()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse("not a document"));
        using var session = SessionOver(harness, attempts: 3);

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(async () => await session.Standing);

        // Assert
        Assert.Equal(DeploymentFailureReason.Unusable, failure.Reason);
        Assert.Single(harness.Deployment.Requests);
    }

    /// <summary>
    /// A failure this client has no reading of says nothing about the network, so it ends the standing rather than
    /// leaving it mid-attempt: a standing still reading "reaching" would close the frame's notice about the failed
    /// session along with the connection's, and the screen would have failed in silence.
    /// </summary>
    [Fact]
    public async Task Connection_AFailureThatIsNotAFailedExchange_EndsTheStandingRatherThanLeavingItMidAttempt()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => throw new InvalidOperationException("nothing composed this"));
        using var session = SessionOver(harness, attempts: 3);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.Standing);

        // Assert
        var connection = await session.Connection;
        Assert.NotNull(connection);
        Assert.True(connection.IsReached);
        Assert.False(connection.IsRetrying);
        Assert.False(connection.IsLost);
        Assert.Equal(1, connection.Attempt);
    }

    /// <summary>A session that could be built without one of its collaborators would be one nothing ever refreshed.</summary>
    [Fact]
    public void Constructor_AMissingService_IsRefused()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(ACallerWhoMayRead));
        var address = new DeploymentAddress(harness.Tokens);
        var retry = DeploymentConnectionRetry.Standard;
        var clock = new StubClock(DateTimeOffset.UnixEpoch);

        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => new DeploymentClientSession(null!, harness.Tokens, address, retry, clock));
        Assert.Throws<ArgumentNullException>(
            () => new DeploymentClientSession(harness.Client, null!, address, retry, clock));
        Assert.Throws<ArgumentNullException>(
            () => new DeploymentClientSession(harness.Client, harness.Tokens, null!, retry, clock));
        Assert.Throws<ArgumentNullException>(
            () => new DeploymentClientSession(harness.Client, harness.Tokens, address, null!, clock));
        Assert.Throws<ArgumentNullException>(
            () => new DeploymentClientSession(harness.Client, harness.Tokens, address, retry, null!));
    }

    /// <summary>A session over a scripted deployment, retrying on a curve a test spends no time on.</summary>
    /// <remarks>
    /// The waits are zero and the clock is a stub, so nothing here is timed against a real one: what is asserted is how
    /// many attempts are made and what each one publishes, and the curve the waits are drawn from is
    /// <c>DeploymentConnectionRetryTests</c>'s own subject.
    /// </remarks>
    private static DeploymentClientSession SessionOver(
        DeploymentHarness harness,
        DeploymentAddress? address = null,
        int attempts = 1) =>
        new(
            harness.Client,
            harness.Tokens,
            address ?? new DeploymentAddress(harness.Tokens),
            new DeploymentConnectionRetry(attempts, TimeSpan.Zero, TimeSpan.Zero),
            new StubClock(DateTimeOffset.UnixEpoch));
}
