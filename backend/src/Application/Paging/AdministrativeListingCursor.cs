// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Paging;

/// <summary>Writes and reads the cursor an administrative listing walked in identifier order continues from.</summary>
/// <remarks>
/// <para>
/// These listings are ordered by the row's identifier alone, which is unique, total, and what the primary key's index is
/// already ordered by — so the boundary is that one value and a page is read straight off the index however many rows
/// the deployment holds. Every identifier MailFathom mints is a version 7 UUID, whose leading bits are the instant it
/// was minted, so the order is also the order the rows were recorded in.
/// </para>
/// <para>
/// The encoding is <see cref="KeysetCursorPayload" />'s with no position, and the listing's name is what the fingerprint
/// is taken over: these listings take no filters, so the one thing a cursor can be wrong about is which listing issued
/// it. A listing that walks the rows of one record — a group's members — is a different set per record, so the
/// fingerprint is taken over that record's identifier as well and a cursor carried from one group to another is refused
/// rather than read as a position among members it never described.
/// </para>
/// </remarks>
public static class AdministrativeListingCursor
{
    /// <summary>Writes the cursor the page after one row continues from.</summary>
    /// <param name="listing">The listing the page was read from.</param>
    /// <param name="after">The identifier of the last row the page held.</param>
    /// <param name="within">The record whose rows the listing walks, or <see langword="null" /> for a listing of the deployment's own.</param>
    /// <returns>The opaque cursor.</returns>
    public static string Encode(AdministrativeListing listing, Guid after, Guid? within = null) =>
        KeysetCursorPayload.At(null, after, FingerprintOf(listing, within)).Encode();

    /// <summary>Reads a cursor a caller presented to one listing.</summary>
    /// <param name="text">The cursor, as a previous page returned it.</param>
    /// <param name="listing">The listing it was presented to.</param>
    /// <param name="after">The identifier the walk continues after when the cursor was issued by that listing; otherwise <see cref="Guid.Empty" />.</param>
    /// <param name="within">The record whose rows the listing walks, or <see langword="null" /> for a listing of the deployment's own.</param>
    /// <returns><see langword="true" /> when the text is a cursor this listing issued for that record; otherwise <see langword="false" />.</returns>
    public static bool TryDecode(string? text, AdministrativeListing listing, out Guid after, Guid? within = null)
    {
        after = Guid.Empty;

        if (!KeysetCursorPayload.TryDecode(text, out var payload)
            || payload.Position is not null
            || !string.Equals(payload.FilterFingerprint, FingerprintOf(listing, within), StringComparison.Ordinal))
        {
            return false;
        }

        after = payload.Identity;

        return true;
    }

    private static string FingerprintOf(AdministrativeListing listing, Guid? within) => within is { } record
        ? PageFilterFingerprint.Of(listing.ToString(), record.ToString("D"))
        : PageFilterFingerprint.Of(listing.ToString());
}
