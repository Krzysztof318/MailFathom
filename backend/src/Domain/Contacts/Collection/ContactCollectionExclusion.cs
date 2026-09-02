// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.IO.Enumeration;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Domain.Contacts.Collection;

/// <summary>One address, or one whole domain, an account's owner said collection never records.</summary>
/// <remarks>
/// <para>
/// An entry names a domain or writes a pattern over the address, and the two answer different questions. A domain entry
/// is the shape for correspondence that all arrives from one place an owner wants nothing kept from — a ticketing
/// system, a newsletter provider, an employer's automation. A pattern is the shape for a name rather than a place:
/// <c>*+noreply@*</c> and <c>bot-*@example.test</c> select on how an address is spelled, wherever it is hosted.
/// </para>
/// <para>
/// Reaching under a domain is opt-in per entry rather than a mode the list runs in, for the reason
/// <see cref="TrustedSenderEntry" /> gives: one host excluded inside a domain full of correspondents must not take the
/// rest of the domain with it, and an organization whose automation lives on names beneath its own domain must be
/// excludable in one entry.
/// </para>
/// <para>
/// A pattern is held against the address's own comparison form, so it is the same matching rule the book stores and
/// looks addresses up under and a sender's casing decides nothing. Two wildcards are read: <c>*</c> stands for any run
/// of characters including none, and <c>?</c> for exactly one. Everything else in the pattern is the literal text of an
/// address, so an entry that writes no wildcard at all excludes exactly one mailbox.
/// </para>
/// <para>
/// An entry is personal data — it names who an owner does not want recorded — so nothing here may be logged.
/// </para>
/// </remarks>
public sealed record ContactCollectionExclusion
{
    /// <summary>The greatest length a pattern may carry.</summary>
    /// <remarks>
    /// The longest address the book holds, because a pattern longer than the longest value it could match excludes
    /// nothing while still being scanned against every address collection considers.
    /// </remarks>
    public const int MaximumPatternLength = Contact.MaximumAddressLength;

    private ContactCollectionExclusion(SenderDomain domain, bool includesSubdomains, string? addressPattern)
    {
        this.Domain = domain;
        this.IncludesSubdomains = includesSubdomains;
        this.AddressPattern = addressPattern;
    }

    /// <summary>Gets the domain this entry excludes, or the default value when the entry writes a pattern instead.</summary>
    public SenderDomain Domain { get; }

    /// <summary>Gets whether a domain entry also reaches the names beneath <see cref="Domain" />.</summary>
    public bool IncludesSubdomains { get; }

    /// <summary>Gets the comparison form of the pattern, or <see langword="null" /> when the entry names a domain.</summary>
    public string? AddressPattern { get; }

    /// <summary>Builds an entry that excludes every address at one domain.</summary>
    /// <param name="domain">The domain text an operator supplied.</param>
    /// <param name="includeSubdomains">Whether the entry also reaches the names beneath that domain.</param>
    /// <param name="exclusion">The entry, when the text is a domain this system compares on.</param>
    /// <returns><see langword="true" /> when the text is usable; otherwise <see langword="false" />.</returns>
    public static bool TryCreateForDomain(
        string? domain,
        bool includeSubdomains,
        [NotNullWhen(true)] out ContactCollectionExclusion? exclusion)
    {
        exclusion = null;

        if (!SenderDomain.TryCreate(domain, out var excludedDomain))
        {
            return false;
        }

        exclusion = new ContactCollectionExclusion(excludedDomain, includeSubdomains, addressPattern: null);

        return true;
    }

    /// <summary>Builds an entry that excludes every address one pattern selects.</summary>
    /// <param name="pattern">The pattern text an operator supplied.</param>
    /// <param name="exclusion">The entry, when the text is a pattern this system can hold an address against.</param>
    /// <returns><see langword="true" /> when the text is usable; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// <para>
    /// Blank text and text beyond <see cref="MaximumPatternLength" /> are refused, and so is a pattern that narrows
    /// nothing: an entry matching every address would switch collection off through a list written to narrow it, and an
    /// owner meaning that turns collection off where it is turned on.
    /// </para>
    /// <para>
    /// A pattern narrows nothing when its only characters are the two wildcards and the at-sign, which is wider than
    /// refusing an all-wildcard pattern and is the shape that actually reaches an owner. <c>*@*</c> is not all wildcards
    /// — it carries a literal — and yet it matches every address there is, because a normalized address holds exactly
    /// one at-sign and arbitrary text on either side of it. The same rule also refuses the mirror mistakes, <c>*@</c>
    /// and <c>@*</c>, which match no address at all, and that is deliberate: an entry selecting on nothing an address
    /// can differ by is a typo whichever way it lands, and this is the one place it can still be said out loud.
    /// </para>
    /// </remarks>
    public static bool TryCreateForAddressPattern(
        string? pattern,
        [NotNullWhen(true)] out ContactCollectionExclusion? exclusion)
    {
        exclusion = null;

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        var trimmed = pattern.Trim();

        if (trimmed.Length > MaximumPatternLength || NarrowsNothing(trimmed))
        {
            return false;
        }

        exclusion = new ContactCollectionExclusion(
            domain: default,
            includesSubdomains: false,
            trimmed.ToUpperInvariant());

        return true;
    }

    /// <summary>Answers whether this entry excludes one address from ever being collected.</summary>
    /// <param name="address">The address collection is considering.</param>
    /// <returns><see langword="true" /> when the entry names that address.</returns>
    /// <remarks>
    /// A pattern is matched by the platform's own simple-expression matcher rather than by a regular expression built
    /// from operator text: it reads the two wildcards this entry documents and nothing else, so a pattern cannot become
    /// an expression whose cost the person who wrote it did not intend.
    /// </remarks>
    public bool Excludes(EmailAddress address)
    {
        if (this.AddressPattern is { } pattern)
        {
            return FileSystemName.MatchesSimpleExpression(pattern, address.NormalizedAddress, ignoreCase: false);
        }

        return SenderDomain.TryCreateFromMailbox(address.Address, out var domain)
            && (domain == this.Domain || (this.IncludesSubdomains && domain.IsSubdomainOf(this.Domain)));
    }

    /// <summary>Answers whether a pattern selects on nothing an address can differ by.</summary>
    private static bool NarrowsNothing(string pattern) =>
        pattern.All(static character => character is '*' or '?' or '@');
}
