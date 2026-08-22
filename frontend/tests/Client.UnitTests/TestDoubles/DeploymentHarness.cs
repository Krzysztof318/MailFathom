// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Authorization;
using MailFathom.Client.Backend.Authorization.Redirect;

namespace MailFathom.Client.UnitTests.TestDoubles;

/// <summary>One deployment, scripted end to end, with everything it composes owned in one place.</summary>
/// <remarks>
/// <para>
/// The pieces the client is built from are disposable and several of them wrap each other, so a test that assembled
/// them by hand would either leak them or spend more lines releasing them than asserting. Owning the whole graph here
/// keeps a test to its arrangement and its assertion.
/// </para>
/// <para>
/// The stubs are created from descriptions rather than handed in, which is the same reason: what a test states is how
/// the deployment and the authorization server behave, not which objects hold that behaviour.
/// </para>
/// </remarks>
internal sealed class DeploymentHarness : IDisposable
{
    /// <summary>The address every harness reaches its deployment at.</summary>
    internal static readonly Uri DeploymentAddress = new("https://mail.example/");

    private readonly HttpClient deploymentTransport;
    private readonly HttpClient authorizationServerTransport;
    private readonly AccessTokenHandler? tokenHandler;

    /// <summary>Builds a deployment answering from a script, reached with or without a signed-in token.</summary>
    /// <param name="deployment">How the deployment answers.</param>
    /// <param name="authorizationServer">How the authorization server answers.</param>
    /// <param name="redirect">What the person's approval comes back as.</param>
    /// <param name="throughTokenHandler">Whether requests to the deployment go through the handler that attaches the token, which is how the registration composes them.</param>
    internal DeploymentHarness(
        Func<HttpRequestMessage, HttpResponseMessage> deployment,
        Func<HttpRequestMessage, HttpResponseMessage>? authorizationServer = null,
        Func<Uri, SignInRedirect>? redirect = null,
        bool throughTokenHandler = false)
    {
        this.Deployment = new StubTransport(deployment);
        this.AuthorizationServer = new StubTransport(authorizationServer ?? (_ => StubTransport.JsonResponse("{}")));
        this.Listener = new StubSignInRedirectListener(redirect ?? (_ => new SignInRedirect(null, null, null)));

        if (throughTokenHandler)
        {
            this.tokenHandler = new AccessTokenHandler(this.Tokens) { InnerHandler = this.Deployment };
        }

        this.deploymentTransport = new HttpClient((HttpMessageHandler?)this.tokenHandler ?? this.Deployment)
        {
            BaseAddress = DeploymentAddress,
        };

        this.authorizationServerTransport = new HttpClient(this.AuthorizationServer);

        this.Client = new DeploymentClient(this.deploymentTransport);

        this.SignIn = new DeploymentSignIn(
            this.deploymentTransport,
            this.authorizationServerTransport,
            new DeploymentOptions(DeploymentAddress, "the-client"),
            this.Listener,
            this.Tokens);
    }

    /// <summary>Gets what the deployment was asked, in order.</summary>
    internal StubTransport Deployment { get; }

    /// <summary>Gets what the authorization server was asked, in order.</summary>
    internal StubTransport AuthorizationServer { get; }

    /// <summary>Gets the stand-in for the head's redirect listener.</summary>
    internal StubSignInRedirectListener Listener { get; }

    /// <summary>Gets where a completed sign-in's token is held.</summary>
    internal AccessTokenStore Tokens { get; } = new();

    /// <summary>Gets the client under test.</summary>
    internal DeploymentClient Client { get; }

    /// <summary>Gets the sign-in under test.</summary>
    internal DeploymentSignIn SignIn { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        this.deploymentTransport.Dispose();
        this.authorizationServerTransport.Dispose();
        this.tokenHandler?.Dispose();
        this.Deployment.Dispose();
        this.AuthorizationServer.Dispose();
        this.Listener.Dispose();
    }
}
