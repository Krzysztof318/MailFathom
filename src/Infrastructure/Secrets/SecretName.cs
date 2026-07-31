// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;

namespace MailMcp.Infrastructure.Secrets;

/// <summary>The operator-chosen identity of one configured secret, safe to record wherever the material is not.</summary>
/// <remarks>
/// <para>
/// A secret needs an identity that survives everything about it changing. Its position in a configuration array does
/// not: inserting an entry renumbers every one after it, so a log line naming position 2 describes a different
/// credential after the next edit. Its material does not either, and naming a secret by its value is what the rest of
/// this machinery exists to prevent. The name is therefore the one thing a rotation, an expiry, a diagnostic, and an
/// audit record can all agree on.
/// </para>
/// <para>
/// The accepted characters are narrow so the name is safe to put in a log, a metric label, a header challenge, or an
/// audit record without escaping or truncation deciding what it means. That also keeps a name from carrying the
/// material an operator was tempted to describe it with.
/// </para>
/// </remarks>
public readonly partial record struct SecretName
{
    /// <summary>The greatest number of characters a name may carry.</summary>
    public const int MaximumLength = 64;

    private SecretName(string value) => this.Value = value;

    /// <summary>Gets the name, or <see langword="null" /> for the struct default, which names nothing.</summary>
    public string? Value { get; }

    /// <summary>Gets whether this value names a secret rather than being the unusable struct default.</summary>
    public bool IsSpecified => this.Value is not null;

    /// <summary>Reads a configured name.</summary>
    /// <param name="configuredValue">The bound value.</param>
    /// <param name="name">The validated name when the value is accepted; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the value is an accepted name; otherwise <see langword="false" />.</returns>
    /// <remarks>Surrounding whitespace is not trimmed away: a name that needs trimming is two spellings of one identity, and only one of them would match a comparison made elsewhere.</remarks>
    public static bool TryCreate(string? configuredValue, out SecretName name)
    {
        if (configuredValue is null || configuredValue.Length > MaximumLength || !AcceptedName().IsMatch(configuredValue))
        {
            name = default;

            return false;
        }

        name = new SecretName(configuredValue);

        return true;
    }

    /// <summary>Returns the name, which carries no secret material.</summary>
    /// <returns>The name, or a marker when the value is the struct default.</returns>
    public override string ToString() => this.Value ?? "(unnamed)";

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex AcceptedName();
}
