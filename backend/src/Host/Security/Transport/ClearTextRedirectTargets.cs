// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Security.Transport;

/// <summary>Answers, for the port a connection arrived on, whether that listener only redirects and where a domain moved to.</summary>
/// <remarks>
/// <para>
/// Composed once, from the surfaces that terminate TLS and have a redirect turned on, so the request path resolves a
/// target by two dictionary lookups and reads no configuration. Both questions are asked of the local port, which is the
/// socket the operating system accepted the connection on and therefore something a caller cannot state or forward — the
/// same property the endpoint isolation middlewares decide on.
/// </para>
/// </remarks>
internal sealed class ClearTextRedirectTargets
{
    private readonly Dictionary<int, IReadOnlyDictionary<string, int>> publishedPortsByListenerPort;

    /// <summary>Initializes the targets from the listeners the process opens.</summary>
    /// <param name="listeners">One entry per clear-text listener, each naming the domains it redirects to.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="listeners" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when two listeners claim one port, which the endpoint sections refuse before composition reaches this.</exception>
    internal ClearTextRedirectTargets(IEnumerable<ClearTextRedirectListener> listeners)
    {
        ArgumentNullException.ThrowIfNull(listeners);

        this.publishedPortsByListenerPort = listeners.ToDictionary(
            static listener => listener.Port,
            static listener => listener.PublishedDomainPorts);
    }

    /// <summary>Reports whether a listener exists only to redirect, and therefore serves no route at all.</summary>
    /// <param name="localPort">The TCP port the connection was accepted on.</param>
    /// <returns><see langword="true" /> when the listener redirects and nothing else, otherwise <see langword="false" />.</returns>
    internal bool RedirectsOnly(int localPort) => this.publishedPortsByListenerPort.ContainsKey(localPort);

    /// <summary>Reads the HTTPS port a domain is published on, for the surface whose clear-text listener a request reached.</summary>
    /// <param name="localPort">The TCP port the connection was accepted on.</param>
    /// <param name="domain">The host name the request asked for, without its port.</param>
    /// <returns>The HTTPS port, or <see langword="null" /> when this listener's surface publishes no such domain.</returns>
    /// <remarks>
    /// A domain the surface does not publish resolves to nothing rather than to a default. The name came from the client,
    /// and answering it with a redirect to some other configured domain would send a request to a deployment identity
    /// nobody asked for; the caller is told its host is not served instead.
    /// </remarks>
    internal int? PublishedHttpsPortFor(int localPort, string domain)
    {
        ArgumentNullException.ThrowIfNull(domain);

        return this.publishedPortsByListenerPort.TryGetValue(localPort, out var publishedPorts)
            && publishedPorts.TryGetValue(domain, out var httpsPort)
                ? httpsPort
                : null;
    }
}
