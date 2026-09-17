// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using Microsoft.AspNetCore.HttpOverrides;

namespace MailFathom.Host.Security.Transport;

/// <summary>Composes the rule under which a forwarded scheme, host, and client address are applied to a request.</summary>
/// <remarks>
/// <para>
/// The platform's own forwarded-headers middleware does the work, and this states the policy it runs under. One of its
/// defaults is deliberately replaced rather than accepted: it trusts loopback, which is the wrong peer inside a
/// container.
/// </para>
/// <para>
/// A policy is composed on every startup, because there is no posture in which no forwarded header is read. A section
/// naming no proxy resolves to trusting every address, so the lists cleared below are repopulated with a prefix
/// covering each family rather than left empty; <see cref="ReverseProxyOptions.TrustedProxies" /> states what that
/// gives up and the startup warning names it.
/// </para>
/// <para>
/// The client address is read only where a proxy is named, and named narrower than a whole address family. <see cref="ConnectionInfo.RemoteIpAddress" /> is what an
/// administrator's network restriction and the password attempt bound read, so behind a proxy it has to be the client
/// rather than the proxy — and on a section that believes every peer, rewriting it from a header would let any caller
/// write the address it is judged by. Refusing a network restriction on such a section is startup's job, so here the
/// header is simply left unread and the address stays the peer that opened the connection.
/// </para>
/// </remarks>
internal static class TrustedReverseProxyExtensions
{
    /// <summary>Adds the policy under which a trusted proxy's forwarded scheme, host, and client address reach the request.</summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="reverseProxySettings">The reverse-proxy settings composition read.</param>
    /// <returns>The container, so composition reads as one sequence.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> or <paramref name="reverseProxySettings" /> is <see langword="null" />.</exception>
    /// <exception cref="FormatException">Thrown when the settings have not passed <see cref="ReverseProxyOptions.FindConfigurationErrors" />.</exception>
    internal static IServiceCollection AddTrustedReverseProxy(
        this IServiceCollection services,
        ReverseProxyOptions reverseProxySettings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(reverseProxySettings);

        var trustedAddresses = reverseProxySettings.ToTrustedProxyAddresses();
        var trustedNetworks = reverseProxySettings.ToTrustedProxyNetworks();

        return services.Configure<ForwardedHeadersOptions>(forwardedHeaders =>
        {
            forwardedHeaders.ForwardedHeaders = ForwardedHeadersFor(reverseProxySettings);
            forwardedHeaders.ForwardLimit = reverseProxySettings.MaximumForwardedHops;

            // Both lists arrive holding loopback. Left in place, a deployment that named its ingress controller would
            // also believe anything on the machine, which is the whole of a shared host in a native installation and
            // every sidecar in a pod.
            forwardedHeaders.KnownProxies.Clear();
            forwardedHeaders.KnownIPNetworks.Clear();

            foreach (var address in trustedAddresses)
            {
                forwardedHeaders.KnownProxies.Add(address);
            }

            foreach (var network in trustedNetworks)
            {
                forwardedHeaders.KnownIPNetworks.Add(network);
            }
        });
    }

    /// <summary>Names the forwarded headers a request is rewritten from under the given settings.</summary>
    /// <param name="reverseProxySettings">The reverse-proxy settings composition read.</param>
    /// <returns>The scheme and the host always, and the client address only where <see cref="ReverseProxyOptions.ForwardsTheClientAddress" /> says so.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reverseProxySettings" /> is <see langword="null" />.</exception>
    /// <exception cref="FormatException">Thrown when the settings have not passed <see cref="ReverseProxyOptions.FindConfigurationErrors" />.</exception>
    internal static ForwardedHeaders ForwardedHeadersFor(ReverseProxyOptions reverseProxySettings)
    {
        ArgumentNullException.ThrowIfNull(reverseProxySettings);

        var schemeAndHost = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;

        return reverseProxySettings.ForwardsTheClientAddress() ? schemeAndHost | ForwardedHeaders.XForwardedFor : schemeAndHost;
    }
}
