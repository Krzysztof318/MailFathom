// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using Microsoft.AspNetCore.HttpOverrides;

namespace MailFathom.Host.Security.Transport;

/// <summary>Composes the rule under which a forwarded scheme and host are applied to a request.</summary>
/// <remarks>
/// <para>
/// The platform's own forwarded-headers middleware does the work, and this states the policy it runs under. Two of
/// its defaults are deliberately replaced rather than accepted: it trusts loopback, which is the wrong peer inside a
/// container, and it processes <c>X-Forwarded-For</c> when asked to, which this deployment never asks for.
/// </para>
/// <para>
/// A policy is composed on every startup, because there is no posture in which no forwarded header is read. A section
/// naming no proxy resolves to trusting every address, so the lists cleared below are repopulated with a prefix
/// covering each family rather than left empty; <see cref="ReverseProxyOptions.TrustedProxies" /> states what that
/// gives up and the startup warning names it.
/// </para>
/// <para>
/// The client address is out of scope on purpose. Nothing here partitions, limits, or logs by remote address, so
/// rewriting <see cref="ConnectionInfo.RemoteIpAddress" /> from a header would replace the
/// one address this process observes for itself — the peer that opened the connection — with one an upstream wrote,
/// and buy nothing for it.
/// </para>
/// </remarks>
internal static class TrustedReverseProxyExtensions
{
    /// <summary>Adds the policy under which a trusted proxy's forwarded scheme and host reach the request.</summary>
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
            forwardedHeaders.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
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
}
