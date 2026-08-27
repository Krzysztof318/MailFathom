// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace MailFathom.Domain.Access;

/// <summary>The name one owner credential is looked up by, in the single form the deployment stores and compares.</summary>
/// <remarks>
/// <para>
/// A username is not an identity and names no person: it is the handle a caller writes into an HTTP Basic credential,
/// and what it resolves to is one credential row belonging to one <see cref="MailOwnerId" />. Two credentials may
/// belong to one owner and be rotated apart; no two may carry one username, which is what makes the lookup a single
/// indexed read rather than a scan that could match twice.
/// </para>
/// <para>
/// Canonical form is the whole reason this is a type. A username arrives from a person typing it into a client, so it
/// arrives with the capitalization and the surrounding space that a person's keyboard produced, while the column it is
/// matched against holds exactly one spelling. Folding is done here — trimmed, then lowercased under the invariant
/// culture — so that the form stored at provisioning, the form the unique index enforces, and the form a request is
/// resolved by are one form decided in one place. Culture-sensitive lowercasing is deliberately not used: a deployment
/// whose process culture changed would otherwise fold two different sets of names and stop resolving credentials it had
/// already issued.
/// </para>
/// <para>
/// The accepted characters are the conservative set an operator can type, quote, and read back out of a log without
/// escaping: letters, digits, and <c>.</c>, <c>-</c>, <c>_</c>, <c>+</c>, and <c>@</c>. A colon is refused outright and
/// is the one refusal that is about the transport rather than about tidiness — RFC 7617 separates the username from the
/// password with the first colon, so a username carrying one could never be presented whole and would silently
/// authenticate as a shorter name.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and is not a username. It reports itself through
/// <see cref="IsSpecified" /> and refuses to answer for a value, and the only ways to obtain one are the two factories
/// below.
/// </para>
/// </remarks>
public readonly record struct OwnerCredentialUsername
{
    /// <summary>The longest username this deployment accepts, in characters of its canonical form.</summary>
    /// <remarks>
    /// Long enough for an address-shaped name and short enough that the column it is stored in is bounded, which is
    /// what keeps an administrative surface from being handed a page of text where a handle was meant. It bounds the
    /// credential a request presents as well, so a header naming something longer is refused before any hash is
    /// computed.
    /// </remarks>
    public const int MaximumLength = 128;

    private readonly string? value;

    private OwnerCredentialUsername(string value) => this.value = value;

    /// <summary>Gets whether this value names a username rather than the unusable struct default.</summary>
    public bool IsSpecified => this.value is not null;

    /// <summary>Gets the canonical form, which is what is stored, indexed, and compared.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a username.</exception>
    public string Value => this.value
        ?? throw new InvalidOperationException("The value is the default of the struct and names no credential username.");

    /// <summary>Reads a written username into its canonical form.</summary>
    /// <param name="written">The name as a person or an operator typed it, or <see langword="null" /> when none was supplied.</param>
    /// <param name="username">The canonical username when the written form is usable; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the written form is a username this deployment accepts.</returns>
    /// <remarks>
    /// It answers rather than raising because both of its callers meet an unusable value as an ordinary event: a
    /// request presenting a malformed credential, which is refused like every other refused credential, and an operator
    /// provisioning one, which is answered with what to write instead.
    /// </remarks>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "The canonical form is lowercase because it is what an operator types and what the unique index holds; it is never used for a security decision on its own.")]
    public static bool TryCreate(string? written, out OwnerCredentialUsername username)
    {
        username = default;

        if (written is null)
        {
            return false;
        }

        var canonical = written.Trim().ToLowerInvariant();

        if (canonical.Length == 0 || canonical.Length > MaximumLength || !IsWritable(canonical))
        {
            return false;
        }

        username = new OwnerCredentialUsername(canonical);

        return true;
    }

    /// <summary>Reads a written username into its canonical form, refusing anything this deployment does not accept.</summary>
    /// <param name="written">The name as a person or an operator typed it.</param>
    /// <returns>The canonical username.</returns>
    /// <exception cref="ArgumentException">Thrown when the written form is not a username this deployment accepts.</exception>
    /// <remarks>For a caller that has already established the value is usable — a row read back out of the table it was written into, above all — where an answer would only be discarded.</remarks>
    public static OwnerCredentialUsername Create(string written) =>
        TryCreate(written, out var username)
            ? username
            : throw new ArgumentException(
                $"A credential username is 1 to {MaximumLength} letters, digits, or the characters '.', '-', '_', '+', and '@'.",
                nameof(written));

    /// <summary>Describes what a written username may contain, for a refusal an operator reads.</summary>
    /// <returns>The sentence naming the accepted form.</returns>
    /// <remarks>Written here rather than by each surface that refuses one, so an operator provisioning a credential and a validator refusing theirs describe one rule.</remarks>
    public static string DescribeAcceptedForm() => string.Format(
        CultureInfo.InvariantCulture,
        "A username is 1 to {0} characters of letters, digits, or '.', '-', '_', '+', and '@', and is compared "
        + "lowercased with surrounding space removed.",
        MaximumLength);

    /// <inheritdoc />
    public override string ToString() => this.value ?? "(unspecified)";

    private static bool IsWritable(string canonical) =>
        canonical.All(static character =>
            char.IsAsciiLetterLower(character)
            || char.IsAsciiDigit(character)
            || character is '.' or '-' or '_' or '+' or '@');
}
