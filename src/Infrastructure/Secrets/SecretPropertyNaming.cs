// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Secrets;

/// <summary>Recognizes a configuration property name that announces a secret.</summary>
/// <remarks>
/// The rule steers an author towards <see cref="ConfiguredSecret" />: a settings property that names a secret and binds
/// to a raw <see cref="string" /> is rejected, at build time by the boundary architecture test and at startup by
/// <see cref="ConfiguredSecretDiscovery" />. It is deliberately name-based and therefore cannot catch a secret called
/// <c>Value</c>; it exists to make the ordinary mistake — adding <c>public string Password { get; set; }</c> to an
/// options type — fail rather than ship.
/// </remarks>
public static class SecretPropertyNaming
{
    private static readonly string[] SecretNameFragments = ["Password", "Secret", "Credential", "PrivateKey", "Token", "ApiKey"];

    /// <summary>Suffixes that make a name an address rather than a credential.</summary>
    /// <remarks>
    /// A property whose name ends in one of these locates something; it does not hold it. The case that forces the
    /// distinction is OAuth's <c>TokenEndpoint</c>, which is the published name for the address a grant is exchanged
    /// at and which no rewording avoids — every accurate name for it contains "token". Treating it as a secret would
    /// push a public URL into <see cref="ConfiguredSecret" />, where an operator would have to provision an address as
    /// though it were a credential.
    /// </remarks>
    private static readonly string[] AddressNameSuffixes = ["Endpoint", "Uri", "Url", "Address"];

    /// <summary>Gets whether a property name announces that the property holds a secret.</summary>
    /// <param name="propertyName">The property name to classify.</param>
    /// <returns><see langword="true" /> when the name contains a secret-announcing fragment and does not name an address; otherwise <see langword="false" />.</returns>
    public static bool NamesASecret(string? propertyName) => propertyName is not null
        && SecretNameFragments.Any(fragment => propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase))
        && !NamesAnAddress(propertyName);

    /// <summary>Gets whether a property name locates something rather than holding it.</summary>
    private static bool NamesAnAddress(string propertyName) =>
        AddressNameSuffixes.Any(suffix => propertyName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
}
