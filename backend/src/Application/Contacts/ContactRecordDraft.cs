// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts;

namespace MailFathom.Application.Contacts;

/// <summary>The record a caller wants a contact to have, as the caller wrote it and before any rule has judged it.</summary>
/// <remarks>
/// <para>
/// A draft states the whole record rather than a change to one, because that is what a write to the book is: adding an
/// address, dropping one, choosing a different default, and correcting a name are one operation whose result the
/// invariants can be checked against, instead of four that could each leave a contact without an address.
/// </para>
/// <para>
/// Every field is text a caller supplied, which is what makes this a draft rather than a record. The values become
/// domain ones inside the use case, so the rules the book holds — what a name may carry, which addresses are usable,
/// which of them may be preferred, how long a note may be — are applied in one place for every writer.
/// </para>
/// <para>
/// A name, an address, and a note are personal data about a third party. Nothing logs a draft, and no refusal it
/// produces repeats a value it carried.
/// </para>
/// </remarks>
public sealed record ContactRecordDraft
{
    /// <summary>Gets the name to record for this person.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Gets every address this person uses, at most <see cref="Contact.MaximumAddressCount" /> values, of which two spellings of one address are stored as one.</summary>
    /// <remarks>The ceiling is on the values sent rather than on the mailboxes they name, so a list naming fewer distinct addresses than it holds entries is refused on its length before anything is deduplicated.</remarks>
    public IReadOnlyList<string>? Addresses { get; init; }

    /// <summary>Gets the address to use by default, which must be one of <see cref="Addresses" />.</summary>
    /// <remarks>
    /// Stated rather than inferred, even where the record names one address, because which address is preferred is the
    /// owner's choice and nothing in the book picks one for them.
    /// </remarks>
    public string? PreferredAddress { get; init; }

    /// <summary>Gets what the owner wrote about this person, or <see langword="null" /> for none.</summary>
    /// <remarks>Blank text is the absence of a note rather than an empty one, so a caller clearing a note sends the field empty instead of reaching for a second verb.</remarks>
    public string? Note { get; init; }
}
