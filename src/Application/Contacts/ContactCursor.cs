// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

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
}
