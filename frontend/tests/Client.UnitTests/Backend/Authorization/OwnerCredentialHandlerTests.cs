// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Client.Backend.Authorization;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Backend.Authorization;

/// <summary>What the transport pipeline puts on a request, and where a credential is never allowed to appear.</summary>
public sealed class OwnerCredentialHandlerTests
{
    private static readonly Uri Deployment = new("https://mail.example/");

    [Fact]
    public async Task SendAsync_WithNobodySignedIn_SendsTheRequestUnauthenticated()
    {
        // Arrange
        using var deployment = new StubTransport(_ => StubTransport.JsonResponse("{}"));
        using var handler = new OwnerCredentialHandler(new SignedInOwner(UnkeptOwnerCredentialStore.Instance))
        {
            InnerHandler = deployment,
        };

        using var transport = new HttpClient(handler);

        // Act
        using var answer = await transport.GetAsync(
            new Uri(Deployment, "api/client/session"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(Assert.Single(deployment.Requests).Authorization);
    }

    [Fact]
    public async Task SendAsync_AfterSigningIn_PresentsTheCredentialAsAnRfc7617BasicHeader()
    {
        // Arrange
        using var deployment = new StubTransport(_ => StubTransport.JsonResponse("{}"));

        var owner = new SignedInOwner(new StubOwnerCredentialStore());

        await owner.AcceptAsync(
            Deployment,
            new OwnerCredential("ada", "a-long-password"),
            TestContext.Current.CancellationToken);

        using var handler = new OwnerCredentialHandler(owner) { InnerHandler = deployment };
        using var transport = new HttpClient(handler);

        // Act
        using var answer = await transport.GetAsync(
            new Uri(Deployment, "api/client/session"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes("ada:a-long-password"))}",
            Assert.Single(deployment.Requests).Authorization);
    }

    /// <summary>
    /// A credential in a query reaches every access log, proxy, and browser history on the path — and this one is a
    /// password rather than a token that would have expired, so the exposure would have no end.
    /// </summary>
    [Fact]
    public async Task SendAsync_AfterSigningIn_PutsNothingOfTheCredentialInTheAddress()
    {
        // Arrange
        using var deployment = new StubTransport(_ => StubTransport.JsonResponse("{}"));

        var owner = new SignedInOwner(new StubOwnerCredentialStore());

        await owner.AcceptAsync(
            Deployment,
            new OwnerCredential("ada", "a-long-password"),
            TestContext.Current.CancellationToken);

        using var handler = new OwnerCredentialHandler(owner) { InnerHandler = deployment };
        using var transport = new HttpClient(handler);

        // Act
        using var answer = await transport.GetAsync(
            new Uri(Deployment, "api/client/emails?folder=inbox"),
            TestContext.Current.CancellationToken);

        // Assert
        var asked = Assert.Single(deployment.Requests).RequestUri.ToString();

        Assert.DoesNotContain("a-long-password", asked, StringComparison.Ordinal);
        Assert.DoesNotContain("ada", asked, StringComparison.Ordinal);
    }

    /// <summary>Signing out mid-run is answered by the next request carrying nothing, without a transport being rebuilt.</summary>
    [Fact]
    public async Task SendAsync_AfterSigningOut_StopsPresentingTheCredential()
    {
        // Arrange
        using var deployment = new StubTransport(_ => StubTransport.JsonResponse("{}"));

        var owner = new SignedInOwner(new StubOwnerCredentialStore());

        await owner.AcceptAsync(
            Deployment,
            new OwnerCredential("ada", "a-long-password"),
            TestContext.Current.CancellationToken);

        using var handler = new OwnerCredentialHandler(owner) { InnerHandler = deployment };
        using var transport = new HttpClient(handler);

        using (var first = await transport.GetAsync(
            new Uri(Deployment, "api/client/session"),
            TestContext.Current.CancellationToken))
        {
            await owner.ForgetAsync(TestContext.Current.CancellationToken);
        }

        // Act
        using var answer = await transport.GetAsync(
            new Uri(Deployment, "api/client/session"),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, deployment.Requests.Count);
        Assert.NotNull(deployment.Requests[0].Authorization);
        Assert.Null(deployment.Requests[1].Authorization);
    }

    [Fact]
    public void Constructor_NoSignedInOwner_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new OwnerCredentialHandler(null!));
    }
}
