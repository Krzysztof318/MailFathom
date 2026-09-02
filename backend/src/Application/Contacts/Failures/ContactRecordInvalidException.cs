// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Contacts.Failures;

/// <summary>The failure raised when the record a write states is not one the contact book admits.</summary>
/// <remarks>
/// <para>
/// Every rule is checked before the book is reached, because what a caller has to be told is which rule it broke, and
/// the domain's own guards say that by naming a constructor parameter. The two rules only the domain can state — which
/// characters carry no glyph — are translated here into sentences of their own rather than published as the exception's
/// text.
/// </para>
/// <para>
/// <strong>No value a contact record carries reaches this message.</strong> A name, an address, and a note are personal
/// data about a third party, so a malformed address is reported as an address that is not usable rather than echoed
/// into a client-readable result and the log line beside it. Every limit named here comes from this system.
/// </para>
/// </remarks>
public sealed class ContactRecordInvalidException : MailFathomException
{
    private ContactRecordInvalidException(string operatorSafeMessage)
        : base(operatorSafeMessage)
    {
    }

    private ContactRecordInvalidException(string operatorSafeMessage, Exception cause)
        : base(operatorSafeMessage, cause)
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.ContactRecordInvalid;

    /// <summary>Refuses a record that names no display name.</summary>
    /// <returns>The failure to raise.</returns>
    public static ContactRecordInvalidException NoDisplayName() =>
        new("A contact record names no display name.");

    /// <summary>Refuses a display name the book cannot hold.</summary>
    /// <param name="cause">The validation failure the domain value raised, kept as the inner exception.</param>
    /// <returns>The failure to raise.</returns>
    public static ContactRecordInvalidException NotADisplayName(Exception cause) => new(
        string.Format(
            CultureInfo.InvariantCulture,
            "A contact name carries at most {0} characters and no character that renders as nothing.",
            ContactDisplayName.MaximumLength),
        cause);

    /// <summary>Refuses a record carrying no address.</summary>
    /// <returns>The failure to raise.</returns>
    public static ContactRecordInvalidException NoAddress() =>
        new("A contact holds at least one address.");

    /// <summary>Refuses a record naming more addresses than one person may be recorded as using.</summary>
    /// <returns>The failure to raise.</returns>
    public static ContactRecordInvalidException TooManyAddresses() => new(
        string.Format(
            CultureInfo.InvariantCulture,
            "A contact cannot hold more than {0} addresses.",
            Contact.MaximumAddressCount));

    /// <summary>Refuses text that is not an address the book can hold.</summary>
    /// <returns>The failure to raise.</returns>
    public static ContactRecordInvalidException NotAnAddress() => new(
        string.Format(
            CultureInfo.InvariantCulture,
            "A contact address is a usable mail address of at most {0} characters.",
            Contact.MaximumAddressLength));

    /// <summary>Refuses a record whose preferred address is not one it holds.</summary>
    /// <returns>The failure to raise.</returns>
    public static ContactRecordInvalidException PreferredAddressNotHeld() =>
        new("The preferred address is one of the addresses the contact holds.");

    /// <summary>Refuses a note the book cannot hold.</summary>
    /// <param name="cause">The validation failure the domain value raised, kept as the inner exception.</param>
    /// <returns>The failure to raise.</returns>
    public static ContactRecordInvalidException NotANote(Exception cause) => new(
        string.Format(
            CultureInfo.InvariantCulture,
            "A contact note carries at most {0} characters and no character that renders as nothing, other than line breaks and tabs.",
            ContactNote.MaximumLength),
        cause);
}
