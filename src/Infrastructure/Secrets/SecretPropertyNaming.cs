// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Infrastructure.Secrets;

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
    private static readonly string[] SecretNameFragments = ["Password", "Secret", "Credential", "PrivateKey", "Token"];

    /// <summary>Gets whether a property name announces that the property holds a secret.</summary>
    /// <param name="propertyName">The property name to classify.</param>
    /// <returns><see langword="true" /> when the name contains a secret-announcing fragment; otherwise <see langword="false" />.</returns>
    public static bool NamesASecret(string? propertyName) => propertyName is not null
        && SecretNameFragments.Any(fragment => propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
