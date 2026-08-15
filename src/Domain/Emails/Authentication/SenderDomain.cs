// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>Names the domain half of a mail identity, in the one form everything about a sender compares on.</summary>
/// <remarks>
/// <para>
/// A sending domain arrives from three unrelated places — a DKIM signature's <c>d=</c> tag, an SPF check's envelope
/// sender, and the <c>From</c> header a client displays — and the whole point of a verdict is that those three can be
/// held against each other. A comparison form derived at each of those call sites would drift between them, so the
/// normalization is the type's own rule and <see cref="NormalizedValue" /> is the only form anything compares.
/// </para>
/// <para>
/// The form is upper-cased for the reason <see cref="EmailAddress.NormalizedAddress" /> is: upper case round-trips in
/// every culture, so the key means the same thing in memory, in a query, and in a database whose collation MailFathom
/// does not control.
/// </para>
/// <para>
/// A domain name is personal data once it is attached to a message, so nothing here may be logged.
/// </para>
/// </remarks>
public readonly record struct SenderDomain
{
    /// <summary>The greatest length a domain name has, which RFC 1035 fixes at 253 characters.</summary>
    public const int MaximumLength = 253;

    /// <summary>The greatest length one label of a domain name has, which RFC 1035 fixes at 63 characters.</summary>
    private const int MaximumLabelLength = 63;

    private SenderDomain(string value, string normalizedValue)
    {
        this.Value = value;
        this.NormalizedValue = normalizedValue;
    }

    /// <summary>Gets the domain exactly as its source wrote it, trimmed of surrounding whitespace.</summary>
    public string Value { get; }

    /// <summary>Gets the comparison form of <see cref="Value" />.</summary>
    public string NormalizedValue { get; }

    /// <summary>Builds a domain from what a header wrote.</summary>
    /// <param name="candidate">The domain text a header carried.</param>
    /// <param name="domain">The normalized domain, when the text is usable.</param>
    /// <returns><see langword="true" /> when the text is a domain this system will compare on; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// A malformed domain is refused rather than repaired, for the reason a malformed address is: guessing what an
    /// unparseable header meant would put a domain nobody wrote into a verdict a reader is later shown. What refusal
    /// costs is one identity of one message, which the not-established verdict already has a value for.
    /// </remarks>
    public static bool TryCreate(string? candidate, out SenderDomain domain)
    {
        domain = default;

        var trimmed = candidate?.Trim() ?? string.Empty;
        if (!IsUsableDomain(trimmed))
        {
            return false;
        }

        domain = new SenderDomain(trimmed, trimmed.ToUpperInvariant());

        return true;
    }

    /// <summary>Builds a domain from a mailbox, which is what an SPF check and a <c>From</c> header both carry.</summary>
    /// <param name="mailbox">The mailbox text, either an addr-spec or a bare domain.</param>
    /// <param name="domain">The normalized domain, when one can be read from the text.</param>
    /// <returns><see langword="true" /> when a domain was read; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// RFC 8601 writes the SPF identity as <c>smtp.mailfrom</c>, whose value is a mailbox for an ordinary message and a
    /// bare domain for one whose envelope sender is empty. Both forms are accepted here, and the domain is what follows
    /// the last at-sign because a quoted local part is allowed to contain one.
    /// </remarks>
    public static bool TryCreateFromMailbox(string? mailbox, out SenderDomain domain)
    {
        var trimmed = mailbox?.Trim() ?? string.Empty;
        var separatorIndex = trimmed.LastIndexOf('@');

        return TryCreate(separatorIndex < 0 ? trimmed : trimmed[(separatorIndex + 1)..], out domain);
    }

    /// <summary>Compares two domains by the form they were normalized to.</summary>
    /// <param name="other">The domain to compare with.</param>
    /// <returns><see langword="true" /> when both name the same domain.</returns>
    public bool Equals(SenderDomain other) =>
        string.Equals(this.NormalizedValue, other.NormalizedValue, StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() =>
        this.NormalizedValue is null ? 0 : StringComparer.Ordinal.GetHashCode(this.NormalizedValue);

    /// <inheritdoc />
    /// <remarks>
    /// A default instance carries no value, because a struct is constructible without its factory whatever the factory
    /// refuses. It reads as the empty name rather than as <see langword="null" />, so a caller that formats one is not
    /// the place the absence surfaces.
    /// </remarks>
    public override string ToString() => this.NormalizedValue ?? string.Empty;

    /// <summary>Accepts a dot-separated name of the shape every resolver and every mail transport agrees on.</summary>
    /// <remarks>
    /// The check stays narrower than the DNS grammar and deliberately says nothing about which characters a label may
    /// hold: an internationalized domain reaches this type in whichever encoding its header carried, and settling on one
    /// of those encodings is a matching decision rather than a parsing one. What is refused is what would make an
    /// unusable comparison key — an empty name, one past the length a resolver accepts, whitespace, a control character,
    /// an at-sign that says a mailbox was handed over whole, and an empty or over-long label.
    /// </remarks>
    private static bool IsUsableDomain(string domain)
    {
        if (domain.Length is 0 or > MaximumLength
            || domain.Any(static character => char.IsWhiteSpace(character) || char.IsControl(character))
            || domain.Contains('@', StringComparison.Ordinal))
        {
            return false;
        }

        return domain.Split('.').All(static label => label.Length is > 0 and <= MaximumLabelLength);
    }
}
