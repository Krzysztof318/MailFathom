// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Net.Sockets;

namespace MailFathom.Host.Configuration.Endpoints;

/// <summary>Configures whether Kestrel terminates TLS for the MCP endpoint itself, and under which identities.</summary>
/// <remarks>
/// <para>
/// Empty is the default and means the endpoint is served over whatever listener the host is already configured with,
/// which is clear-text HTTP unless something else supplies TLS. That posture is deliberately kept rather than
/// deprecated: it is what local development runs, and it is what a deployment behind a TLS-terminating reverse proxy
/// runs, where a second TLS layer inside the trust boundary buys nothing. Startup warns about it, because a clear-text
/// endpoint reachable from anywhere is a different thing from one reachable only from the machine or the proxy in
/// front of it, and only an operator knows which they have.
/// </para>
/// <para>
/// Configuring any profile takes the opposite posture in full: Kestrel binds exactly the profiles named here and the
/// listeners the host would otherwise have opened are not opened. There is no mixed state in which an HTTPS profile is
/// served and a clear-text listener quietly stays behind it, because that listener would serve the same mailbox
/// without the protection the profile was configured to add.
/// </para>
/// </remarks>
internal sealed class TransportHttpsOptions
{
    /// <summary>Gets the HTTPS profiles served, empty when Kestrel terminates no TLS of its own.</summary>
    public IList<TransportHttpsEndpointOptions> Endpoints { get; } = [];

    /// <summary>Gets or sets the clear-text listener that tells a client still pointed at <c>http://</c> where these profiles are.</summary>
    public TransportClearTextRedirectOptions Redirect { get; set; } = new();

    /// <summary>Gets whether any profile is configured, which is what decides between the two postures.</summary>
    internal bool TerminatesTls => this.Endpoints.Count > 0;

    /// <summary>Gets whether a clear-text listener is bound to redirect to these profiles.</summary>
    /// <remarks>Both halves are required, and the first is what keeps the enabled-by-default setting silent on a surface that terminates no TLS: there is no clear-text listener to redirect away from, because the surface is already served over one.</remarks>
    internal bool RedirectsClearText => this.TerminatesTls && this.Redirect.Enabled;

    /// <summary>Reads the HTTPS port each configured domain is published on, which is what a redirect resolves against.</summary>
    /// <returns>One entry per profile, keyed by the domain it publishes, matched without regard to case the way a host name is.</returns>
    /// <remarks>Every profile carries a domain and no two share one, both of which validation has already proven by the time a composed redirect reads this.</remarks>
    internal IReadOnlyDictionary<string, int> PublishedDomainPorts() =>
        this.Endpoints.ToDictionary(
            static endpoint => endpoint.Domain.Trim(),
            static endpoint => endpoint.Port,
            StringComparer.OrdinalIgnoreCase);

    /// <summary>Finds everything an operator must fix before the configured profiles can be served.</summary>
    /// <param name="configurationPath">The configuration path of this section, which prefixes every reported error.</param>
    /// <param name="http3Supported">Whether the host platform can provide the QUIC transport HTTP/3 needs.</param>
    /// <param name="defaultRedirectPort">The port this surface's clear-text redirect binds when the deployment states none.</param>
    /// <returns>One message per faulty setting, empty when the section is usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurationPath" /> is <see langword="null" />.</exception>
    internal IReadOnlyList<string> FindConfigurationErrors(
        string configurationPath,
        bool http3Supported,
        int defaultRedirectPort)
    {
        ArgumentNullException.ThrowIfNull(configurationPath);

        var errors = new List<string>(this.Endpoints
            .Index()
            .SelectMany(entry => entry.Item.FindConfigurationErrors(
                $"{configurationPath}:{nameof(this.Endpoints)}:{entry.Index}",
                http3Supported)));

        errors.AddRange(this.FindCollidingIdentities(configurationPath));
        errors.AddRange(this.FindListenerDisagreements(configurationPath));
        errors.AddRange(this.FindOverlappingListeners(configurationPath));
        errors.AddRange(this.FindRedirectErrors(configurationPath, defaultRedirectPort));

        return errors;
    }

    /// <summary>Refuses a redirect this surface cannot serve, and one whose socket a profile of its own already binds.</summary>
    /// <remarks>
    /// A redirect stated for a surface that terminates no TLS is refused rather than ignored, because that surface is
    /// already reachable in clear text: the setting would read as configured while nothing bound it and nothing redirected
    /// anywhere. The port check is the within-surface half of the collision rule — these profiles are the only listeners
    /// this section can see, and the surface that owns it compares the same port against every other listener the process
    /// opens.
    /// </remarks>
    private IEnumerable<string> FindRedirectErrors(string configurationPath, int defaultRedirectPort)
    {
        var sectionPath = $"{configurationPath}:{nameof(this.Redirect)}";

        if (!this.TerminatesTls)
        {
            if (this.Redirect.WasStated)
            {
                yield return $"{sectionPath} — a clear-text redirect is configured while {configurationPath}:{nameof(this.Endpoints)} names no HTTPS profile, so there is nothing to redirect to and this surface is already served in clear text. Configure a profile, or remove this section.";
            }

            yield break;
        }

        foreach (var error in this.Redirect.FindConfigurationErrors(sectionPath))
        {
            yield return error;
        }

        if (!this.Redirect.Enabled)
        {
            yield break;
        }

        var redirectPort = this.Redirect.Port ?? defaultRedirectPort;

        if (this.Endpoints.Any(endpoint => endpoint.Port == redirectPort))
        {
            yield return $"{sectionPath}:{nameof(TransportClearTextRedirectOptions.Port)} — port {redirectPort} is bound by an HTTPS profile in this section, and one socket cannot serve both schemes. State a port no profile uses, or turn the redirect off.";
        }
    }

