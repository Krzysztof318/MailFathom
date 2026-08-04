// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace MailFathom.Host.Configuration.DataEncryption;

/// <summary>Configures the key ring every value MailFathom seals at rest is sealed under.</summary>
/// <remarks>
/// <para>
/// This is a configuration root of its own rather than a section of <c>Persistence</c>. The database is the first thing
/// sealed under the ring and there is no reason it is the last, so naming the ring after its first consumer would leave
/// the second one either moving the section or inheriting a name that says the ring belongs elsewhere. Being a root also
/// makes it a uniqueness scope for secret names, exactly as <c>MailSynchronization</c> and <c>McpEndpoint</c> are.
/// </para>
/// <para>
/// The ring holds several keys so that rotation is not a flag day. Every sealed value stores the identifier of the key
/// that sealed it, a value is re-sealed under <see cref="ActiveKeyId" /> the next time it is written, and a key is
/// retired once nothing references it. A ring that configured one key could only be replaced with the service stopped.
/// </para>
/// <para>
/// Validation here covers what the configuration can be judged on by reading it. Whether the referenced material
/// resolves, and whether it decodes to a key of the right length, is answered where the references are resolved, so a
/// mistyped path and a truncated key are reported together with every other unusable secret rather than one restart
/// apart. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0005-data-encryption-key-ring-and-provisioning.md">ADR 0005</see>.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed partial class DataEncryptionOptions : IValidatableObject
{
    /// <summary>Gets or sets the identifier of the key new values are sealed under.</summary>
    /// <remarks>
    /// Moving it to another configured key is the whole of a rotation's first half: from that moment every value
    /// written is sealed under the new key, while values still carrying the previous identifier keep opening under the
    /// key that sealed them. The previous key therefore stays configured until nothing references it.
    /// </remarks>
    public string ActiveKeyId { get; set; } = string.Empty;

    /// <summary>Gets or sets every key the deployment can open a sealed value with.</summary>
    /// <remarks>
    /// Removing a key that sealed values the database still holds makes those values unopenable, and the failure appears
    /// at the next read rather than at the edit, so a key is removed only once it is known to be unreferenced.
    /// </remarks>
    public IList<DataEncryptionKeyOptions> Keys { get; } = [];

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // An absent section is a deployment that seals nothing, which is a valid deployment: no stored value carries a
        // key identifier yet, so requiring a key here would refuse to start every deployment that has no use for one.
        // The section becomes required by whatever first seals a value, at the point that value is written.
        if (this.Keys.Count == 0)
        {
            if (this.ActiveKeyId.Length > 0)
            {
                yield return new ValidationResult(
                    $"DataEncryption:ActiveKeyId is '{this.ActiveKeyId}', but DataEncryption:Keys configures no key at all. Generate 32 bytes with 'openssl rand -base64 32' and provision them as a secret reference, or remove the active key.",
                    [nameof(this.ActiveKeyId)]);
            }

            yield break;
        }

        foreach (var result in this.Keys.SelectMany((key, position) => FindKeyErrors(key, position)))
        {
            yield return result;
        }

        foreach (var result in this.FindDuplicateKeyIdErrors())
        {
            yield return result;
        }

        foreach (var result in this.FindActiveKeyErrors())
        {
            yield return result;
        }
    }

    private static IEnumerable<ValidationResult> FindKeyErrors(DataEncryptionKeyOptions key, int position)
    {
        if (!AcceptedKeyId().IsMatch(key.KeyId))
        {
            yield return new ValidationResult(
                $"DataEncryption:Keys:{position}:KeyId is '{key.KeyId}', which is not an acceptable key identifier. It may carry up to 64 letters, digits, dots, dashes, and underscores, and must begin with a letter or a digit.",
                [nameof(Keys)]);
        }

        if (key.Material is null)
        {
            yield return new ValidationResult(
                $"DataEncryption:Keys:{position}:Material configures no reference to the key material.",
                [nameof(Keys)]);
        }
    }

    private IEnumerable<ValidationResult> FindDuplicateKeyIdErrors() =>
        this.Keys
            .GroupBy(key => key.KeyId, StringComparer.Ordinal)
            .Where(sameIdentifier => sameIdentifier.Count() > 1)
            .Select(sameIdentifier => new ValidationResult(
                $"DataEncryption:Keys configures the key identifier '{sameIdentifier.Key}' {sameIdentifier.Count()} times. An identifier is what a stored value names its key by, so two keys sharing one would leave which key opens a value undecidable.",
                [nameof(this.Keys)]));

    private IEnumerable<ValidationResult> FindActiveKeyErrors()
    {
        if (this.ActiveKeyId.Length == 0)
        {
            yield return new ValidationResult(
                "DataEncryption:ActiveKeyId names no key. It selects which of the configured keys new values are sealed under, so it is required even where the ring holds exactly one.",
                [nameof(this.ActiveKeyId)]);

            yield break;
        }

        if (!this.Keys.Any(key => string.Equals(key.KeyId, this.ActiveKeyId, StringComparison.Ordinal)))
        {
            yield return new ValidationResult(
                $"DataEncryption:ActiveKeyId is '{this.ActiveKeyId}', which no configured key declares. Configured identifiers are: {string.Join(", ", this.Keys.Select(key => $"'{key.KeyId}'"))}.",
                [nameof(this.ActiveKeyId)]);
        }
    }

    /// <remarks>
    /// The same grammar a secret name takes, and for the same reason: a key identifier is written into log lines, metric
    /// labels, and audit records without escaping, so one that could carry a newline or a quotation mark would let a
    /// configuration file decide how a log line parses. It is additionally persisted beside every value it seals, which
    /// is why the set is narrow rather than merely careful.
    /// </remarks>
    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex AcceptedKeyId();
}
