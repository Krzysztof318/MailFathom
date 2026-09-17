// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;

namespace MailFathom.Host.Configuration.Endpoints;

/// <summary>The one reading of a configured list of IP addresses and CIDR networks, wherever such a list is written.</summary>
/// <remarks>
/// <para>
/// Two settings take this shape — the proxies a forwarded value is believed from, and the networks an administrator may
/// act from — and both answer a question about the address a connection or a forwarded hop arrives from. A value
/// carrying <c>/</c> is a network and everything else a single address, and both lists refuse the same spellings for the
/// same reasons, so the rule is written once and each list supplies only the words its own refusal is phrased in.
/// </para>
/// <para>
/// A peer is compared in its IPv4 form where it has one. A dual-stack listener reports an IPv4 client as
/// <c>::ffff:10.0.0.5</c> while an operator writes <c>10.0.0.5</c>, and neither <see cref="IPAddress.Equals(object)" />
/// nor <see cref="IPNetwork.Contains" /> matches across address families — which is also how the forwarded-headers
/// middleware reads the proxy list.
/// </para>
/// </remarks>
internal static class ConfiguredAddressRanges
{
    /// <summary>Reports whether a configured entry names a network rather than one address.</summary>
    /// <param name="entry">The entry, trimmed.</param>
    /// <returns><see langword="true" /> when the entry carries a prefix length.</returns>
    internal static bool NamesNetwork(string entry) => entry.Contains('/', StringComparison.Ordinal);

    /// <summary>Trims an entry the way every reading of one does.</summary>
    /// <param name="entry">The entry as the binder produced it.</param>
    /// <returns>The trimmed entry, empty for a missing one.</returns>
    internal static string Normalize(string? entry) => entry?.Trim() ?? string.Empty;

    /// <summary>Maps validated entries onto the networks they cover, a single address being a network of one.</summary>
    /// <param name="entries">The configured entries.</param>
    /// <returns>The networks, in configuration order.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entries" /> is <see langword="null" />.</exception>
    /// <exception cref="FormatException">Thrown when the entries have not passed <see cref="FindErrors" />.</exception>
    internal static IReadOnlyList<IPNetwork> ToNetworks(IEnumerable<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return [.. entries.Select(Normalize).Select(ToNetwork)];
    }

    /// <summary>Reports whether a peer falls inside any of the given networks.</summary>
    /// <param name="networks">The networks to compare against.</param>
    /// <param name="peer">The address the request arrived from.</param>
    /// <returns><see langword="true" /> when one of the networks holds the peer, read in its IPv4 form where it has one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    internal static bool Contain(IReadOnlyList<IPNetwork> networks, IPAddress peer)
    {
        ArgumentNullException.ThrowIfNull(networks);
        ArgumentNullException.ThrowIfNull(peer);

        var address = InComparableForm(peer);

        return networks.Any(network => network.Contains(address));
    }

    /// <summary>Reads a peer in the form a configured entry is compared against.</summary>
    /// <param name="peer">The address the request arrived from.</param>
    /// <returns>The IPv4 form of an IPv4-mapped address, and the address itself otherwise.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="peer" /> is <see langword="null" />.</exception>
    internal static IPAddress InComparableForm(IPAddress peer)
    {
        ArgumentNullException.ThrowIfNull(peer);

        return peer.IsIPv4MappedToIPv6 ? peer.MapToIPv4() : peer;
    }

    /// <summary>Finds every entry that is not an address or a network, and every network written as a host inside one.</summary>
    /// <param name="entries">The configured entries, in configuration order.</param>
    /// <param name="listPath">The configuration path of the list, which each message names an entry beneath.</param>
    /// <param name="wording">The words this list's refusals are phrased in.</param>
    /// <returns>One message per faulty entry, empty when every entry is usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    internal static IEnumerable<string> FindErrors(
        IEnumerable<string> entries,
        string listPath,
        ConfiguredAddressRangeWording wording)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(listPath);
        ArgumentNullException.ThrowIfNull(wording);

        return entries
            .Index()
            .SelectMany(indexed => FindEntryErrors(Normalize(indexed.Item), $"{listPath}:{indexed.Index}", wording));
    }

    private static IEnumerable<string> FindEntryErrors(string entry, string entryPath, ConfiguredAddressRangeWording wording)
    {
        if (entry.Length == 0)
        {
            return [$"{entryPath} — an empty entry names no {wording.EntryNames}; state an IP address such as '10.0.0.5' or a CIDR network such as '10.0.0.0/24', or remove it."];
        }

        if (NamesNetwork(entry))
        {
            return FindNetworkErrors(entry, entryPath, wording);
        }

        return IPAddress.TryParse(entry, out _)
            ? []
            : [$"{entryPath} — '{entry}' is neither an IP address nor a CIDR network. {wording.WhyNotADnsName}"];
    }

    /// <summary>Refuses a prefix that is not a network, and one that names a host inside a network rather than the network.</summary>
    /// <remarks>
    /// The framework's parser accepts a base address whose host bits are set and silently masks them off, so
    /// <c>10.0.0.5/24</c> would bind as <c>10.0.0.0/24</c>: an operator who meant one address would have written two
    /// hundred and fifty-six without being told. The base address is therefore compared against what the parse
    /// produced, which is the same comparison that catches an IPv6 prefix written one bit too wide.
    /// </remarks>
    private static IEnumerable<string> FindNetworkErrors(string entry, string entryPath, ConfiguredAddressRangeWording wording)
    {
        var configuredBaseAddress = entry[..entry.IndexOf('/', StringComparison.Ordinal)];

        if (!IPNetwork.TryParse(entry, out var network) || !IPAddress.TryParse(configuredBaseAddress, out var baseAddress))
        {
            yield return $"{entryPath} — '{entry}' is not a CIDR network; state a network address and its prefix length, for example '10.0.0.0/24'.";

            yield break;
        }

        if (!network.BaseAddress.Equals(baseAddress))
        {
            yield return $"{entryPath} — '{entry}' names an address inside '{network}' rather than the network itself. Write '{network}' to {wording.Verb} that whole range, or drop the prefix to {wording.Verb} the one address.";
        }
    }

    private static IPNetwork ToNetwork(string entry)
    {
        if (NamesNetwork(entry))
        {
            return IPNetwork.Parse(entry);
        }

        var address = InComparableForm(IPAddress.Parse(entry));

        return new IPNetwork(address, address.GetAddressBytes().Length * 8);
    }
}