    /// <summary>Refuses two profiles that cannot be told apart, by the name diagnostics use or by the name a handshake selects on.</summary>
    private IEnumerable<string> FindCollidingIdentities(string configurationPath)
    {
        var sectionPath = $"{configurationPath}:{nameof(this.Endpoints)}";

        var repeatedNames = this.Endpoints
            .Select(static endpoint => endpoint.Name?.Trim() ?? string.Empty)
            .Where(static name => name.Length > 0)
            .GroupBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key);

        foreach (var name in repeatedNames)
        {
            yield return $"{sectionPath} — '{name}' names more than one HTTPS profile, so a diagnostic about one of them could not say which.";
        }

        var repeatedDomains = this.Endpoints
            .Select(static endpoint => endpoint.Domain?.Trim() ?? string.Empty)
            .Where(static domain => domain.Length > 0)
            .GroupBy(static domain => domain, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key);

        foreach (var domain in repeatedDomains)
        {
            yield return $"{sectionPath} — '{domain}' is published by more than one HTTPS profile, so which certificate a handshake for it receives would be decided by configuration order rather than by an operator.";
        }
    }

    /// <summary>Refuses profiles that share a listener while disagreeing about what that listener serves.</summary>
    /// <remarks>
    /// The TLS floor is settled per connection, once the client's server name is known, so profiles sharing an address
    /// may each keep their own. The set of HTTP versions cannot be: ALPN offers what the listener was bound with, and
    /// HTTP/3 is a second socket the listener either opens or does not. Silently taking one profile's set for the
    /// other's connections would serve a version that profile never named.
    /// </remarks>
    private IEnumerable<string> FindListenerDisagreements(string configurationPath)
    {
        var sectionPath = $"{configurationPath}:{nameof(this.Endpoints)}";

        var listenerGroups = this.Endpoints
            .Where(static endpoint => IPAddress.TryParse(endpoint.BindAddress?.Trim(), out _))
            .GroupBy(static endpoint => endpoint.ListenerAddress);

        foreach (var listener in listenerGroups)
        {
            var declaredSets = listener
                .Select(static endpoint => string.Join(
                    ',',
                    endpoint.ServedHttpProtocols.Order().Select(static protocol => protocol.ToString())))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (declaredSets.Length > 1)
            {
                yield return $"{sectionPath} — the profiles sharing {listener.Key.Address}:{listener.Key.Port} name different HTTP versions ({string.Join(" and ", declaredSets)}); profiles on one listener are told apart by server name during the handshake, which is after the HTTP versions have already been offered.";
            }
        }
    }

    /// <summary>Refuses a port whose profiles ask for two sockets the operating system will only grant one of.</summary>
    /// <remarks>
    /// Profiles naming the same address share one listener, which is intended. Naming a wildcard address beside a
    /// specific one on the same port is not: the wildcard socket already accepts the connections the second listener
    /// was bound for, so the second bind fails and takes the whole process down with an address-in-use error that names
    /// a socket rather than the profile that asked for it. Reporting it here turns that into a configuration message
    /// read before any certificate is loaded.
    /// </remarks>
    private IEnumerable<string> FindOverlappingListeners(string configurationPath)
    {
        var sectionPath = $"{configurationPath}:{nameof(this.Endpoints)}";

        var portGroups = this.Endpoints
            .Where(static endpoint => IPAddress.TryParse(endpoint.BindAddress?.Trim(), out _))
            .GroupBy(static endpoint => endpoint.ListenerAddress.Port);

        foreach (var port in portGroups)
        {
            var addresses = port
                .Select(static endpoint => endpoint.ListenerAddress.Address)
                .Distinct()
                .ToArray();

            foreach (var wildcard in addresses.Where(IsWildcard))
            {
                var covered = addresses
                    .Where(address => !address.Equals(wildcard) && Covers(wildcard, address))
                    .ToArray();

                if (covered.Length > 0)
                {
                    yield return $"{sectionPath} — profiles on port {port.Key} bind {wildcard} as well as {string.Join(" and ", covered.AsEnumerable())}; {wildcard} already accepts the connections those addresses would receive, so only one of the two listeners could bind. State one address for a port, or move a profile to a port of its own.";
                }
            }
        }
    }

    /// <summary>Reports whether an address stands for every interface rather than for one of them.</summary>
    private static bool IsWildcard(IPAddress address) =>
        address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);

    /// <summary>Reports whether a wildcard listener already accepts what another address would be bound for.</summary>
    /// <remarks>
    /// <c>::</c> covers IPv4 as well, because Kestrel binds it as a dual-mode socket; <c>0.0.0.0</c> covers IPv4 alone,
    /// and its overlap with <c>::</c> is reported against the IPv6 wildcard rather than twice.
    /// </remarks>
    private static bool Covers(IPAddress wildcard, IPAddress address) =>
        wildcard.Equals(IPAddress.IPv6Any) || address.AddressFamily == AddressFamily.InterNetwork;
}
