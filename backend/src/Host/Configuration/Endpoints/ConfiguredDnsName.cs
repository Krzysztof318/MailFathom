// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Endpoints;

/// <summary>Decides whether a configured name could be the DNS name a TLS listener claims.</summary>
/// <remarks>
/// <para>
/// Every listener MailFathom terminates TLS on states a name, and every one of them is matched the same way: against
/// the certificate's DNS subject alternative names, which carry neither an IP address nor a catch-all. What a name may
/// be is therefore a property of that matching rather than of the endpoint that configured it, which is why the rules
/// live here instead of once per section.
/// </para>
/// <para>
/// Emptiness is deliberately not reported here. A missing name means something different to each caller — an MCP
/// profile that publishes nothing could select no certificate, while a probe listener has nothing to prove its
/// material against — so each section says so in its own words.
/// </para>
/// </remarks>
internal static class ConfiguredDnsName
{
    /// <summary>Finds the reasons a configured name is not a DNS name.</summary>
    /// <param name="configuredName">The name as an operator wrote it, or <see langword="null" /> when none is configured.</param>
    /// <param name="settingPath">The configuration path of the setting, which prefixes every reported error.</param>
    /// <returns>One message per reason, empty when the name is a DNS name or when none is configured.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settingPath" /> is <see langword="null" />.</exception>
    internal static IEnumerable<string> FindErrors(string? configuredName, string settingPath)
    {
        ArgumentNullException.ThrowIfNull(settingPath);

        var name = configuredName?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            yield break;
        }

        if (!name.All(char.IsAscii))
        {
            yield return $"{settingPath} — state an internationalized domain in its punycode A-label form, because that is what a client sends and what a certificate's subject alternative names carry.";

            yield break;
        }

        var hostNameKind = Uri.CheckHostName(name);

        if (hostNameKind is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            yield return $"{settingPath} — an IP address cannot be the name a listener claims: a certificate is matched against its DNS subject alternative names, which never carry one, and a client sends no server name for an address. State the DNS name and bind the address through 'BindAddress'.";

            yield break;
        }

        if (hostNameKind != UriHostNameType.Dns)
        {
            yield return $"{settingPath} — '{name}' is not a DNS name. Wildcard and catch-all names are deliberately not accepted; state the exact name this listener claims.";
        }
    }
}
