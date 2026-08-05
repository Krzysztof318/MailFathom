// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Net.Sockets;

namespace MailFathom.Host.Configuration.Endpoints;

/// <summary>The sockets this process opens, composed from every surface that asked for one.</summary>
/// <param name="Listeners">One entry per socket, each naming the surfaces served on it.</param>
/// <param name="Errors">What an operator must fix before these sockets can bind, empty when the composition is usable.</param>
/// <remarks>
/// Composed once, before anything binds, because three questions are really one: which sockets to open, whether the
/// surfaces sharing one agree about it, and which paths a request arriving on a port may ask for. Answering them
/// separately is what would let a deployment bind a socket twice, or serve one surface's posture to another's clients.
/// </remarks>
internal sealed record ComposedListeners(
    IReadOnlyList<ComposedListener> Listeners,
    IReadOnlyList<string> Errors)
{
    /// <summary>Reads which surfaces each bound port serves.</summary>
    /// <returns>One entry per port, carrying the union of the surfaces sharing it.</returns>
    /// <remarks>Keyed by port rather than by socket, because a request carries the port it arrived on and not the address the listener was bound to.</remarks>
    internal IReadOnlyDictionary<int, ServedSurfaces> SurfacesByPort() =>
        this.Listeners
            .GroupBy(static listener => listener.Address.Port)
            .ToDictionary(
                static port => port.Key,
                static port => port.Aggregate(ServedSurfaces.None, static (served, listener) => served | listener.Surfaces));
}

/// <summary>One socket, and everything it serves.</summary>
/// <param name="Address">The address and port bound.</param>
/// <param name="Surfaces">The surfaces served on it.</param>
/// <param name="TerminatesTls">Whether the socket carries TLS.</param>
/// <param name="RedirectsClearText">Whether a clear-text socket answers with the address of the TLS one instead of serving routes.</param>
/// <param name="PresentsProfiles">Whether the TLS identity is selected from HTTPS profiles by server name.</param>
/// <param name="Profiles">The HTTPS profiles bound here, across every surface that contributed one.</param>
/// <param name="RequestsClientCertificates">Whether the handshake asks the client for a certificate.</param>
/// <param name="ContributingSections">The configuration sections that asked for this socket, in composition order.</param>
internal sealed record ComposedListener(
    TransportHttpsListenerAddress Address,
    ServedSurfaces Surfaces,
    bool TerminatesTls,
    bool RedirectsClearText,
    bool PresentsProfiles,
    IReadOnlyList<TransportHttpsEndpointOptions> Profiles,
    bool RequestsClientCertificates,
    IReadOnlyList<string> ContributingSections);

/// <summary>Composes the process's sockets from what each surface asked for, and refuses what cannot be shared.</summary>
/// <remarks>
/// <para>
/// Surfaces may share a socket. That is the posture a single-node deployment behind one ingress wants — the MCP endpoint
/// and the administrative endpoint on one port, or all three — and it is why both request-serving surfaces default to
/// the same ports. What sharing costs is exposure: the probes and the administrative surface are reachable wherever the
/// port they were put on is published, so it stays something an operator selects rather than something arrived at.
/// </para>
/// <para>
/// What sharing may never do is leave two surfaces disagreeing about the socket. A socket is clear text or it is TLS, it
/// redirects or it serves the routes, it asks for a client certificate or it does not, and it presents identities one
/// way. Each disagreement is refused here, naming both sections, because the alternatives are all worse: a second
/// <c>Listen</c> for one socket fails to bind and takes the process down with an address-in-use error, and binding once
/// from whichever section was composed first would serve that section's posture to the other's clients.
/// </para>
/// </remarks>
internal static class ListenerComposition
{
    /// <summary>Composes every socket the enabled surfaces ask for.</summary>
    /// <param name="declarations">What each surface asked for, one entry per socket it wants.</param>
    /// <returns>The composed sockets, and everything an operator must fix first.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declarations" /> is <see langword="null" />.</exception>
    /// <remarks>The listeners are composed whether or not errors were found, so a caller reporting them does not also have to guard against a half-built composition; nothing binds while <see cref="ComposedListeners.Errors" /> is non-empty.</remarks>
    internal static ComposedListeners Compose(IReadOnlyList<DeclaredListener> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        var errors = new List<string>();
        var listeners = new List<ComposedListener>();

        // Grouped by the socket rather than by the port, so two surfaces on one port but different specific addresses
        // stay the two independent sockets the operating system grants. Overlapping addresses on one port are a
        // different fault and are reported below, because there the operating system grants only one.
        var sockets = declarations
            .Where(static declaration => IPAddress.TryParse(declaration.BindAddress?.Trim(), out _))
            .GroupBy(static declaration => new TransportHttpsListenerAddress(
                IPAddress.Parse(declaration.BindAddress.Trim()),
                declaration.Port));

        foreach (var socket in sockets)
        {
            var sharing = socket.ToArray();

            errors.AddRange(FindDisagreements(socket.Key, sharing));

            var first = sharing[0];

            listeners.Add(new ComposedListener(
                socket.Key,
                sharing.Aggregate(ServedSurfaces.None, static (served, declaration) => served | declaration.Surface),
                first.TerminatesTls,
                first.RedirectsClearText,
                first.PresentsProfiles,
                [.. sharing.SelectMany(static declaration => declaration.Profiles)],
                sharing.Any(static declaration => declaration.RequestsClientCertificates),
                [.. sharing.Select(static declaration => declaration.SectionName).Distinct(StringComparer.Ordinal)]));
        }

        errors.AddRange(FindOverlappingSockets(listeners));

        return new ComposedListeners(listeners, errors);
    }

