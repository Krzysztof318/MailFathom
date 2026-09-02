// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Paging;
using MailFathom.Domain.Mutations.Audit;

namespace MailFathom.Application.Mail.Mutations.Audit;

/// <summary>Marks where one page of an audit trail ended, so the next page continues from it.</summary>
/// <remarks>
/// <para>
/// The trail is ordered newest first by completion instant, with the entry identifier breaking a tie, and this pairs
/// those two values with a fingerprint of the filters the page was read under. The pair is what makes pagination
/// keyset-based rather than offset-based: the next page asks for entries beyond a known boundary, so a mutation
/// finishing between two requests neither shifts a window nor causes an entry to be skipped or repeated. The fingerprint
/// is what makes the boundary meaningful, because a position names a page edge only within the filtered set it was
/// computed for.
/// </para>
/// <para>
/// It carries no secret and needs no signature: every value in it is one the caller already supplied or already
/// received. Encoding is about opacity rather than protection — a client that cannot read a cursor does not build one,
/// and a built cursor is how a caller would ask for a boundary this system never computed. The encoded form itself is
/// <see cref="KeysetCursorPayload" />'s, which every keyset cursor here shares.
/// </para>
/// </remarks>
public readonly record struct MailboxMutationAuditCursor
{
    private MailboxMutationAuditCursor(
        DateTimeOffset completedAt,
        MailboxMutationAuditEntryId entryId,
        string filterFingerprint)
    {
        this.CompletedAt = completedAt;
        this.EntryId = entryId;
        this.FilterFingerprint = filterFingerprint;
    }

    /// <summary>Gets the completion instant of the last entry the page returned.</summary>
    public DateTimeOffset CompletedAt { get; }

    /// <summary>Gets the identity of that entry, which breaks a tie between two that finished together.</summary>
    public MailboxMutationAuditEntryId EntryId { get; }

    /// <summary>Gets the fingerprint of the filters this cursor was issued for.</summary>
    public string FilterFingerprint { get; }

    /// <summary>Creates the cursor that continues a walk after one entry.</summary>
    /// <param name="entry">The last entry the page returned.</param>
    /// <param name="filterFingerprint">The fingerprint of the filters the page was read under.</param>
    /// <returns>The cursor.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filterFingerprint" /> is blank.</exception>
    public static MailboxMutationAuditCursor After(MailboxMutationAuditEntry entry, string filterFingerprint)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return After(entry.CompletedAt, entry.Id, filterFingerprint);
    }

    /// <summary>Creates the cursor that continues a walk after one position in the trail.</summary>
    /// <param name="completedAt">The completion instant the page ended on.</param>
    /// <param name="entryId">The identity of the entry at that instant.</param>
    /// <param name="filterFingerprint">The fingerprint of the filters the page was read under.</param>
    /// <returns>The cursor.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="filterFingerprint" /> is blank.</exception>
    /// <remarks>
    /// The position is taken rather than an entry, because a page advances by the rows it read rather than by the
    /// entries it could present. A row this build cannot interpret is left out of the page and still passed, so a walk
    /// never stalls on one and never repeats the rows either side of it.
    /// </remarks>
    public static MailboxMutationAuditCursor After(
        DateTimeOffset completedAt,
        MailboxMutationAuditEntryId entryId,
        string filterFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filterFingerprint);

        return new MailboxMutationAuditCursor(completedAt, entryId, filterFingerprint);
    }

    /// <summary>Reads a cursor a caller presented.</summary>
    /// <param name="text">The encoded cursor, as a previous page returned it.</param>
    /// <param name="cursor">The decoded cursor when the text is one this version issued; otherwise <see langword="null" />.</param>
    /// <returns><see langword="true" /> when the text decoded into a usable cursor; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// Every entry this trail returns completed at a known instant, so a payload carrying none names no boundary here
    /// and is refused. Whether a decoded cursor belongs to the current request is a separate question its
    /// <see cref="FilterFingerprint" /> answers, and one this method deliberately does not ask.
    /// </remarks>
    public static bool TryDecode(string? text, out MailboxMutationAuditCursor? cursor)
    {
        cursor = null;

        if (!KeysetCursorPayload.TryDecode(text, out var payload) || payload.Position is not { } completedAt)
        {
            return false;
        }

        cursor = new MailboxMutationAuditCursor(
            completedAt,
            MailboxMutationAuditEntryId.Create(payload.Identity),
            payload.FilterFingerprint);

        return true;
    }

    /// <summary>Writes the cursor as the opaque string a caller presents to continue the walk.</summary>
    /// <returns>The encoded cursor.</returns>
    public string Encode() =>
        KeysetCursorPayload.At(this.CompletedAt, this.EntryId.Value, this.FilterFingerprint).Encode();
}
