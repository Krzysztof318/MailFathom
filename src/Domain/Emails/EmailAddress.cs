// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails;

/// <summary>Names one mail participant by its address and, when the sender wrote one, its display name.</summary>
/// <remarks>
/// Normalization is a domain rule rather than a query-time convenience: search, filtering, and future deduplication all
/// depend on two addresses that differ only in case comparing equal, and a comparison form derived at each call site
/// would drift between them. <see cref="Address" /> keeps what the message said, while
/// <see cref="NormalizedAddress" /> is the only form anything compares, groups, or indexes on — and the only one that
/// takes part in equality.
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

    /// <summary>Gets the address exactly as the message wrote it, trimmed of surrounding whitespace.</summary>
    public string Address { get; }

    /// <summary>Gets the comparison form of <see cref="Address" />.</summary>
    /// <remarks>
    /// The form is upper-cased for the same reason a folder alias is: upper case round-trips in every culture, so the
    /// key means the same thing in memory, in a query, and in a database whose collation MailFathom does not control. It
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

        var trimmedAddress = address?.Trim() ?? string.Empty;
        if (!IsUsableAddress(trimmedAddress))
        {
            return false;
        }

        emailAddress = new EmailAddress(
            NormalizeDisplayName(displayName),
            trimmedAddress,
            trimmedAddress.ToUpperInvariant());

        return true;
    }

    /// <summary>Splits an addr-spec into its two halves at the at-sign every split in this system is made at.</summary>
    /// <param name="address">The addr-spec text to read, which may be anything a header carried.</param>
    /// <param name="localPartText">What precedes the last at-sign, when the text names a mailbox.</param>
    /// <param name="domainText">What follows it.</param>
    /// <returns><see langword="true" /> when the text has both halves; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// The last at-sign, because a quoted local part is allowed to contain one. Both halves must be non-empty for the
    /// text to name a mailbox at all, so text that is only a domain, only a local part, or a bare at-sign is refused
    /// rather than handed back with an empty half every caller would have to check for. Neither half is validated here:
    /// what a usable local part and a usable domain are belongs to the types that own those questions.
    /// </remarks>
    public static bool TrySplit(
        ReadOnlySpan<char> address,
        out ReadOnlySpan<char> localPartText,
        out ReadOnlySpan<char> domainText)
    {
        var separatorIndex = address.LastIndexOf('@');

        if (separatorIndex <= 0 || separatorIndex == address.Length - 1)
        {
            localPartText = default;
            domainText = default;

            return false;
        }

        localPartText = address[..separatorIndex];
        domainText = address[(separatorIndex + 1)..];

        return true;
    }

    /// <summary>Splits this address into the comparison form of its local part and the text of its domain.</summary>
    /// <param name="normalizedLocalPart">The local part in the form <see cref="NormalizedAddress" /> is written in.</param>
    /// <param name="domainText">The domain exactly as the message wrote it, which is the form a domain type normalizes from.</param>
    /// <returns><see langword="true" /> when the address has both halves; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// The two halves are handed back in different forms because they normalize differently: the local part is compared
    /// as text and takes this type's comparison form, while the domain has an encoding to settle and is left to the type
    /// that settles it. Reading the domain from <see cref="Address" /> rather than from the comparison form is what
    /// keeps a domain type's own record of what its source wrote intact.
    /// </remarks>
    public bool TrySplit(out string normalizedLocalPart, out ReadOnlySpan<char> domainText)
    {
        if (!TrySplit(this.Address, out var localPartText, out domainText))
        {
            normalizedLocalPart = string.Empty;

            return false;
        }

        normalizedLocalPart = this.NormalizedAddress[..localPartText.Length];

        return true;
    }

    /// <summary>Compares two addresses by the form they were normalized to.</summary>
    /// <param name="other">The address to compare with.</param>
    /// <returns><see langword="true" /> when both name the same mailbox.</returns>
    /// <remarks>
    /// Equality is the comparison form alone, because the other two members are presentation: the display name is what
    /// one sender chose to write, and <see cref="Address" /> keeps that sender's casing. Letting either take part would
    /// make <c>Anna@Example.test</c> and <c>anna@example.test</c> two participants in any <c>Distinct</c>, set, or
    /// dictionary — the exact merge normalization exists to perform.
    /// </remarks>
    public bool Equals(EmailAddress other) =>
        string.Equals(this.NormalizedAddress, other.NormalizedAddress, StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() =>
        this.NormalizedAddress is null ? 0 : StringComparer.Ordinal.GetHashCode(this.NormalizedAddress);

    /// <inheritdoc />
    public override string ToString() => this.NormalizedAddress;

    /// <summary>Accepts one addr-spec of the shape every mail transport agrees on.</summary>
    /// <remarks>
    /// <para>
    /// The addr-spec arrives already unfolded and already parsed — a mail parser resolves header folding long before an
    /// address reaches a domain type — so nothing here rewrites what it was given. Whitespace inside the value is
    /// therefore significant rather than leftover folding: <c>"John Smith"@example.com</c> is a valid mailbox, and
    /// deleting its space would record an address nobody wrote and merge it with a different participant.
    /// </para>
    /// <para>
    /// The domain is what follows the last at-sign, because a quoted local part is allowed to contain one. An
    /// unquoted local part is not, and neither half may carry whitespace or a control character. The check stays
    /// narrower than RFC 5322 — it accepts no comments and no domain literals — and what it refuses is what would
    /// otherwise become an unusable comparison key.
    /// </para>
    /// </remarks>
    private static bool IsUsableAddress(string address)
    {
        if (address.Length == 0 || address.Any(char.IsControl))
        {
            return false;
        }

        if (!TrySplit(address, out var localPartText, out var domainText))
        {
            return false;
        }

        return !ContainsWhiteSpace(domainText) && IsUsableLocalPart(localPartText);
    }

    /// <summary>Accepts a local part that is either an ordinary token or a quoted string.</summary>
    private static bool IsUsableLocalPart(ReadOnlySpan<char> localPart)
    {
        var isQuoted = localPart.Length > 1
            && localPart[0] == '"'
            && localPart[^1] == '"';

        return isQuoted
            ? localPart.Length > 2
            : !ContainsWhiteSpace(localPart) && !localPart.Contains('@');
    }

    /// <summary>Reports whether a half of an address carries whitespace, which neither unquoted half may.</summary>
    /// <remarks>
    /// Written as a walk rather than as a query, because the halves are spans cut out of the address and materializing
    /// either of them to ask this question would allocate a string on the path every parsed address takes.
    /// </remarks>
    private static bool ContainsWhiteSpace(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                return true;
            }
        }

        return false;
    }

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
