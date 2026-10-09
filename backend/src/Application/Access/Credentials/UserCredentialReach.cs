// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Credentials;

/// <summary>Where one credential may be presented: on which endpoints, and from which networks.</summary>
/// <param name="Surfaces">The endpoints the credential is good for, in published order.</param>
/// <param name="AllowedSourceNetworks">The networks a request presenting it must come from, empty when it may come from anywhere.</param>
/// <remarks>
/// <para>
/// Both halves belong to the credential rather than to the person, because the threat each answers is about one
/// credential: a key provisioned to read mail must not administer the deployment, and a key that leaks is one key —
/// a person's workstation key and their scheduled job's key are used from different places.
/// </para>
/// <para>
/// A request presenting a credential on a surface it does not list, or from a network it does not allow, fails
/// authentication exactly as a credential nobody holds does, so a probe learns nothing about which credentials exist.
/// </para>
/// </remarks>
public sealed record UserCredentialReach(
    IReadOnlyList<UserCredentialSurface> Surfaces,
    IReadOnlyList<IPNetwork> AllowedSourceNetworks)
{
    /// <summary>The most networks one credential may name, which bounds what every presentation compares against.</summary>
    public const int MaximumAllowedSourceNetworks = 32;

    /// <summary>Gets what a credential states when nothing was written for it: both mail-serving endpoints, from anywhere.</summary>
    public static UserCredentialReach Default { get; } = new(UserCredentialSurface.Default, []);

    /// <summary>Gets whether the credential may be presented on the given endpoint.</summary>
    /// <param name="surface">The endpoint judging the request.</param>
    /// <returns><see langword="true" /> when the credential lists the endpoint; otherwise <see langword="false" />.</returns>
    public bool Lists(UserCredentialSurface surface) => this.Surfaces.Contains(surface);

    /// <summary>Gets whether a request from the given address may present the credential.</summary>
    /// <param name="source">The client address the forwarded-headers policy resolved, or <see langword="null" /> where none is known.</param>
    /// <returns><see langword="true" /> when the credential names no network, or one of its networks holds the address.</returns>
    /// <remarks>An IPv4 address arriving mapped into IPv6 is compared in its IPv4 form, which is how a dual-stack listener reports one and how a network is stored.</remarks>
    public bool AdmitsSource(IPAddress? source)
    {
        if (this.AllowedSourceNetworks.Count == 0)
        {
            return true;
        }

        if (source is null)
        {
            return false;
        }

        var comparable = source.IsIPv4MappedToIPv6 ? source.MapToIPv4() : source;

        return this.AllowedSourceNetworks.Any(network => network.Contains(comparable));
    }
}
