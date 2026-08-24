// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;

namespace MailFathom.Client.Backend;

/// <summary>Why an address is not one this client may be pointed at.</summary>
/// <remarks>
/// A reason rather than a message, because the two readers of this rule show it differently: a composing host raises
/// it as an exception naming the setting somebody wrote, and a screen shows a person a sentence in the language they
/// are reading in. A message decided here would be the wrong one for one of them.
/// </remarks>
public enum DeploymentAddressRefusal
{
    /// <summary>The address is one this client may be pointed at.</summary>
    None = 0,

    /// <summary>It is not an absolute <c>http</c> or <c>https</c> address, so no route could be resolved against it.</summary>
    NotAWebAddress = 1,

    /// <summary>It is clear text to a host that is not this machine, and every request this client sends carries the signed-in credential.</summary>
    ClearTextOffThisMachine = 2,

    /// <summary>It carries more than an origin — a path, a query, a fragment, or embedded credentials.</summary>
    MoreThanAnOrigin = 3,
}

/// <summary>What makes an address one this client may be pointed at, decided in one place for every head.</summary>
/// <remarks>
/// <para>
/// Stated once because it is judged twice and the two must never disagree: before a person's typed address is stored,
/// and again when whatever was stored is composed into the transports. A rule applied at only one of those points is a
/// client that accepts an address on one path and refuses it on the other.
/// </para>
/// <para>
/// It says nothing about whether a deployment is actually there. That is a question only the network can answer, and
/// <see cref="DeploymentProbe" /> is what asks it.
/// </para>
/// </remarks>
public static class DeploymentAddressRule
{
    /// <summary>Judges whether an address is one this client may be pointed at.</summary>
    /// <param name="address">The candidate, which may be <see langword="null" />.</param>
    /// <returns><see cref="DeploymentAddressRefusal.None" /> when it may be used, and why not otherwise.</returns>
    public static DeploymentAddressRefusal Judge(Uri? address)
    {
        if (address is null
            || !address.IsAbsoluteUri
            || (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp))
        {
            return DeploymentAddressRefusal.NotAWebAddress;
        }

        // Every request this client sends carries the signed-in credential, so clear text to anything but this machine
        // hands that credential to whatever is on the path. Loopback is where clear text is a development posture
        // rather than an exposure, which is the same line backend/src/Host/Configuration/DeploymentOptions.cs draws
        // about the address a deployment publishes.
        if (address.Scheme == Uri.UriSchemeHttp && !IsLoopback(address))
        {
            return DeploymentAddressRefusal.ClearTextOffThisMachine;
        }

        // An origin, not a mount point. MailFathom serves its client surface at /api/client and derives the resource
        // identifier a token is issued for from that same prefix, so there is no sub-path deployment to support — and a
        // path written here would be dropped silently when a route resolves against it, which is a deployment somebody
        // configured and never reached, with nothing saying why. Embedded credentials are refused by the same check
        // rather than by one of their own: this client authenticates with the credential it presents on each request,
        // so one written into an address is a secret nothing here would use and everything here would carry.
        return address.AbsolutePath != "/"
            || !string.IsNullOrEmpty(address.Query)
            || !string.IsNullOrEmpty(address.Fragment)
            || !string.IsNullOrEmpty(address.UserInfo)
                ? DeploymentAddressRefusal.MoreThanAnOrigin
                : DeploymentAddressRefusal.None;
    }

    /// <summary>Decides whether an address names this machine, which is where clear text is a posture rather than an exposure.</summary>
    /// <remarks>
    /// <see cref="Uri.IsLoopback" /> answers for an address literal and for the reserved name; a host that resolves to
    /// a loopback address elsewhere is not one, and treating it as one would let DNS decide whether the credential
    /// travels in clear text.
    /// </remarks>
    private static bool IsLoopback(Uri address) =>
        address.IsLoopback
        || (IPAddress.TryParse(address.Host, out var literal) && IPAddress.IsLoopback(literal));
}
