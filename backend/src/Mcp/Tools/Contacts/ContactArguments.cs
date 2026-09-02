// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Failures;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;

namespace MailFathom.Mcp.Tools.Contacts;

/// <summary>Turns the text a caller names a contact by into the identities the book is expressed in.</summary>
/// <remarks>
/// <para>
/// The one thing a use case cannot do for this boundary, which is why it is here and why nothing else about a contact
/// is: every rule a record obeys belongs to the use case, and what a protocol adapter owns is converting a caller's
/// strings into domain values and refusing the ones that name nothing.
/// </para>
/// <para>
/// The refusals are the application's own failures rather than shapes invented here, so a second entrypoint refusing the
/// same text reports it under the same code. None of them repeats the refused text: an identifier a caller invented says
/// nothing an operator needs, and an address is somebody's address whether or not it was mistyped.
/// </para>
/// </remarks>
internal static class ContactArguments
{
    /// <summary>The longest text read before it is refused for not being an identifier.</summary>
    /// <remarks>
    /// The longest form <see cref="Guid.TryParse(string, out Guid)" /> accepts is the 68-character hexadecimal one, not
    /// the 38-character braced or parenthesized form, so a shorter ceiling would refuse a spelling of a well-formed UUID
    /// that every other tool on this surface accepts. The bound is applied before the parse rather than after it,
    /// because the parse scans what it is handed and the caller decides how long that is.
    /// </remarks>
    private const int MaximumIdentifierLength = 68;

    /// <summary>Reads the contact identity a caller's text names.</summary>
    /// <param name="contactId">The text the caller wrote.</param>
    /// <returns>The identity the book is expressed in.</returns>
    /// <exception cref="ContactIdentifierMalformedException">Thrown when the text is not an identifier this system issues.</exception>
    /// <remarks>
    /// The all-zero UUID is refused with everything else rather than looked up: it is the value a client sends when it
    /// has no identifier at all, and no contact is ever recorded under it.
    /// </remarks>
    public static ContactId NamedContact(string? contactId) =>
        contactId is { Length: <= MaximumIdentifierLength }
        && Guid.TryParse(contactId, out var identity)
        && identity != Guid.Empty
            ? ContactId.Create(identity)
            : throw ContactIdentifierMalformedException.NotAnIdentifier();

    /// <summary>Reads the address a caller names a person by.</summary>
    /// <param name="address">The text the caller wrote.</param>
    /// <returns>The address the book resolves people by.</returns>
    /// <exception cref="ContactIdentifierMalformedException">Thrown when the text is not a usable mail address.</exception>
    /// <remarks>
    /// What is accepted is the addr-spec alone, so a caller that copied a header's <c>Anna Kowalska
    /// &lt;anna@example.test&gt;</c> is refused rather than read leniently, and so is the bracketed form on its own,
    /// which <see cref="ContactAddressText" /> states once for both readers of a caller's address text. Splitting either
    /// here would put header parsing into a tool argument and would still leave the write tools refusing it, since the
    /// addresses a record carries are read against the same rules; one rule across the five tools is what a caller can
    /// learn once.
    /// </remarks>
    public static EmailAddress NamedAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw ContactIdentifierMalformedException.NotAnAddress();
        }

        var trimmed = address.Trim();

        if (trimmed.Length > Contact.MaximumAddressLength || ContactAddressText.IsAngleAddress(trimmed))
        {
            throw ContactIdentifierMalformedException.NotAnAddress();
        }

        if (!EmailAddress.TryCreate(displayName: null, trimmed, out var resolved))
        {
            throw ContactIdentifierMalformedException.NotAnAddress();
        }

        return resolved;
    }
}
