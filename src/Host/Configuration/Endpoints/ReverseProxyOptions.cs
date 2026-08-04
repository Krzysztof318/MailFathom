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
/// Reading them is not a mode to switch on. The middleware is always in the pipeline and the section carries one
/// decision — which peers a forwarded value is believed from — because that is the only question an operator has to
/// answer. A section that names a proxy believes that proxy and nothing else; a section that names none believes every
/// peer that can open a connection, which is the default and is announced at startup rather than assumed to be
/// understood. <see cref="TrustedProxies" /> states what the second posture costs.
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
/// The section is read once, while the host is being composed, because it decides what the pipeline's forwarded-header
/// policy is. A change takes effect on restart.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class ReverseProxyOptions
{
    /// <summary>The configuration section the reverse-proxy settings are bound from.</summary>
    public const string SectionName = "ReverseProxy";

    /// <summary>The trust a section that names no proxy resolves to: every address of both families.</summary>
    /// <remarks>
    /// Held as the configured spelling rather than as parsed networks so that one code path composes trust and one
    /// reports it. The startup warning names these strings back to the operator, which is the same text they would
    /// write to state the posture explicitly.
    /// </remarks>
    private static readonly string[] EveryAddress = ["0.0.0.0/0", "::/0"];

    /// <summary>Gets the proxy addresses or CIDR networks a forwarded scheme and host are accepted from.</summary>
    /// <remarks>
    /// <para>
    /// One list rather than an address list beside a network list, because an operator answering "which proxy do I
    /// trust" writes <c>10.0.0.5</c> or <c>10.0.0.0/24</c> to the same question. A value carrying <c>/</c> is read as a
    /// network and everything else as a single address.
    /// </para>
    /// <para>
    /// Empty is the default and means every peer is believed, which is the posture named by <see cref="EveryAddress" />
    /// written out. It is a real posture — a load balancer pool with no stable address, a network already closed by
    /// something other than this setting — and it is what an unconfigured deployment gets, so what it costs belongs
    /// here rather than in a page somebody may not reach. The refusal of an access token that arrived without transport
    /// encryption decides by reading the scheme this policy has already applied, so with every peer believed any client
    /// that can reach the listener sends <c>X-Forwarded-Proto: https</c> and has a reusable credential accepted over
    /// clear text. <see cref="Hosting.Warnings.ReverseProxyTrustWarning" /> says so at every startup
    /// that runs on it. Naming the addresses your proxies actually use is what turns that refusal back on.
    /// </para>
    /// <para>
    /// The list is deliberately left empty rather than pre-populated with <see cref="EveryAddress" />. The
    /// configuration binder adds to an existing collection instead of replacing it, so a default written here would
    /// survive alongside whatever an operator configured and every deployment would trust every peer no matter what it
    /// wrote.
    /// </para>
    /// </remarks>
    public IList<string> TrustedProxies { get; } = [];

    /// <summary>Gets or sets how many proxies may have appended a value to the forwarded headers.</summary>
    /// <remarks>
    /// One by default, which is the deployment this exists for: a single proxy in front of this process. Each header is
    /// read right to left, so the limit is how far back into a chain a value is believed; raise it only to the number
    /// of proxies a request genuinely passes through, because every entry beyond the real chain is one an earlier hop
    /// could have appended.
    /// </remarks>
    public int MaximumForwardedHops { get; set; } = 1;

    /// <summary>Gets whether the section names a proxy, rather than resolving to trusting every peer.</summary>
    /// <remarks>
    /// This is the operator saying what stands in front of the process, which is why it is what the startup warnings
    /// read: one picks which posture to describe, and the other decides whether a clear-text hop is the one to a proxy
    /// or the one to a client.
    /// </remarks>
    public bool NamesAProxy => this.TrustedProxies.Count > 0;

    /// <summary>Reads the section the way composition does, defaults included.</summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The bound settings.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Strict binding is part of the read, like every other security-sensitive section: a key this section does not
    /// carry would otherwise leave a deployment believing it had named its proxy while every peer was trusted, or
    /// believing a chain limit applied that never bound.
    /// </remarks>
    public static ReverseProxyOptions ReadFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.GetSection(SectionName)
            .Get<ReverseProxyOptions>(binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
            ?? new ReverseProxyOptions();
    }

    /// <summary>Finds everything an operator must fix before a forwarded scheme and host can be read.</summary>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the settings are usable.</returns>
    /// <remarks>
    /// An empty list is not among them. It is the default posture rather than a mistake, so it is announced at startup
    /// instead of refused here; refusing it would make every unconfigured deployment fail to start.
    /// </remarks>
    public IReadOnlyList<string> FindConfigurationErrors()
    {
        var errors = new List<string>(this.FindTrustedProxyErrors());

        if (this.MaximumForwardedHops < 1)
        {
            errors.Add($"{SectionName}:{nameof(this.MaximumForwardedHops)} — '{this.MaximumForwardedHops}' reads no forwarded value at all, so no proxy's scheme or host would reach a request however {nameof(this.TrustedProxies)} is stated. State the number of proxies a request passes through, which is 1 for a single proxy in front of this process.");
        }

        return errors;
    }

    /// <summary>Maps the trusted entries onto the single proxy addresses among them.</summary>
    /// <returns>The addresses, in configuration order, empty when every entry names a network.</returns>
    /// <exception cref="FormatException">Thrown when the settings have not passed <see cref="FindConfigurationErrors" />.</exception>
    public IReadOnlyList<IPAddress> ToTrustedProxyAddresses() =>
        [.. this.EffectiveTrustedProxies().Where(static entry => !NamesNetwork(entry)).Select(IPAddress.Parse)];

    /// <summary>Maps the trusted entries onto the proxy networks among them.</summary>
    /// <returns>The networks, in configuration order, empty when every entry names a single address.</returns>
    /// <exception cref="FormatException">Thrown when the settings have not passed <see cref="FindConfigurationErrors" />.</exception>
    public IReadOnlyList<IPNetwork> ToTrustedProxyNetworks() =>
        [.. this.EffectiveTrustedProxies().Where(NamesNetwork).Select(IPNetwork.Parse)];

    /// <summary>Reports the trusted ranges that cover every address, and so believe any peer that can open a connection.</summary>
    /// <returns>The ranges, in configuration order, empty when every entry names a proxy this deployment could have meant.</returns>
    /// <exception cref="FormatException">Thrown when the settings have not passed <see cref="FindConfigurationErrors" />.</exception>
    /// <remarks>
    /// Reached by a section that named such a range and by one that named nothing, because the two produce the same
    /// trust and give up the same protection. Which of them a deployment is running is <see cref="NamesAProxy" />'s
    /// question, and only the wording of the warning turns on it.
    /// </remarks>
    public IReadOnlyList<IPNetwork> ToTrustedProxyRangesCoveringEveryAddress() =>
        [.. this.ToTrustedProxyNetworks().Where(static network => network.PrefixLength == 0)];

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

    /// <summary>Produces the entries trust is actually composed from, which is every address when none is named.</summary>
    private IEnumerable<string> EffectiveTrustedProxies() =>
        this.NamesAProxy ? this.TrustedProxies.Select(Normalize) : EveryAddress;

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
