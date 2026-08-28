// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Domain.Access;

/// <summary>The one value a presented credential is resolved to its owner by, in the form the deployment indexes.</summary>
/// <remarks>
/// <para>
/// Every owner-facing method resolves an owner by exactly one indexed value, and this is that value whichever method
/// produced it: a canonical username, the digest of a key this deployment minted, the fingerprint of a client's public
/// key, or an authorization server's issuer and subject together. Modelling them as one type is what lets one table,
/// one index, and one store contract answer all four — and what keeps the composition of the two-part OAuth value in
/// one place rather than in the authenticator that reads it and the administration that writes it.
/// </para>
/// <para>
/// What the value <em>says</em> differs by method and is deliberately not hidden. Two of the four are readable —
/// a username and a subject are written by whoever provisioned them — and two are digests, which is what a credential
/// whose material may never be stored resolves by. So a listing renders this verbatim: for a key it is a fingerprint
/// an operator can compare against what a client holds, and for a subject it is the mapping itself.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and is not a lookup. It reports itself through
/// <see cref="IsSpecified" />, refuses to answer for a value, and is rejected by every store operation that names one.
/// </para>
/// </remarks>
public readonly record struct OwnerCredentialLookup
{
    /// <summary>The longest lookup value the deployment stores, in characters.</summary>
    /// <remarks>
    /// Set by the longest of the four rather than by the shortest: a username is bounded at
    /// <see cref="OwnerCredentialUsername.MaximumLength" /> and a digest is 43 characters, while an issuer and a
    /// subject are two values an authorization server chose and are the only pair that can approach this. It is bounded
    /// at all because an unbounded index key is one an administrative surface could be persuaded to store a page into.
    /// </remarks>
    public const int MaximumLength = 512;

    /// <summary>What separates the issuer from the subject in the value an OAuth credential is resolved by.</summary>
    /// <remarks>A space, because an issuer is an absolute URI and cannot contain one, so the first space is unambiguously the separator and the value stays something an operator can read out of a listing.</remarks>
    private const char OAuthSubjectSeparator = ' ';

    private readonly string? value;

    private OwnerCredentialLookup(string value) => this.value = value;

    /// <summary>Gets whether this value names a lookup rather than the unusable struct default.</summary>
    public bool IsSpecified => this.value is not null;

    /// <summary>Gets the stored form, which is what the unique index holds and a request is resolved by.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a lookup.</exception>
    public string Value => this.value
        ?? throw new InvalidOperationException("The value is the default of the struct and resolves no credential.");

    /// <summary>Reads the value a username credential is resolved by.</summary>
    /// <param name="username">The canonical username.</param>
    /// <returns>The lookup.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="username" /> is the unspecified struct default.</exception>
    /// <remarks>The canonical form and nothing else, because the folding a username needs has already been done by the type that owns it.</remarks>
    public static OwnerCredentialLookup ForUsername(OwnerCredentialUsername username) => username.IsSpecified
        ? new OwnerCredentialLookup(username.Value)
        : throw new ArgumentException("A username credential is resolved by a specified username.", nameof(username));

    /// <summary>Reads the value a minted key or a client's public key is resolved by.</summary>
    /// <param name="digest">The key's digest, or the public key's fingerprint, as the base64url text the deployment computed.</param>
    /// <returns>The lookup.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="digest" /> is not a value this type can hold.</exception>
    /// <remarks>Named for what it is rather than for which of the two methods produced it, because a digest and a fingerprint are the same shape and are compared the same way; what differs is what was digested, which is the authenticator's question rather than this type's.</remarks>
    public static OwnerCredentialLookup ForDigest(string digest) =>
        TryCreate(digest, out var lookup)
            ? lookup
            : throw new ArgumentException(
                $"A credential digest is 1 to {MaximumLength} characters and carries no control character.",
                nameof(digest));

    /// <summary>Composes the value an authorization server's validated subject is resolved by.</summary>
    /// <param name="issuer">The issuer exactly as the token carried it.</param>
    /// <param name="subject">The subject claim the token carried.</param>
    /// <param name="lookup">The composed lookup, or the unspecified default when the pair cannot be one.</param>
    /// <returns><see langword="true" /> when the pair composes a lookup this deployment can store and resolve.</returns>
    /// <remarks>
    /// The two are held together rather than as one value each, because a subject is only ever meaningful beside the
    /// issuer that minted it: two servers may name two different people identically, and a mapping keyed by the subject
    /// alone would let one of them act for the other's owner. An issuer carrying a space is refused, since that is what
    /// separates the halves — it cannot arise from an absolute URI, and refusing it is what keeps the reading of a
    /// stored value unambiguous.
    /// </remarks>
    public static bool TryCreateForOAuthSubject(
        string? issuer,
        string? subject,
        out OwnerCredentialLookup lookup)
    {
        lookup = default;

        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            return false;
        }

        var trimmedIssuer = issuer.Trim();
        var trimmedSubject = subject.Trim();

        return !trimmedIssuer.Any(char.IsWhiteSpace)
            && TryCreate($"{trimmedIssuer}{OAuthSubjectSeparator}{trimmedSubject}", out lookup);
    }

    /// <summary>Reads a stored value back into the issuer and the subject it was composed from.</summary>
    /// <param name="issuer">The issuer the value names, or <see langword="null" /> when it names none.</param>
    /// <param name="subject">The subject the value names, or <see langword="null" /> when it names none.</param>
    /// <returns><see langword="true" /> when the value carries both halves.</returns>
    /// <remarks>For a listing and a refusal message rather than for authentication, which composes the value it is looking for instead of decomposing the ones it holds.</remarks>
    public bool TryReadOAuthSubject(
        [NotNullWhen(true)] out string? issuer,
        [NotNullWhen(true)] out string? subject)
    {
        issuer = null;
        subject = null;

        if (this.value is null)
        {
            return false;
        }

        var separator = this.value.IndexOf(OAuthSubjectSeparator, StringComparison.Ordinal);

        if (separator <= 0 || separator == this.value.Length - 1)
        {
            return false;
        }

        issuer = this.value[..separator];
        subject = this.value[(separator + 1)..];

        return true;
    }

    /// <summary>Reads a stored value back into a lookup, whichever method wrote it.</summary>
    /// <param name="stored">The value as the row holds it.</param>
    /// <param name="lookup">The lookup when the stored value is usable; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the stored value is one this type can hold.</returns>
    /// <remarks>The one entry point that accepts a value composed elsewhere, which is what a row read back out of the table it was written into needs; every other factory states which method it is composing for.</remarks>
    public static bool TryCreate(string? stored, out OwnerCredentialLookup lookup)
    {
        lookup = default;

        if (stored is null || stored.Length == 0 || stored.Length > MaximumLength || stored.Any(char.IsControl))
        {
            return false;
        }

        lookup = new OwnerCredentialLookup(stored);

        return true;
    }

    /// <inheritdoc />
    public override string ToString() => this.value ?? "(unspecified)";
}
