// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>The authserv-id of the one server whose sender-authentication statements an account believes.</summary>
/// <remarks>
/// <para>
/// <c>Authentication-Results</c> is an ordinary header, so anything upstream of the receiving server can write one, and
/// a message arriving with a fabricated header claiming that everything passed is precisely what an attacker sends. RFC
/// 8601 answers that by having each producing server stamp its own identifier into the header it adds, and by having a
/// consumer read only the headers carrying the identifier it trusts.
/// </para>
/// <para>
/// The value is a property of who receives an account's mail rather than of MailFathom, which is why it is configured
/// per account and why <see cref="None" /> is a usable state rather than a misconfiguration to work around. An account
/// that names no authority believes no header at all, and every message it holds carries the not-established verdict.
/// </para>
/// <para>
/// Only the comparison form is kept. Nothing displays an authserv-id, and the failure rules refuse a host name in any
/// message a boundary publishes, so a second copy in the original casing would exist for nothing.
/// </para>
/// </remarks>
public readonly record struct TrustedAuthenticationAuthority
{
    /// <summary>The greatest length a configured authserv-id may have, which is the length of a domain name.</summary>
    public const int MaximumLength = SenderDomain.MaximumLength;

    private TrustedAuthenticationAuthority(string normalizedValue) => this.NormalizedValue = normalizedValue;

    /// <summary>Gets the authority that believes nothing, which is what an account configuring none has.</summary>
    public static TrustedAuthenticationAuthority None => default;

    /// <summary>Gets the comparison form of the configured identifier, which a header's own identifier is matched against.</summary>
    /// <remarks>Upper-cased, for the reason every other comparison key in this system is: the form round-trips in every culture.</remarks>
    public string NormalizedValue { get; }

    /// <summary>Gets whether this authority names a server, which is false exactly for <see cref="None" />.</summary>
    public bool NamesAServer => !string.IsNullOrEmpty(this.NormalizedValue);

    /// <summary>Builds an authority from what a deployment configured.</summary>
    /// <param name="candidate">The configured authserv-id, or <see langword="null" /> when the account named none.</param>
    /// <param name="authority">The authority, which is <see cref="None" /> when the account named none.</param>
    /// <returns><see langword="true" /> when the text is usable or absent; <see langword="false" /> when it is present and unusable.</returns>
    /// <remarks>
    /// Absence and a malformed value are separated rather than merged, because startup has to refuse the second while
    /// accepting the first: an account that configured nothing has made a choice, and an account whose value is blank,
    /// over-long, or full of whitespace has made a mistake that would otherwise be discovered as mail that never
    /// authenticates.
    /// </remarks>
    public static bool TryCreate(string? candidate, out TrustedAuthenticationAuthority authority)
    {
        authority = None;

        if (candidate is null)
        {
            return true;
        }

        var trimmed = candidate.Trim();

        if (trimmed.Length is 0 or > MaximumLength
            || trimmed.Any(static character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            return false;
        }

        authority = new TrustedAuthenticationAuthority(trimmed.ToUpperInvariant());

        return true;
    }

    /// <summary>Answers whether one header's authserv-id is this authority's.</summary>
    /// <param name="authorityIdentifier">The identifier the header carried.</param>
    /// <returns><see langword="true" /> when the header was produced by the server this account believes.</returns>
    /// <remarks>
    /// Compared on the normalized form, because RFC 8601 writes the identifier as a domain-shaped token and a server is
    /// entitled to change its casing between messages. <see cref="None" /> matches nothing, including a header that
    /// wrote no identifier at all, so an account naming no authority cannot accidentally believe one.
    /// </remarks>
    public bool Produced(string? authorityIdentifier) =>
        this.NamesAServer
        && string.Equals(
            this.NormalizedValue,
            authorityIdentifier?.Trim().ToUpperInvariant(),
            StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => this.NormalizedValue ?? string.Empty;
}
