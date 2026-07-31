// Copyright © 2026 Krzysztof Kasprowicz

using System.Net;

namespace MailMcp.Host.Configuration;

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
internal sealed class McpHttpsOptions
{
    /// <summary>Gets the HTTPS profiles served, empty when Kestrel terminates no TLS of its own.</summary>
    public IList<McpHttpsEndpointOptions> Endpoints { get; } = [];

    /// <summary>Gets whether any profile is configured, which is what decides between the two postures.</summary>
    internal bool TerminatesTls => this.Endpoints.Count > 0;

    /// <summary>Finds everything an operator must fix before the configured profiles can be served.</summary>
    /// <param name="configurationPath">The configuration path of this section, which prefixes every reported error.</param>
    /// <param name="http3Supported">Whether the host platform can provide the QUIC transport HTTP/3 needs.</param>
    /// <returns>One message per faulty setting, empty when the section is usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configurationPath" /> is <see langword="null" />.</exception>
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

        return errors;
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
}
