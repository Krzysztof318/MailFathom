// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace MailFathom.Host.Configuration.Endpoints;

/// <summary>Configures which reverse proxy this process accepts a public scheme and host from.</summary>
/// <remarks>
/// <para>
/// Behind a proxy that terminates TLS, the request arrives as <c>http</c> under whichever internal name the proxy
/// dialled, and the deployment's public identity reaches this process only as <c>X-Forwarded-Proto</c> and
/// <c>X-Forwarded-Host</c>. Applying those two makes the request true: OAuth discovery, the authentication challenge,
/// and every address composed from a request agree with the name a client actually used.
/// </para>
/// <para>
/// It changes nothing about who declares the deployment's identity. A resource identifier stays a configured value
/// compared against a token's audience, never a header, so nothing an upstream writes can decide what a client is told
/// to authorize for. What a forwarded header settles is which name this request arrived under, which is a fact about
/// the hop rather than a claim about the deployment.
/// </para>
/// <para>
/// One section for the whole process rather than one per surface. The MCP, administrative, and probe surfaces are
/// separate listeners over one request pipeline, and a forwarded header is applied by middleware in that pipeline
/// before any surface's routing runs — so a per-surface setting would carry three copies of one network fact, which
/// proxy this deployment sits behind, and leave an operator to keep them in step. Trust here is a property of what
/// stands in front of the process, so it is stated once and holds on every listener the named proxy can reach.
/// </para>
/// <para>
/// The section is read once, while the host is being composed, because it decides which middleware the pipeline
/// carries. A change takes effect on restart.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class ReverseProxyOptions
{
    /// <summary>The configuration section the reverse-proxy settings are bound from.</summary>
    public const string SectionName = "ReverseProxy";

    /// <summary>Gets or sets whether a forwarded scheme and host are read at all.</summary>
    /// <remarks>
    /// Off unless a deployment states otherwise, so a process nobody put a proxy in front of ignores both headers
    /// entirely. A forwarded header is a value whoever is upstream wrote, and a deployment reachable directly has no
    /// upstream worth believing.
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>Gets the proxy addresses or CIDR networks a forwarded scheme and host are accepted from.</summary>
    /// <remarks>
    /// <para>
    /// One list rather than an address list beside a network list, because an operator answering "which proxy do I
    /// trust" writes <c>10.0.0.5</c> or <c>10.0.0.0/24</c> to the same question. A value carrying <c>/</c> is read as a
    /// network and everything else as a single address.
    /// </para>
    /// <para>
    /// Empty is not a permitted posture for an enabled section. The framework's own default trusts loopback, which is
    /// wrong inside a container where the proxy is a peer on the pod or bridge network, and the usual workaround of
    /// clearing its lists trusts every peer that can open a connection — which is worse than the problem it solves.
    /// </para>
    /// </remarks>
    public IList<string> TrustedProxies { get; } = [];

    /// <summary>Gets or sets how many proxies may have appended a value to the forwarded headers.</summary>
    /// <remarks>
    /// One by default, which is the deployment this mode exists for: a single proxy in front of this process. Each
    /// header is read right to left, so the limit is how far back into a chain a value is believed; raise it only to
    /// the number of proxies a request genuinely passes through, because every entry beyond the real chain is one an
    /// earlier hop could have appended.
    /// </remarks>
    public int MaximumForwardedHops { get; set; } = 1;

    /// <summary>Reads the section the way composition does, defaults included.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The bound settings.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <remarks>Strict binding is part of the read, like every other security-sensitive section: a misspelled key here would leave a deployment believing it had named its proxy while nothing was trusted, or believing a chain limit applied that never bound.</remarks>
    public static ReverseProxyOptions ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.GetSection(SectionName)
            .Get<ReverseProxyOptions>(binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            ?? new ReverseProxyOptions();
    }

    /// <summary>Finds everything an operator must fix before a forwarded scheme and host can be read.</summary>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the settings are usable.</returns>
    public IReadOnlyList<string> FindConfigurationErrors()
    {
        if (!this.Enabled)
        {
            // Configured-but-unread is refused rather than ignored, for the reason every other section refuses it: an
            // operator who named their proxy and left the mode off has a deployment they believe reads a forwarded
            // header and one that does not.
            return this.TrustedProxies.Count == 0
                ? []
                : [$"{SectionName}:{nameof(this.TrustedProxies)} — proxies are configured while '{nameof(this.Enabled)}' is false, so no forwarded header is read from any of them; enable the section or remove them."];
        }

        var errors = new List<string>();

        if (this.TrustedProxies.Count == 0)
        {
            errors.Add($"{SectionName}:{nameof(this.TrustedProxies)} — a forwarded scheme and host are worth what the connection that carried them is worth, so an enabled section must name the proxy it accepts them from. State one or more IP addresses or CIDR networks, for example '10.0.0.5' or '10.0.0.0/24'.");
        }

        errors.AddRange(this.FindTrustedProxyErrors());

        if (this.MaximumForwardedHops < 1)
        {
            errors.Add($"{SectionName}:{nameof(this.MaximumForwardedHops)} — '{this.MaximumForwardedHops}' reads no forwarded value at all, which is what disabling the section already does. State the number of proxies a request passes through, which is 1 for a single proxy in front of this process.");
        }

        return errors;
    }

    /// <summary>Maps the configured entries onto the single proxy addresses among them.</summary>
    /// <returns>The addresses, in configuration order, empty when every entry names a network.</returns>
    /// <exception cref="FormatException">Thrown when the settings have not passed <see cref="FindConfigurationErrors" />.</exception>
    public IReadOnlyList<IPAddress> ToTrustedProxyAddresses() =>
        [.. this.TrustedProxies.Select(Normalize).Where(static entry => !NamesNetwork(entry)).Select(IPAddress.Parse)];

    /// <summary>Maps the configured entries onto the proxy networks among them.</summary>
    /// <returns>The networks, in configuration order, empty when every entry names a single address.</returns>
    /// <exception cref="FormatException">Thrown when the settings have not passed <see cref="FindConfigurationErrors" />.</exception>
    public IReadOnlyList<IPNetwork> ToTrustedProxyNetworks() =>
        [.. this.TrustedProxies.Select(Normalize).Where(NamesNetwork).Select(IPNetwork.Parse)];

    private static bool NamesNetwork(string entry) => entry.Contains('/', StringComparison.Ordinal);

    /// <summary>Refuses a prefix that is not a network, and one that names a host inside a network rather than the network.</summary>
    /// <remarks>
    /// The framework's parser accepts a base address whose host bits are set and silently masks them off, so
    /// <c>10.0.0.5/24</c> would bind as <c>10.0.0.0/24</c>: an operator who meant to trust one proxy would have trusted
    /// two hundred and fifty-six addresses without being told. The base address is therefore compared against what the
    /// parse produced, which is the same comparison that catches an IPv6 prefix written one bit too wide.
    /// </remarks>
    private static IEnumerable<string> FindNetworkErrors(string entry, string entryPath)
    {
        var configuredBaseAddress = entry[..entry.IndexOf('/', StringComparison.Ordinal)];

        if (!IPNetwork.TryParse(entry, out var network) || !IPAddress.TryParse(configuredBaseAddress, out var baseAddress))
        {
            yield return $"{entryPath} — '{entry}' is not a CIDR network; state a network address and its prefix length, for example '10.0.0.0/24'.";

            yield break;
        }

        if (!network.BaseAddress.Equals(baseAddress))
        {
            yield return $"{entryPath} — '{entry}' names an address inside '{network}' rather than the network itself. Write '{network}' to trust that whole range, or drop the prefix to trust the one address.";
        }
    }

    private static string Normalize(string? entry) => entry?.Trim() ?? string.Empty;

    private IEnumerable<string> FindTrustedProxyErrors()
    {
        foreach (var (index, configuredEntry) in this.TrustedProxies.Index())
        {
            var entryPath = $"{SectionName}:{nameof(this.TrustedProxies)}:{index}";
            var entry = Normalize(configuredEntry);

            if (entry.Length == 0)
            {
                yield return $"{entryPath} — an empty entry names no proxy; state an IP address such as '10.0.0.5' or a CIDR network such as '10.0.0.0/24', or remove it.";

                continue;
            }

            if (NamesNetwork(entry))
            {
                foreach (var error in FindNetworkErrors(entry, entryPath))
                {
                    yield return error;
                }

                continue;
            }

            if (!IPAddress.TryParse(entry, out _))
            {
                yield return $"{entryPath} — '{entry}' is neither an IP address nor a CIDR network. A proxy is trusted by the address its connection arrives from, so a DNS name cannot stand in for one.";
            }
        }
    }
}
