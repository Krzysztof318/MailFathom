// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;

namespace MailFathom.Client.Backend;

/// <summary>Which MailFathom deployment this client reaches, and as which registered client it signs in to one.</summary>
/// <remarks>
/// <para>
/// Every value here comes from whoever composes the application. Nothing in this assembly has a default address, and
/// there is deliberately no fallback to a literal: a client that guesses where its deployment is would reach somebody
/// else's on a mistyped configuration, and MailFathom is a single-tenant service somebody runs for their own mail.
/// </para>
/// <para>
/// The client identifier is public information, unlike a client secret, which this application holds none of and could
/// hold none of — a desktop binary and a WebAssembly bundle are both readable by whoever runs them, which is exactly
/// the situation RFC 7636 defines a public client for. That is why every grant here is bound by a proof key.
/// </para>
/// </remarks>
public sealed record DeploymentOptions
{
    /// <summary>The request timeout applied when the composing host states none.</summary>
    /// <remarks>
    /// Long enough for a mailbox query against a deployment on a slow link, short enough that a screen waiting on it
    /// reports a failure rather than appearing to hang. A host with a slower deployment states its own.
    /// </remarks>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Initializes the options one deployment is reached under.</summary>
    /// <param name="address">The deployment's base address, which every route is resolved against.</param>
    /// <param name="clientId">The client identifier registered with the deployment's authorization server.</param>
    /// <param name="timeout">How long a single request may take, or <see langword="null" /> for <see cref="DefaultTimeout" />.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="address" /> or <paramref name="clientId" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the address is not an absolute web address, is clear text to a host that is not loopback, or carries more than an origin, or when the client identifier is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the timeout is not positive.</exception>
    public DeploymentOptions(Uri address, string clientId, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        if (!address.IsAbsoluteUri
            || (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException(
                $"'{address}' is not an absolute http or https address, so no route could be resolved against it.",
                nameof(address));
        }

        // The registration puts the access token on every request sent to this address, so clear text to anything but
        // this machine hands the token to whatever is on the path. Loopback is where clear text is a development
        // posture rather than an exposure, which is the same line backend/src/Host/Configuration/DeploymentOptions.cs
        // draws about the address that deployment publishes.
        if (address.Scheme == Uri.UriSchemeHttp && !IsLoopback(address))
        {
            throw new ArgumentException(
                $"'{address}' is clear text to a host that is not loopback. Every request this client sends carries the signed-in token, so state an https address.",
                nameof(address));
        }

        // An origin, not a mount point. MailFathom serves its client surface at /api/client and derives the resource
        // identifier a token is issued for from that same prefix, so there is no sub-path deployment to support — and a
        // path written here would be dropped silently when a route resolves against it, which is a deployment somebody
        // configured and never reached, with nothing saying why. Embedded credentials are refused by the same check
        // rather than by one of their own: this client authenticates with the token it was issued, so a password in an
        // address is a credential nothing here would use and everything here would carry.
        if (address.AbsolutePath != "/"
            || !string.IsNullOrEmpty(address.Query)
            || !string.IsNullOrEmpty(address.Fragment)
            || !string.IsNullOrEmpty(address.UserInfo))
        {
            throw new ArgumentException(
                $"'{address}' carries more than an origin. MailFathom serves the client surface at '{DeploymentRoutes.Prefix}', so state the scheme, host, and port and nothing else.",
                nameof(address));
        }

        var resolvedTimeout = timeout ?? DefaultTimeout;

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(resolvedTimeout, TimeSpan.Zero, nameof(timeout));

        this.Address = address;
        this.ClientId = clientId;
        this.Timeout = resolvedTimeout;
    }

    /// <summary>Gets the deployment's base address.</summary>
    public Uri Address { get; }

    /// <summary>Gets the client identifier presented to the authorization server.</summary>
    public string ClientId { get; }

    /// <summary>Gets how long a single request to the deployment may take.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>Decides whether an address names this machine, which is where clear text is a posture rather than an exposure.</summary>
    /// <remarks>
    /// <see cref="Uri.IsLoopback" /> answers for an address literal and for the reserved name; a host that resolves to
    /// a loopback address elsewhere is not one, and treating it as one would let DNS decide whether the token travels
    /// in clear text.
    /// </remarks>
    private static bool IsLoopback(Uri address) =>
        address.IsLoopback
        || (IPAddress.TryParse(address.Host, out var literal) && IPAddress.IsLoopback(literal));
}