    /// <summary>Refuses a socket whose surfaces disagree about what it is.</summary>
    private static IEnumerable<string> FindDisagreements(
        TransportHttpsListenerAddress address,
        DeclaredListener[] sharing)
    {
        if (sharing.Length < 2)
        {
            yield break;
        }

        var sections = string.Join(" and ", sharing.Select(static declaration => declaration.SectionName).Distinct(StringComparer.Ordinal));
        var socket = $"{address.Address}:{address.Port}";

        if (sharing.Select(static declaration => declaration.TerminatesTls).Distinct().Count() > 1)
        {
            yield return $"{sections} — {sections} share {socket} while disagreeing about whether it carries TLS. One socket serves one scheme, so state the same 'Transport' on both, or give one of them a port of its own.";

            // Everything below describes one scheme or the other, so reporting it as well would describe consequences
            // of the disagreement above rather than second mistakes.
            yield break;
        }

        if (!sharing[0].TerminatesTls)
        {
            if (sharing.Select(static declaration => declaration.RedirectsClearText).Distinct().Count() > 1)
            {
                yield return $"{sections} — {sections} share the clear-text socket {socket} while one redirects to its HTTPS profiles and the other serves its routes there. One socket cannot do both, so state the same 'Https:Redirect:Enabled' on both, or give one of them a port of its own.";
            }

            yield break;
        }

        if (sharing.Select(static declaration => declaration.PresentsProfiles).Distinct().Count() > 1)
        {
            yield return $"{sections} — {sections} share the TLS socket {socket} while one selects its certificate from HTTPS profiles by server name and the other presents one certificate to every connection. A socket answers a handshake one way, so give one of them a port of its own.";

            yield break;
        }

        if (sharing.Select(static declaration => declaration.RequestsClientCertificates).Distinct().Count() > 1)
        {
            yield return $"{sections} — {sections} share the TLS socket {socket} while only one asks the client for a certificate. Whether a certificate is asked for is settled while the connection is established and is therefore one answer for the socket, so give one of them a port of its own.";
        }

        var repeatedDomains = sharing
            .SelectMany(static declaration => declaration.Profiles)
            .Select(static profile => profile.Domain?.Trim() ?? string.Empty)
            .Where(static domain => domain.Length > 0)
            .GroupBy(static domain => domain, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key);

        foreach (var domain in repeatedDomains)
        {
            yield return $"{sections} — '{domain}' is published on {socket} by more than one of {sections}, so which surface a client reaching that name is served by would be decided by composition order rather than by an operator.";
        }

        var declaredProtocolSets = sharing
            .SelectMany(static declaration => declaration.Profiles)
            .Select(static profile => string.Join(',', profile.ServedHttpProtocols.Order().Select(static protocol => protocol.ToString())))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (declaredProtocolSets.Length > 1)
        {
            yield return $"{sections} — the profiles sharing {socket} name different HTTP versions ({string.Join(" and ", declaredProtocolSets)}); ALPN offers what the listener was bound with, which is before any server name has been read.";
        }
    }

    /// <summary>Refuses a port whose sockets the operating system will only grant one of.</summary>
    /// <remarks>
    /// Two specific addresses on one port are two sockets, which is the case sharing a port across interfaces relies on.
    /// A wildcard beside a specific address is not: the wildcard already accepts the connections the second was bound
    /// for, so the second bind fails and takes the process down with an error naming a socket rather than the sections
    /// that asked for it.
    /// </remarks>
    private static IEnumerable<string> FindOverlappingSockets(IReadOnlyList<ComposedListener> listeners)
    {
        foreach (var port in listeners.GroupBy(static listener => listener.Address.Port))
        {
            var addresses = port.Select(static listener => listener.Address.Address).Distinct().ToArray();

            foreach (var wildcard in addresses.Where(IsWildcard))
            {
                var covered = addresses.Where(address => !address.Equals(wildcard) && Covers(wildcard, address)).ToArray();

                if (covered.Length > 0)
                {
                    var sections = string.Join(
                        " and ",
                        port.SelectMany(static listener => listener.ContributingSections).Distinct(StringComparer.Ordinal));

                    yield return $"{sections} — port {port.Key} is bound on {wildcard} as well as on {string.Join(" and ", covered.AsEnumerable())}; {wildcard} already accepts the connections those addresses would receive, so only one of the two listeners could bind. State one address for a port, or move a surface to a port of its own.";
                }
            }
        }
    }

    /// <summary>Reports whether an address stands for every interface rather than for one of them.</summary>
    private static bool IsWildcard(IPAddress address) =>
        address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);

    /// <summary>Reports whether a wildcard listener already accepts what another address would be bound for.</summary>
    private static bool Covers(IPAddress wildcard, IPAddress address) =>
        wildcard.Equals(IPAddress.IPv6Any) || address.AddressFamily == AddressFamily.InterNetwork;
}
