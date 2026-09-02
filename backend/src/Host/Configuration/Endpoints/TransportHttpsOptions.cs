// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Net.Sockets;

namespace MailFathom.Host.Configuration.Endpoints;

/// <summary>Configures the identities Kestrel terminates TLS under for a request-serving surface.</summary>
/// <remarks>
/// <para>
/// These profiles are the TLS half of <see cref="EndpointTransport" />. Under <see cref="EndpointTransport.Http" />
/// there are none, which is the posture local development runs and the one a deployment behind a TLS-terminating
/// reverse proxy runs, where a second TLS layer inside the trust boundary buys nothing. Startup warns about it, because
/// a clear-text endpoint reachable from anywhere is a different thing from one reachable only from the machine or the
/// proxy in front of it, and only an operator knows which they have.
/// </para>
/// <para>
/// Under <see cref="EndpointTransport.HttpsOnly" /> Kestrel binds exactly the profiles named here and opens no
/// clear-text socket at all, so nothing stays behind them serving the same routes without the protection they were
/// configured to add. <see cref="EndpointTransport.HttpAndHttps" /> is the deliberate exception rather than a mixed
/// state arrived at by accident: the surface's clear-text socket stays open, and <see cref="Redirect" /> decides
/// whether it points clients at these profiles or serves the routes itself.
/// </para>
/// </remarks>
internal sealed class TransportHttpsOptions
{
    /// <summary>Gets the HTTPS profiles served, empty when the surface terminates no TLS.</summary>
    public IList<TransportHttpsEndpointOptions> Endpoints { get; } = [];

    /// <summary>Gets or sets what the surface's clear-text socket does while these profiles are served.</summary>
    public TransportClearTextRedirectOptions Redirect { get; set; } = new();

    /// <summary>Reads the HTTPS port each configured domain is published on, which is what a redirect resolves against.</summary>
    /// <returns>One entry per profile, keyed by the domain it publishes, matched without regard to case the way a host name is.</returns>
    /// <remarks>Every profile carries a domain and no two share one, both of which validation has already proven by the time a composed redirect reads this.</remarks>
    internal IReadOnlyDictionary<string, int> PublishedDomainPorts() =>
        this.Endpoints.ToDictionary(
            static endpoint => endpoint.Domain.Trim(),
            static endpoint => endpoint.Port,
            StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads the ports the configured profiles bind.</summary>
    internal IEnumerable<int> ListenerPorts() => this.Endpoints.Select(static endpoint => endpoint.Port);

    /// <summary>Finds everything an operator must fix before the configured profiles can be served.</summary>
    /// <param name="configurationPath">The configuration path of this section, which prefixes every reported error.</param>
    /// <param name="http3Supported">Whether the host platform can provide the QUIC transport HTTP/3 needs.</param>
    /// <returns>One message per faulty setting, empty when the section is usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurationPath" /> is <see langword="null" />.</exception>
    /// <remarks>Whether the section belongs on this surface at all is the surface's question, because only it knows which <see cref="EndpointTransport" /> was selected.</remarks>
    internal IReadOnlyList<string> FindConfigurationErrors(string configurationPath, bool http3Supported)
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

        return errors;
    }

    /// <summary>Finds the profiles whose socket the surface's own clear-text listener already binds.</summary>
    /// <param name="configurationPath">The configuration path of this section, which prefixes every reported error.</param>
    /// <param name="clearTextAddress">The address the surface's clear-text socket binds.</param>
    /// <param name="clearTextPort">The port the surface's clear-text socket binds.</param>
    /// <returns>One message per colliding profile, empty when nothing collides.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurationPath" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The within-surface half of the collision rule, and it is the address-aware comparison
    /// <see cref="FindOverlappingListeners" /> already applies between two profiles rather than a comparison of port
    /// numbers. A port number alone would refuse a multi-interface deployment that binds a profile to one address and
    /// the clear-text socket to another, which the operating system grants as two independent sockets.
    /// </remarks>
    internal IEnumerable<string> FindClearTextCollisions(
        string configurationPath,
        string? clearTextAddress,
        int clearTextPort)
    {
        ArgumentNullException.ThrowIfNull(configurationPath);

        // An address the parser does not recognize was reported by the surface that owns it, and there is no socket to
        // compare against a profile's until an operator fixes it. Reporting a collision as well would describe a second
        // mistake nobody made.
        if (!IPAddress.TryParse(clearTextAddress?.Trim(), out var address))
        {
            yield break;
        }

        var collidingProfiles = this.Endpoints
            .Where(endpoint => endpoint.Port == clearTextPort)
            .Where(endpoint => IPAddress.TryParse(endpoint.BindAddress?.Trim(), out var profileAddress)
                && Overlaps(address, profileAddress))
            .Select(static endpoint => endpoint.Name)
            .ToArray();

        if (collidingProfiles.Length > 0)
        {
            yield return $"{configurationPath}:{nameof(this.Endpoints)} — the clear-text listener binds {address}:{clearTextPort}, which the HTTPS profile {string.Join(" and ", collidingProfiles)} in this section already binds, and one socket cannot serve both schemes. State a port or an address no profile uses.";
        }
    }

    /// <summary>Reports whether two listener addresses on one port would contend for the same socket.</summary>
    /// <remarks>
    /// Either one being a wildcard is enough, and the direction matters: the wildcard is the address that accepts the
    /// connections the other would be bound for, so each is asked whether it covers the other rather than only the first.
    /// Two specific addresses are two sockets the operating system grants independently, which is the case this exists to
    /// let through.
    /// </remarks>
    private static bool Overlaps(IPAddress left, IPAddress right) =>
        left.Equals(right)
        || (IsWildcard(left) && Covers(left, right))
        || (IsWildcard(right) && Covers(right, left));

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
