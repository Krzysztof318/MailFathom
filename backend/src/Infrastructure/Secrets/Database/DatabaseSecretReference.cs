// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Secrets.References;

namespace MailFathom.Infrastructure.Secrets.Database;

/// <summary>Identifies one sealed secret stored in MailFathom's database.</summary>
/// <remarks>
/// The configuration form is <c>database:&lt;uuid&gt;</c>. The identifier is a random version 4 UUID rather than a
/// time-ordered one because the reference reaches documents and administrative responses, where creation time is not
/// part of its contract. The target is redacted like every other secret reference even though it is not material.
/// </remarks>
public readonly record struct DatabaseSecretReference
{
    private DatabaseSecretReference(Guid id) => this.Id = id;

    /// <summary>Gets the database secret scheme used on the configuration wire.</summary>
    public static SecretReferenceScheme Scheme { get; } = SecretReferenceScheme.Create("database");

    /// <summary>Gets whether this value identifies a stored secret rather than the unusable struct default.</summary>
    public bool IsSpecified => this.Id != Guid.Empty;

    /// <summary>Gets the complete value a <see cref="Discovery.ConfiguredSecret" /> carries.</summary>
    /// <exception cref="InvalidOperationException">Thrown when this value is the unspecified struct default.</exception>
    public string ConfigurationValue => this.IsSpecified
        ? $"{Scheme.Name}:{this.Id:D}"
        : throw new InvalidOperationException("The unspecified database secret reference has no configuration value.");

    /// <summary>Creates a reference for a newly stored secret.</summary>
    /// <returns>A reference carrying a cryptographically random version 4 UUID.</returns>
    public static DatabaseSecretReference Create() => new(Guid.NewGuid());

    /// <summary>Creates a reference from a persisted identifier.</summary>
    /// <param name="id">The stored secret identifier.</param>
    /// <returns>The reference.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="id" /> is empty.</exception>
    public static DatabaseSecretReference Create(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A stored secret identifier is never empty.", nameof(id));
        }

        return new DatabaseSecretReference(id);
    }

    /// <summary>Parses a complete configured reference.</summary>
    /// <param name="configuredValue">The configured value.</param>
    /// <param name="reference">The parsed reference when successful; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the value names one database secret.</returns>
    public static bool TryParse(string? configuredValue, out DatabaseSecretReference reference)
    {
        reference = default;

        return SecretReference.TryParse(configuredValue, out var parsed, out _)
            && TryCreate(parsed, out reference);
    }

    /// <inheritdoc />
    public override string ToString() => this.IsSpecified ? $"{Scheme.Name}:***" : "(unspecified)";

    internal Guid Id { get; }

    internal static bool TryCreate(SecretReference parsed, out DatabaseSecretReference reference)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        reference = default;
        if (parsed.Scheme != Scheme
            || !Guid.TryParseExact(parsed.Target, "D", out var id)
            || id == Guid.Empty)
        {
            return false;
        }

        reference = new DatabaseSecretReference(id);

        return true;
    }
}
