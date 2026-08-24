// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Client.Backend;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Backend;

/// <summary>What an address turns out to be when it is asked, before anything is kept or signed in to.</summary>
/// <remarks>
/// Three claims carry the feature. Something has to answer, and answer the way MailFathom does, or a mistyped address
/// would be kept and arrive later as an authentication failure. A deployment that refuses an unauthenticated caller is
/// still a deployment, because that is what a correctly configured one does and refusing every one of them would make
/// this screen unusable against a real installation. And no credential is presented, because the whole point of asking
/// is that nobody knows yet what is at the other end.
/// </remarks>
public sealed class DeploymentProbeTests : IDisposable
{
    private static readonly Uri Candidate = new("https://mail.example/");

    private readonly StubTransport answers;
    private readonly HttpClient transport;
    private readonly DeploymentProbe probe;

    /// <summary>Builds a probe over a transport a test scripts through <see cref="Answer" />.</summary>
    public DeploymentProbeTests()
    {
        this.answers = new StubTransport(request => this.Answer(request));
        this.transport = new HttpClient(this.answers);
        this.probe = new DeploymentProbe(
            new StubHttpClientFactory(
                new Dictionary<string, HttpClient>(StringComparer.Ordinal)
                {
                    [DeploymentHttpClients.DeploymentProbe] = this.transport,
                }));
    }

    /// <summary>How the address under test answers, which each test replaces.</summary>
    private Func<HttpRequestMessage, HttpResponseMessage> Answer { get; set; } =
        _ => StubTransport.JsonResponse("{}");

    /// <inheritdoc />
    public void Dispose()
    {
        this.transport.Dispose();
        this.answers.Dispose();
    }

    [Fact]
    public async Task ReachAsync_AMailFathomDeployment_IsReachedAndDescribesItself()
    {
        // Arrange
        this.Answer = _ => StubTransport.JsonResponse(
            """{"service":"MailFathom","version":"0.8.0","credential":"anonymous","permissions":[]}""");

        // Act
        var reach = await this.probe.ReachAsync(Candidate, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(reach.IsGuarded);
        Assert.Equal("0.8.0", reach.Session?.Version);
    }

    /// <summary>The route the client surface publishes, resolved against the candidate rather than against a base address nothing has set yet.</summary>
    [Fact]
    public async Task ReachAsync_ACandidate_IsAskedTheSessionRouteBeneathIt()
    {
        // Arrange
        this.Answer = _ => StubTransport.JsonResponse("""{"service":"MailFathom","version":"0.8.0","credential":"anonymous","permissions":[]}""");

        // Act
        await this.probe.ReachAsync(new Uri("https://mail.example:8443/"), TestContext.Current.CancellationToken);

        // Assert
        var asked = Assert.Single(this.answers.Requests);
        Assert.Equal(new Uri("https://mail.example:8443/api/client/session"), asked.RequestUri);
    }

    /// <summary>Nobody has vouched for this address yet, so the session's credential must not be offered to it.</summary>
    [Fact]
    public async Task ReachAsync_ACandidate_IsAskedWithoutACredential()
    {
        // Arrange
        this.Answer = _ => StubTransport.JsonResponse("""{"service":"MailFathom","version":"0.8.0","credential":"anonymous","permissions":[]}""");

        // Act
        await this.probe.ReachAsync(Candidate, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(Assert.Single(this.answers.Requests).Authorization);
    }

    /// <summary>What a deployment configured the way MailFathom's own documentation asks answers, which has to read as a deployment.</summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ReachAsync_ADeploymentGuardingItsClientSurface_IsReachedWithoutDescribingItself(
        HttpStatusCode status)
    {
        // Arrange
        this.Answer = _ => new HttpResponseMessage(status);

        // Act
        var reach = await this.probe.ReachAsync(Candidate, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(reach.IsGuarded);
        Assert.Null(reach.Session);
    }

    /// <summary>Anything can answer on a port, so the answer naming another product is not a deployment however well formed it is.</summary>
    [Fact]
    public async Task ReachAsync_SomethingElseAnsweringTheRoute_IsNotADeployment()
    {
        // Arrange
        this.Answer = _ => StubTransport.JsonResponse(
            """{"service":"SomebodyElse","version":"3.1","credential":"anonymous","permissions":[]}""");

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => this.probe.ReachAsync(Candidate, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.Unusable, failure.Reason);
    }

    /// <summary>A captive portal or a proxy answers with a page rather than with this document, and that is the same verdict.</summary>
    [Fact]
    public async Task ReachAsync_AnAnswerThatIsNotTheDocument_IsNotADeployment()
    {
        // Arrange
        this.Answer = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>Sign in to the hotel network</html>"),
        };

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => this.probe.ReachAsync(Candidate, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.Unusable, failure.Reason);
    }

    [Fact]
    public async Task ReachAsync_NothingAtTheAddress_IsUnreachable()
    {
        // Arrange
        this.Answer = _ => throw new HttpRequestException("no route to host");

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(
            () => this.probe.ReachAsync(Candidate, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DeploymentFailureReason.Unreachable, failure.Reason);
    }

    /// <summary>The rule runs before anything is sent, so an address this client may not carry a credential to is never contacted at all.</summary>
    [Fact]
    public async Task ReachAsync_AnAddressTheRuleRefuses_IsNotContacted()
    {
        // Act
        await Assert.ThrowsAsync<ArgumentException>(
            "candidate",
            () => this.probe.ReachAsync(new Uri("http://mail.example/"), TestContext.Current.CancellationToken));

        // Assert
        Assert.Empty(this.answers.Requests);
    }

    /// <summary>Nothing is sent and nothing is said: the candidate is refused before contact, and the message names no credential written into it.</summary>
    [Fact]
    public async Task ReachAsync_ACandidateCarryingEmbeddedCredentials_RefusesItWithoutNamingTheSecret()
    {
        // Act
        var failure = await Assert.ThrowsAsync<ArgumentException>(
            "candidate",
            () => this.probe.ReachAsync(
                new Uri("https://somebody:secret@mail.example/"),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.DoesNotContain("secret", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("somebody", failure.Message, StringComparison.Ordinal);
        Assert.Empty(this.answers.Requests);
    }

    [Fact]
    public async Task ReachAsync_NoCandidate_IsRefused()
    {
        // Act, Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => this.probe.ReachAsync(null!, TestContext.Current.CancellationToken));
    }
}
