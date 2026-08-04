// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;

namespace MailFathom.Host.Configuration.Endpoints;

/// <summary>Configures the clear-text listener that tells a client where a surface's HTTPS profiles moved to.</summary>
/// <remarks>
/// <para>
/// A redirect protects the next request rather than the one that arrived. An API key sent in clear text is already on the
/// wire by the time a redirect is written, and nothing recovers it. This listener exists so that a client still pointed
/// at <c>http://</c> is told where the endpoint is instead of failing on a connection refused or an unreadable handshake
/// error, which is what enabling TLS otherwise looks like from the outside. It is not a supported way to reach the
/// surface, which is why the listener answers nothing else: no route is mapped on it, and no authentication,
/// rate-limiting, CORS, or client-certificate handler runs for a request that arrived there.
/// </para>
/// <para>
/// On unless a deployment turns it off, and meaningful only where the surface terminates TLS of its own. A deployment
/// behind a proxy that already answers on the clear-text port turns it off rather than having MailFathom bind a port it
/// did not ask for, and one that writes this section for a surface terminating no TLS is refused at startup — the
/// setting would otherwise read as configured while nothing bound it.
/// </para>
/// </remarks>
internal sealed class TransportClearTextRedirectOptions
{
    /// <summary>Gets or sets whether the clear-text listener is bound at all.</summary>
    /// <remarks>Enabled, so enabling TLS does not read as an outage to a client nobody had repointed yet. It is honored only while the surface terminates TLS; there is no clear-text listener to redirect from otherwise, and the surface is already served over one.</remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the IP address the clear-text listener binds.</summary>
    /// <remarks>Separate from the HTTPS profiles' own bind addresses rather than derived from them, because profiles may bind several addresses and a redirect is one socket; a deployment publishing two addresses states which of them answers clear text.</remarks>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>Gets or sets the TCP port the clear-text listener binds, or <see langword="null" /> to take the surface's own default.</summary>
    /// <remarks>
    /// Nullable rather than carrying a number, because the default belongs to the surface: the MCP endpoint and the
    /// administrative endpoint each redirect to their own profiles, so one shared default would have the two collide the
    /// moment a deployment terminated TLS on both. Each surface states its own and documents it.
    /// </remarks>
    public int? Port { get; set; }

    /// <summary>Gets whether the deployment wrote this section, as opposed to inheriting every value above.</summary>
    /// <remarks>
    /// It is what tells a redirect an operator asked for from the default one, which is the difference between a startup
    /// error and silence on a surface that terminates no TLS. The binder cannot answer it — an absent section and one
    /// carrying only defaults bind identically — so it is read from configuration by the surface that owns the section.
    /// </remarks>
    internal bool WasStated { get; private set; }

    /// <summary>Records that the deployment wrote this section.</summary>
    /// <remarks>Called by the surface's own read, which is the only place that holds the configuration section this was bound from.</remarks>
    internal void MarkStated() => this.WasStated = true;

    /// <summary>Reads the socket the listener binds.</summary>
    /// <param name="defaultPort">The port the surface serves the redirect on when the deployment states none.</param>
    /// <returns>The address and port.</returns>
    /// <exception cref="FormatException">Thrown when <see cref="BindAddress" /> is not an IP address, which validation reports before anything reads this.</exception>
    internal TransportHttpsListenerAddress ListenerAddress(int defaultPort) =>
        new(IPAddress.Parse(this.BindAddress.Trim()), this.Port ?? defaultPort);

    /// <summary>Finds everything an operator must fix before the clear-text listener can bind.</summary>
    /// <param name="configurationPath">The configuration path of this section, which prefixes every reported error.</param>
    /// <returns>One message per faulty setting, empty when the section is usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurationPath" /> is <see langword="null" />.</exception>
    /// <remarks>Its own two settings only. Whether a redirect belongs on this surface at all, and whether its port is free, are questions about the surface rather than about this section, and <see cref="TransportHttpsOptions" /> answers both.</remarks>
    internal IEnumerable<string> FindConfigurationErrors(string configurationPath)
    {
        ArgumentNullException.ThrowIfNull(configurationPath);

        if (!IPAddress.TryParse(this.BindAddress?.Trim(), out _))
        {
            yield return $"{configurationPath}:{nameof(this.BindAddress)} — state the IP address the clear-text listener binds, for example '0.0.0.0' for every IPv4 address or '::' for IPv6.";
        }

        if (this.Port is { } statedPort && statedPort is < 1 or > 65535)
        {
            yield return $"{configurationPath}:{nameof(this.Port)} — '{statedPort}' is not a TCP port; state a value between 1 and 65535, or remove the setting to take this endpoint's default.";
        }
    }
}
