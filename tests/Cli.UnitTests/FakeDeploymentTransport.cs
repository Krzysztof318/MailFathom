// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using MailFathom.Cli.Credentials;
using MailFathom.Cli.Transport;

namespace MailFathom.Cli.UnitTests;

/// <summary>The transport seam a command is given, with the network substituted and the certificate decision real.</summary>
/// <remarks>
/// The policy is not faked. A test that wants a deployment whose certificate this machine would refuse hands the real
/// <see cref="ServerCertificatePolicy" /> a real certificate and the errors a handshake would have reported, and takes
/// whatever it decides. Substituting the decision instead would leave the one rule these tests exist for — that a pin
/// accepts exactly one certificate — asserted against a double rather than against the code.
/// </remarks>
internal static class FakeDeploymentTransport
{
    /// <summary>Builds a transport over a handler, for a deployment whose certificate never comes up.</summary>
    /// <param name="handler">The handler every request goes through.</param>
    /// <param name="address">The address the transport is aimed at.</param>
    /// <param name="trust">What the profile accepted about this deployment's transport.</param>
    /// <returns>The transport.</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The client is handed to the DeploymentTransport the command disposes, which disposes it; disposing it here would return a transport with nothing to send through.")]
    internal static DeploymentTransport Over(
        HttpMessageHandler handler,
        Uri address,
        StoredTransportTrust trust) =>
        new(
            new HttpClient(handler, disposeHandler: false) { BaseAddress = address },
            new ServerCertificatePolicy(trust.PinnedCertificateFingerprint));

    /// <summary>Builds a transport that has already met the certificate a deployment presents.</summary>
    /// <param name="handler">The handler every request goes through, which is one that fails when the certificate was refused.</param>
    /// <param name="address">The address the transport is aimed at.</param>
    /// <param name="trust">What the profile accepted about this deployment's transport.</param>
    /// <param name="presented">The certificate the deployment presents.</param>
    /// <param name="errors">What this machine found wrong with it, which is none for a certificate refused only by a pin.</param>
    /// <returns>The transport, refusing or not exactly as the real policy decided.</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The client is handed to the DeploymentTransport the command disposes, which disposes it; disposing it here would return a transport with nothing to send through.")]
    internal static DeploymentTransport Presenting(
        HttpMessageHandler handler,
        Uri address,
        StoredTransportTrust trust,
        X509Certificate2 presented,
        SslPolicyErrors errors = SslPolicyErrors.RemoteCertificateChainErrors)
    {
        ServerCertificatePolicy policy = new(trust.PinnedCertificateFingerprint);

        policy.Accepts(presented, chain: null, errors);

        return new DeploymentTransport(
            new HttpClient(handler, disposeHandler: false) { BaseAddress = address },
            policy);
    }
}
