// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.X509Certificates;
using MailFathom.Cli.Credentials;

namespace MailFathom.Cli.Transport;

/// <summary>One connection aimed at one address, together with what it may accept there.</summary>
/// <remarks>
/// <para>
/// The client and the certificate policy travel as one value because a caller that meets a transport failure has to be
/// able to ask what refused it. <see cref="HttpClient" /> reports a refused handshake as an ordinary request failure
/// with no certificate in it, so a command holding the client alone could only say that the deployment could not be
/// reached — which is the wrong sentence for the one case an operator can act on.
/// </para>
/// <para>
/// A transport is opened per operation and disposed by whoever opened it, which is what keeps a pinned certificate from
/// outliving the profile it belongs to.
/// </para>
/// </remarks>
internal sealed class DeploymentTransport : IDisposable
{
    /// <summary>How long any single request to a deployment may take.</summary>
    /// <remarks>
    /// A person is waiting at a terminal, so the bound is what keeps an unreachable host from looking like a hung
    /// command. It is generous enough for a deployment behind a slow link and short enough that a wrong address is
    /// reported rather than waited out.
    /// </remarks>
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The largest response the command reads from anything, beyond which the request fails.</summary>
    /// <remarks>
    /// Every document the command fetches is a few kilobytes: a session, a protected resource metadata document, an
    /// authorization server's discovery document, a token response. The limit exists because two of the machines
    /// answering are not the deployment's — an authorization server is reached during a sign-in, and a mistyped or
    /// hijacked address is reached by definition — and none of them should be able to make the command buffer an
    /// unbounded body. The same number the service bounds its own metadata retrieval by, for the same reason.
    /// </remarks>
    internal const int ResponseSizeLimitInBytes = 256 * 1024;

    private readonly ServerCertificatePolicy policy;

    /// <summary>Initializes a transport over a client and the policy that decided what it accepted.</summary>
    /// <param name="client">The client, whose <see cref="HttpClient.BaseAddress" /> names what it is aimed at.</param>
    /// <param name="policy">What the connection may accept, and what it refused.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal DeploymentTransport(HttpClient client, ServerCertificatePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(policy);

        this.Client = client;
        this.policy = policy;
    }

    /// <summary>Gets the client requests are sent through.</summary>
    internal HttpClient Client { get; }

    /// <summary>Gets the certificate this connection refused, or <see langword="null" /> when it refused none.</summary>
    internal PresentedCertificate? RefusedCertificate => this.policy.Refused;

    /// <summary>Opens a transport aimed at one address, accepting only what the profile has already accepted.</summary>
    /// <param name="address">The address the transport is aimed at.</param>
    /// <param name="trust">What the operator has accepted about this deployment's transport.</param>
    /// <returns>The transport, which the caller disposes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// Four bounds, and each answers a different way a remote machine could misbehave. Redirects are not followed,
    /// because a redirect would move a request carrying a bearer credential to an address the operator never named. The
    /// timeout keeps an unresponsive host from looking like a hung command. The buffer limit stops any of the machines
    /// the command talks to from answering with an unbounded body — which matters most during a sign-in, where one of
    /// them is an authorization server rather than the deployment. The certificate policy is the fourth, and it is the
    /// only one an operator can widen, once, at sign-in.
    /// </remarks>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The handler is handed to the HttpClient with disposeHandler: true, so the client this transport disposes disposes it; disposing it here would leave that client without a transport.")]
    internal static DeploymentTransport Open(Uri address, StoredTransportTrust trust)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(trust);

        ServerCertificatePolicy policy = new(trust.PinnedCertificateFingerprint);

        SocketsHttpHandler handler = new() { AllowAutoRedirect = false };
        handler.SslOptions.RemoteCertificateValidationCallback =
            (_, certificate, chain, errors) => policy.Accepts(certificate as X509Certificate2, chain, errors);

        return new DeploymentTransport(
            new HttpClient(handler, disposeHandler: true)
            {
                BaseAddress = address,
                Timeout = RequestTimeout,
                MaxResponseContentBufferSize = ResponseSizeLimitInBytes,
            },
            policy);
    }

    /// <summary>Says why the connection was refused, when a certificate is what refused it.</summary>
    /// <returns>The sentence an operator reads, or <see langword="null" /> when the failure was something else.</returns>
    internal string? DescribeRefusal() => this.policy.DescribeRefusal(this.Client.BaseAddress);

    /// <inheritdoc />
    public void Dispose() => this.Client.Dispose();
}
