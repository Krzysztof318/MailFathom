// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Infrastructure.Secrets;

/// <summary>A parsed <c>&lt;scheme&gt;:&lt;target&gt;</c> secret reference.</summary>
/// <remarks>
/// Parsing answers a grammar question only. Whether an adapter serves the scheme is a dispatch question, which
/// <see cref="CompositeSecretReferenceResolver" /> answers, so an unregistered scheme still parses and is reported as
/// <see cref="SecretResolutionFailure.SchemeNotSupported" /> rather than as a malformed reference.
/// </remarks>
public sealed record SecretReference
{
    private SecretReference(SecretReferenceScheme scheme, string target)
    {
        this.Scheme = scheme;
        this.Target = target;
    }

    /// <summary>Gets the scheme that selects the retrieval adapter.</summary>
    public SecretReferenceScheme Scheme { get; }

    /// <summary>Gets everything after the first colon, byte for byte.</summary>
    /// <remarks>
    /// A path, a Windows drive letter, or a URL therefore survives untouched, and a literal supplied through
    /// <see cref="SecretReferenceScheme.Plaintext" /> keeps its leading and trailing spaces, which are valid password
    /// characters.
    /// </remarks>
    public string Target { get; }

    /// <summary>Parses a configured value as a secret reference.</summary>
    /// <param name="configuredValue">The value bound from configuration.</param>
    /// <param name="parsed">The parsed reference when parsing succeeds; otherwise <see langword="null" />.</param>
    /// <param name="failure">The grammar failure when parsing fails. The value is meaningless when this method returns <see langword="true" />.</param>
    /// <returns><see langword="true" /> when the value is a well-formed reference; otherwise <see langword="false" />.</returns>
    public static bool TryParse(
        string? configuredValue,
        [NotNullWhen(true)] out SecretReference? parsed,
        out SecretResolutionFailure failure)
    {
        parsed = null;
        failure = SecretResolutionFailure.ReferenceMissing;

        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return false;
        }

        var separatorIndex = configuredValue.IndexOf(':', StringComparison.Ordinal);
        var schemeName = separatorIndex < 0 ? string.Empty : configuredValue[..separatorIndex].Trim();
        if (schemeName.Length == 0)
        {
            failure = SecretResolutionFailure.SchemeMissing;

            return false;
        }

        // Only the scheme is trimmed. Trimming the target would silently change a password whose value begins or ends
        // with a space, and would corrupt a path that legitimately carries one.
        var target = configuredValue[(separatorIndex + 1)..];
        if (target.Length == 0)
        {
            failure = SecretResolutionFailure.TargetMissing;

            return false;
        }

        parsed = new SecretReference(SecretReferenceScheme.Create(schemeName), target);

        return true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The synthesized record printing is replaced because <see cref="Target" /> is a file path, a credential name, a
    /// vault identifier, or — under <see cref="SecretReferenceScheme.Plaintext" /> — the complete secret. Any of those
    /// would otherwise reach every log line, exception message, and diagnostic dump this value is included in.
    /// </remarks>
    public override string ToString() => $"{this.Scheme.Name}:***";
}
