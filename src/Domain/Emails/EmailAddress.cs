// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Domain.Emails;

/// <summary>Names one mail participant by its address and, when the sender wrote one, its display name.</summary>
/// <remarks>
/// Normalization is a domain rule rather than a query-time convenience: search, filtering, and future deduplication all
/// depend on two addresses that differ only in case or in header folding comparing equal, and a comparison form derived
/// at each call site would drift between them. <see cref="Address" /> keeps what the message said, while
/// <see cref="NormalizedAddress" /> is the only form anything compares, groups, or indexes on.
/// </remarks>
public readonly record struct EmailAddress
{
    private EmailAddress(string? displayName, string address, string normalizedAddress)
    {
        this.DisplayName = displayName;
        this.Address = address;
        this.NormalizedAddress = normalizedAddress;
    }

    /// <summary>Gets the display name the message carried, or <see langword="null" /> when it carried none.</summary>
    public string? DisplayName { get; }

    /// <summary>Gets the address as the message wrote it, with header folding removed.</summary>
    public string Address { get; }

    /// <summary>Gets the comparison form of <see cref="Address" />.</summary>
    /// <remarks>
    /// The form is upper-cased for the same reason a folder alias is: upper case round-trips in every culture, so the
    /// key means the same thing in memory, in a query, and in a database whose collation MailMcp does not control. It
    /// is a comparison key rather than something to display; <see cref="Address" /> is what a reader is shown.
    /// </remarks>
    public string NormalizedAddress { get; }

    /// <summary>Builds a participant address from the parts a mail header supplied.</summary>
    /// <param name="displayName">The display name the header carried, if any.</param>
    /// <param name="address">The addr-spec the header carried.</param>
    /// <param name="emailAddress">The normalized address, when the addr-spec is usable.</param>
    /// <returns><see langword="true" /> when the address is usable; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// A malformed address is refused rather than repaired, because guessing what an unparseable header meant would put
    /// an address nobody wrote into a filter someone later trusts. Refusal costs one participant of one message, which
    /// is why extraction skips it instead of failing the message.
    /// </remarks>
    public static bool TryCreate(string? displayName, string? address, out EmailAddress emailAddress)
    {
        emailAddress = default;

        var unfoldedAddress = RemoveInsignificantWhitespace(address);
        if (!IsUsableAddress(unfoldedAddress))
        {
            return false;
        }

        emailAddress = new EmailAddress(
            NormalizeDisplayName(displayName),
            unfoldedAddress,
            unfoldedAddress.ToUpperInvariant());

        return true;
    }

    /// <inheritdoc />
    public override string ToString() => this.NormalizedAddress;

    /// <summary>Accepts one addr-spec of the shape every mail transport agrees on.</summary>
    /// <remarks>
    /// The check is deliberately narrower than RFC 5322, which permits quoted local parts and domain literals that no
    /// production mailbox uses. What it rejects is what would otherwise become an unusable comparison key: an address
    /// with no domain, no local part, or several at-signs.
    /// </remarks>
    private static bool IsUsableAddress(string address)
    {
        if (address.Length == 0 || address.Any(char.IsControl))
        {
            return false;
        }

        var atSignIndex = address.IndexOf('@', StringComparison.Ordinal);

        return atSignIndex > 0
            && atSignIndex == address.LastIndexOf('@')
            && atSignIndex < address.Length - 1;
    }

    /// <summary>Removes the whitespace a folded header introduced, which carries no meaning inside an addr-spec.</summary>
    private static string RemoveInsignificantWhitespace(string? address) =>
        address is null ? string.Empty : new string([.. address.Where(character => !char.IsWhiteSpace(character))]);

    /// <summary>Keeps a display name readable and refuses to let one span lines it never spanned.</summary>
    private static string? NormalizeDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var withoutControlCharacters = new string([.. displayName.Where(character => !char.IsControl(character))]).Trim();

        return withoutControlCharacters.Length == 0 ? null : withoutControlCharacters;
    }
}
