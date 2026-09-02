// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts;

namespace MailFathom.Application.Contacts;

/// <summary>States who a lookup of the book resolved to: how many people answer to it, and which one when exactly one does.</summary>
/// <remarks>
/// <para>
/// The count is here because a caller that cannot use the answer still has to be told what was wrong with it, and the
/// only thing it may be told is how many. A name several people carry resolves to nobody — nothing ranks them and
/// nothing picks the closest — so the record carries the number and none of the contacts it counted. A lookup by
/// identity answers in the same shape and never counts past one, which is what lets a caller act on the count rather
/// than on how the contact was named.
/// </para>
/// <para>
/// The contact is present for exactly one match, which is what makes the unique case usable without a second read and
/// the ambiguous case impossible to use by accident.
/// </para>
/// </remarks>
public sealed record ContactMatch
{
    private ContactMatch(int matchCount, Contact? onlyMatch)
    {
        this.MatchCount = matchCount;
        this.OnlyMatch = onlyMatch;
    }

    /// <summary>Gets a match reporting that nobody in the book carries the name.</summary>
    public static ContactMatch None { get; } = new(matchCount: 0, onlyMatch: null);

    /// <summary>Gets how many contacts carry the name.</summary>
    public int MatchCount { get; }

    /// <summary>Gets the contact carrying the name, which is present exactly when <see cref="MatchCount" /> is one.</summary>
    public Contact? OnlyMatch { get; }

    /// <summary>Reports the one person the book holds under the name.</summary>
    /// <param name="contact">The contact carrying it.</param>
    /// <returns>A unique match.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="contact" /> is <see langword="null" />.</exception>
    public static ContactMatch Unique(Contact contact)
    {
        ArgumentNullException.ThrowIfNull(contact);

        return new ContactMatch(matchCount: 1, contact);
    }

    /// <summary>Reports that several people carry the name, and how many.</summary>
    /// <param name="matchCount">How many contacts carry it.</param>
    /// <returns>An ambiguous match.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="matchCount" /> is below two, which is a unique match or none rather than an ambiguous one.</exception>
    public static ContactMatch Several(int matchCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(matchCount, 2, nameof(matchCount));

        return new ContactMatch(matchCount, onlyMatch: null);
    }
}
