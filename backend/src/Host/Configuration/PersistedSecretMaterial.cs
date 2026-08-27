// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.References;

namespace MailFathom.Host.Configuration;

/// <summary>Recognizes a setting about to be persisted that carries a secret's material where a reference belongs.</summary>
/// <remarks>
/// <para>
/// A setting announces that it holds a secret through its own name, which is the rule
/// <see cref="SecretPropertyNaming" /> already states for the bound options graph, so nothing here decides a second
/// time what counts as a secret. What it decides is the value: a reference to a scheme this deployment actually
/// resolves names where the material is kept, and anything else — a bare password, the one scheme whose target is the
/// literal itself, or a value that merely happens to carry a colon — is the material.
/// </para>
/// <para>
/// The rule is refused whatever the deployment's secret interpretation says, unlike a value in a file. Under an inline
/// interpretation a configured value <em>is</em> the material and a file carrying one is the operator's own choice
/// about their own file; persisting it is MailFathom putting it into an unsealed column of its own database, which is
/// a choice no write path makes on their behalf.
/// </para>
/// <para>
/// Stated once because both persisted documents are judged by it. A rule that held for the deployment's settings and
/// not for an owner's record would leave a mailbox password written verbatim into the column an owner's declarations
/// live in, which is the one outcome the check exists to prevent.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this rule.")]
internal sealed class PersistedSecretMaterial
{
    /// <summary>The schemes that name where material is kept rather than carrying it.</summary>
    /// <remarks>
    /// Read from what this deployment registered rather than from what the reference syntax admits, because those are
    /// not the same set and the difference is the whole of the check. A scheme is minted for any name before the first
    /// colon, so material carrying one — <c>Pa55:word</c>, a token with a colon in it — parses as a well-formed
    /// reference to a scheme nothing serves. Matching the parse alone would admit it into the column; matching what a
    /// resolver actually answers refuses it, and refuses in the direction that keeps a credential out.
    /// </remarks>
    private readonly HashSet<SecretReferenceScheme> schemesNamingWhereMaterialIsKept;

    /// <summary>Initializes the rule from the secret schemes this deployment resolves.</summary>
    /// <param name="secretSchemeResolvers">The registered resolvers, whose schemes are the ones a reference may name.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="secretSchemeResolvers" /> is <see langword="null" />.</exception>
    public PersistedSecretMaterial(IEnumerable<ISecretSchemeResolver> secretSchemeResolvers)
    {
        ArgumentNullException.ThrowIfNull(secretSchemeResolvers);

        this.schemesNamingWhereMaterialIsKept =
        [
            .. secretSchemeResolvers
                .Select(resolver => resolver.Scheme)
                .Where(scheme => scheme != SecretReferenceScheme.Plaintext),
        ];
    }

    /// <summary>Gets whether a setting at this path would persist a secret's material rather than a reference to it.</summary>
    /// <param name="configurationPath">The colon-delimited path the value is written at.</param>
    /// <param name="value">The value that would be persisted.</param>
    /// <returns><see langword="true" /> when the path names a secret and the value is the material itself.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="configurationPath" /> is <see langword="null" />, empty, or white space.</exception>
    public bool IsCarriedBy(string configurationPath, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);

        return value is not null
            && SecretPropertyNaming.NamesASecret(configurationPath.Split(':')[^1])
            && !this.NamesWhereTheMaterialIsKept(value);
    }

    private bool NamesWhereTheMaterialIsKept(string value) =>
        SecretReference.TryParse(value, out var reference, out _)
        && this.schemesNamingWhereMaterialIsKept.Contains(reference.Scheme);
}
