// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Globalization;
using System.Text;
using MailFathom.Domain.Contacts;

namespace MailFathom.Application.Contacts;

/// <summary>Names the contact a continued walk of the book reads beyond.</summary>
/// <remarks>
/// <para>
/// The pair is exactly what the listing is ordered by, so a contact whose name sorts identically to the last one of the
/// previous page is served once rather than skipped or repeated. The comparison form is carried rather than the name as
/// written, because that is what the order is taken on and what the index is built over.
/// </para>
/// <para>
/// The ordering is total and the same under every filter, so a cursor issued while listing one origin still names a
/// valid boundary when the walk is continued over all of them. That is why nothing here binds a cursor to the filters it
/// was issued under: no combination of them makes reusing one skip a contact or serve one twice. What can is a rename,
/// which moves a contact within the order the cursor was cut from, exactly as
/// <see cref="ContactQuery" /> states — the boundary is a position in the order rather than a snapshot of it.
/// </para>
/// </remarks>
public sealed record ContactCursor
{
    /// <summary>The greatest number of characters an encoded cursor may carry before it is refused unread.</summary>
    /// <remarks>
    /// A name is bounded at <see cref="ContactDisplayName.MaximumLength" /> characters, each of which may cost four
    /// bytes in UTF-8 before the encoding widens it again, so this is that ceiling rather than a round number: anything
    /// longer was not issued here and is refused without being decoded.
    /// </remarks>
    public const int MaximumEncodedLength = 2_048;

    /// <summary>
    /// The encoded form's version. It leads the payload so a later change to the fields refuses the cursors this version
    /// issued instead of misreading them.
    /// </summary>
    private const string FormatVersion = "1";

    /// <summary>Separates the encoded fields.</summary>
    /// <remarks>
    /// The name's comparison form is written last and the split is bounded at three fields, because a name may itself
    /// contain this character and a greedy split would then read part of somebody's name as a field of its own.
    /// </remarks>
    private const char FieldSeparator = '.';

    /// <summary>How many fields the encoded payload carries.</summary>
    private const int FieldCount = 3;

    private ContactCursor(string displayNameSortKey, ContactId contactId)
    {
        this.DisplayNameSortKey = displayNameSortKey;
        this.ContactId = contactId;
    }

    /// <summary>Gets the comparison form of the last served contact's name.</summary>
    public string DisplayNameSortKey { get; }

    /// <summary>Gets the last served contact, which settles the order between contacts whose names compare equal.</summary>
    public ContactId ContactId { get; }

    /// <summary>Names the boundary after one served contact.</summary>
    /// <param name="displayName">The name of the last contact the page served.</param>
    /// <param name="contactId">The identity of the last contact the page served.</param>
    /// <returns>The boundary the following page reads beyond.</returns>
    public static ContactCursor After(ContactDisplayName displayName, ContactId contactId) =>
        new(displayName.SortKey, contactId);

    /// <summary>Reads a cursor a caller presented.</summary>
    /// <param name="text">The encoded cursor, as a previous page returned it.</param>
    /// <param name="cursor">The decoded cursor when the text is one this version issued; otherwise <see langword="null" />.</param>
    /// <returns><see langword="true" /> when the text decoded into a usable cursor; otherwise <see langword="false" />.</returns>
    public static bool TryDecode(string? text, out ContactCursor? cursor)
    {
        cursor = null;

        if (text is null || text.Length is 0 or > MaximumEncodedLength)
        {
            return false;
        }

        // Validity is checked separately because the decoder's Try form reports only that a destination was too small
        // and throws on text that is not base64url at all, which is the shape a caller most easily presents.
        if (!Base64Url.IsValid(text))
        {
            return false;
        }

        var decoded = new byte[Base64Url.GetMaxDecodedLength(text.Length)];

        if (!Base64Url.TryDecodeFromChars(text, decoded, out var decodedLength))
        {
            return false;
        }

        var fields = Encoding.UTF8.GetString(decoded, 0, decodedLength).Split(FieldSeparator, FieldCount);

        if (fields is not [FormatVersion, var identifierField, var sortKeyField]
            || !Guid.TryParseExact(identifierField, "N", out var identifier)
            || identifier == Guid.Empty
            || sortKeyField.Length is 0)
        {
            return false;
        }

        cursor = new ContactCursor(sortKeyField, ContactId.Create(identifier));

        return true;
    }

    /// <summary>Writes the cursor as the opaque string a caller presents to continue the walk.</summary>
    /// <returns>The encoded cursor.</returns>
    /// <remarks>
    /// <para>
    /// The identity is written before the name's comparison form so the form can carry the separator without the
    /// decoding having to guess where it ends.
    /// </para>
    /// <para>
    /// Encoding is opacity rather than protection, exactly as it is for every other cursor here: a client that cannot
    /// read one does not build one. It is not a place to put a value that must not travel — which is why the comparison
    /// form the ordering needs is the only part of a person this carries, and why nothing logs a cursor.
    /// </para>
    /// </remarks>
    public string Encode()
    {
        var payload = string.Join(
            FieldSeparator,
            FormatVersion,
            this.ContactId.Value.ToString("N", CultureInfo.InvariantCulture),
            this.DisplayNameSortKey);

        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));
    }
}
