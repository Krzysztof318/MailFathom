// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Authorization;

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
/// the deployment behaves, not which objects hold that behaviour.
/// </para>
/// </remarks>
internal sealed class DeploymentHarness : IDisposable
{
    /// <summary>The address every harness reaches its deployment at.</summary>
    internal static readonly Uri DeploymentAddress = new("https://mail.example/");

    private readonly HttpClient deploymentTransport;
    private readonly HttpClient signInTransport;
    private readonly HttpClient probeTransport;
    private readonly OwnerCredentialHandler? credentialHandler;

    /// <summary>Builds a deployment answering from a script, reached with or without the signed-in credential.</summary>
    /// <param name="deployment">How the deployment answers.</param>
    /// <param name="signIn">How the deployment answers a credential offered to it, where that differs from the above.</param>
    /// <param name="store">Where a completed sign-in is kept, which defaults to a head that keeps nothing.</param>
    /// <param name="throughCredentialHandler">Whether requests to the deployment go through the handler that presents the credential, which is how the registration composes them.</param>
    private DeploymentHarness(
        Func<HttpRequestMessage, HttpResponseMessage> deployment,
        Func<HttpRequestMessage, HttpResponseMessage>? signIn = null,
        IOwnerCredentialStore? store = null,
        bool throughCredentialHandler = false)
    {
        this.Store = store ?? UnkeptOwnerCredentialStore.Instance;
        this.Owner = new SignedInOwner(this.Store);
        this.Address = new DeploymentAddress(this.Owner);

        this.Deployment = new StubTransport(deployment);
        this.SignedInAt = new StubTransport(signIn ?? deployment);
        this.Probed = new StubTransport(deployment);

        if (throughCredentialHandler)
        {
            this.credentialHandler = new OwnerCredentialHandler(this.Owner) { InnerHandler = this.Deployment };
        }

        this.deploymentTransport = new HttpClient((HttpMessageHandler?)this.credentialHandler ?? this.Deployment)
        {
            BaseAddress = DeploymentAddress,
        };

        // The sign-in and the probe are aimed by absolute address rather than by a base one, exactly as the
        // registration leaves them, so the same scripted deployment answers both.
        this.signInTransport = new HttpClient(this.SignedInAt);
        this.probeTransport = new HttpClient(this.Probed);

        this.Transports = new StubHttpClientFactory(
            new Dictionary<string, HttpClient>(StringComparer.Ordinal)
            {
                [DeploymentHttpClients.Deployment] = this.deploymentTransport,
                [DeploymentHttpClients.SignIn] = this.signInTransport,
                [DeploymentHttpClients.DeploymentProbe] = this.probeTransport,
            });

        this.Client = new DeploymentClient(this.Transports);
        this.Probe = new DeploymentProbe(this.Transports);
        this.SignIn = new DeploymentSignIn(this.Transports, this.Address, this.Owner);
    }

    /// <summary>Gets what the deployment was asked, in order.</summary>
    internal StubTransport Deployment { get; }

    /// <summary>Gets what the deployment was asked while a credential was being offered to it, in order.</summary>
    /// <remarks>Separate from <see cref="Deployment" /> although it can answer the same way, because what a test asserts about a sign-in is which credential it carried — which is only visible if the two are not the same recording.</remarks>
    internal StubTransport SignedInAt { get; }

    /// <summary>Gets what the candidate address was asked while it was being probed, in order.</summary>
    /// <remarks>Separate for the same reason: what a test asserts about a probe is that it carried no credential.</remarks>
    internal StubTransport Probed { get; }

    /// <summary>Gets the factory the client, the sign-in, and the probe take their transports from.</summary>
    internal StubHttpClientFactory Transports { get; }

    /// <summary>Gets where this head keeps a credential, which by default is nowhere.</summary>
    internal IOwnerCredentialStore Store { get; }

    /// <summary>Gets who is signed in during this run.</summary>
    internal SignedInOwner Owner { get; }

    /// <summary>Gets which deployment the client is pointed at.</summary>
    internal DeploymentAddress Address { get; }

    /// <summary>Gets the probe under test.</summary>
    internal DeploymentProbe Probe { get; }

    /// <summary>Gets the client under test.</summary>
    internal DeploymentClient Client { get; }

    /// <summary>Gets the sign-in under test.</summary>
    internal DeploymentSignIn SignIn { get; }

    /// <summary>Builds the harness and points the client at the deployment.</summary>
    /// <param name="deployment">How the deployment answers.</param>
    /// <param name="signIn">How the deployment answers a credential offered to it, where that differs from the above.</param>
    /// <param name="store">Where a completed sign-in is kept, which defaults to a head that keeps nothing.</param>
    /// <param name="throughCredentialHandler">Whether requests to the deployment go through the handler that presents the credential, which is how the registration composes them.</param>
    /// <param name="pointed">Whether the client starts pointed at the deployment, which every caller but the probe needs.</param>
    /// <returns>The harness, ready for the test that owns it.</returns>
    /// <remarks>
    /// A factory rather than a constructor because pointing the client somewhere is asynchronous, and the alternative
    /// is a constructor that blocks on it. That it happens to complete without yielding on a client pointed nowhere yet
    /// is an implementation detail of <see cref="DeploymentAddress.PointAtAsync" />, and a
    /// test double may not be the thing that depends on it.
    /// </remarks>
    internal static async ValueTask<DeploymentHarness> CreateAsync(
        Func<HttpRequestMessage, HttpResponseMessage> deployment,
        Func<HttpRequestMessage, HttpResponseMessage>? signIn = null,
        IOwnerCredentialStore? store = null,
        bool throughCredentialHandler = false,
        bool pointed = true)
    {
        var harness = new DeploymentHarness(deployment, signIn, store, throughCredentialHandler);

        if (pointed)
        {
            await harness.Address.PointAtAsync(DeploymentAddress);
        }

        return harness;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.deploymentTransport.Dispose();
        this.signInTransport.Dispose();
        this.probeTransport.Dispose();
        this.credentialHandler?.Dispose();
        this.Deployment.Dispose();
        this.SignedInAt.Dispose();
        this.Probed.Dispose();
    }
}
