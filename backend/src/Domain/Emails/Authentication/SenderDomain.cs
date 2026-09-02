// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text;

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>Names the domain half of a mail identity, in the one form everything about a sender compares on.</summary>
/// <remarks>
/// <para>
/// A sending domain arrives from four unrelated places — a DKIM signature's <c>d=</c> tag, an SPF check's envelope
/// sender, the <c>From</c> header a client displays, and an entry an operator wrote on a trusted-sender list — and the
/// whole point of a verdict is that those can be held against each other. A comparison form derived at each of those
/// call sites would drift between them, so the normalization is the type's own rule and <see cref="NormalizedValue" />
/// is the only form anything compares.
/// </para>
/// <para>
/// The form is upper-cased for the reason <see cref="EmailAddress.NormalizedAddress" /> is: upper case round-trips in
/// every culture, so the key means the same thing in memory, in a query, and in a database whose collation MailFathom
/// does not control.
/// </para>
/// <para>
/// It is also the ASCII form of an internationalized name. The same domain reaches this type written both ways — a
/// header carries the A-labels a transport agreed on while an operator types the name their language spells — and
/// comparing the two encodings against each other would answer no on names that are the same name. Everything is
/// therefore held in A-labels, which is the encoding the wire already uses, so an ASCII value is its own normal form
/// and costs no conversion at all.
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
    /// costs is one identity of one message, which the not-established verdict already has a value for. A name whose
    /// A-labels no encoder can produce is refused the same way, and so is one whose ASCII form outgrows the bounds
    /// below, since it is that form a column has to hold.
    /// </remarks>
    public static bool TryCreate(string? candidate, out SenderDomain domain)
    {
        domain = default;

        var trimmed = candidate?.Trim() ?? string.Empty;
        if (!IsUsableDomain(trimmed)
            || !TryNormalize(trimmed, out var normalized)
            || !IsUsableDomain(normalized))
        {
            return false;
        }

        domain = new SenderDomain(trimmed, normalized);

        return true;
    }

    /// <summary>Builds a domain from a mailbox, which is what an SPF check and a <c>From</c> header both carry.</summary>
    /// <param name="mailbox">The mailbox text, either an addr-spec or a bare domain.</param>
    /// <param name="domain">The normalized domain, when one can be read from the text.</param>
    /// <returns><see langword="true" /> when a domain was read; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// RFC 8601 writes the SPF identity as <c>smtp.mailfrom</c>, whose value is a mailbox for an ordinary message and a
    /// bare domain for one whose envelope sender is empty. Both forms are accepted here, and the domain is what follows
    /// the last at-sign because a quoted local part is allowed to contain one. Text carrying an at-sign but no local
    /// part is neither form, and it is refused rather than read as the domain that follows it: a mailbox missing a half
    /// says the identity was written wrongly, which the not-established verdict already has a value for.
    /// </remarks>
    public static bool TryCreateFromMailbox(string? mailbox, out SenderDomain domain)
    {
        var trimmed = mailbox?.Trim() ?? string.Empty;

        return EmailAddress.TrySplit(trimmed, out _, out var domainText)
            ? TryCreate(domainText.ToString(), out domain)
            : TryCreate(trimmed, out domain);
    }

    /// <summary>Answers whether this domain lies beneath another one in the naming tree.</summary>
    /// <param name="ancestor">The domain this one may sit under.</param>
    /// <returns><see langword="true" /> when this is a strictly lower name than <paramref name="ancestor" />.</returns>
    /// <remarks>
    /// The comparison is on whole labels rather than on a suffix of characters, which is what separates
    /// <c>mail.example.test</c> from <c>notexample.test</c>: both end in <c>example.test</c> as text and only the first
    /// is beneath it. A domain is not beneath itself, so a caller that means "this name or anything under it" says both.
    /// </remarks>
    public bool IsSubdomainOf(SenderDomain ancestor) =>
        this.NormalizedValue is { Length: > 0 } descendant
        && ancestor.NormalizedValue is { Length: > 0 } parent
        && descendant.Length > parent.Length + 1
        && descendant[descendant.Length - parent.Length - 1] == '.'
        && descendant.EndsWith(parent, StringComparison.Ordinal);

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

    /// <summary>Puts a name into the ASCII encoding everything compares on.</summary>
    /// <remarks>
    /// An all-ASCII name is already in that encoding, so it is upper-cased and nothing else — which is both the fast
    /// path for essentially every message and the guarantee that ordinary mail is unaffected by the conversion existing.
    /// Anything else is a name written in its own script, and the encoder is what turns it into the A-labels a DKIM
    /// signature and an SMTP envelope carry. A name the encoder refuses is refused here rather than compared in the
    /// encoding it arrived in, because a value nothing else can produce would match nothing and silently look like a
    /// sender who is simply not on a list.
    /// </remarks>
    private static bool TryNormalize(string domain, out string normalized)
    {
        if (Ascii.IsValid(domain))
        {
            normalized = domain.ToUpperInvariant();

            return true;
        }

        try
        {
            normalized = new IdnMapping().GetAscii(domain).ToUpperInvariant();

            return true;
        }
        catch (ArgumentException)
        {
            normalized = string.Empty;

            return false;
        }
    }

    /// <summary>Accepts a dot-separated name of the shape every resolver and every mail transport agrees on.</summary>
    /// <remarks>
    /// The check stays narrower than the DNS grammar and deliberately says nothing about which characters a label may
    /// hold, because which script a name is written in is <see cref="TryNormalize" />'s question rather than this one's.
    /// What is refused is what would make an unusable comparison key — an empty name, one past the length a resolver
    /// accepts, whitespace, a control character, an at-sign that says a mailbox was handed over whole, and an empty or
    /// over-long label. It is applied to both encodings of a name, since the ASCII form of an internationalized one is
    /// the longer of the two and is what a column has to hold.
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
