// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Contacts.Failures;

/// <summary>The failure raised when a contact listing asks for something the book does not serve.</summary>
/// <remarks>
/// <para>
/// The page size, the origin, and the search text are the three things a caller composes a listing from, and each is
/// refused rather than absorbed: a page size above the ceiling is refused instead of quietly served the ceiling, so a
/// caller never reads a short page as the end of the book, and an unusable search is refused instead of matching
/// nothing, so a caller never reads an empty page as a person this deployment does not hold.
/// </para>
/// <para>
/// The message names the part of the request and its limit. It never repeats the text that was refused, because a
/// search is somebody's name or address; both the part and the limit come from this assembly rather than from the
/// request.
/// </para>
/// </remarks>
public sealed class ContactQueryInvalidException : MailFathomException
{
    private ContactQueryInvalidException(string operatorSafeMessage)
        : base(operatorSafeMessage)
    {
    }

    private ContactQueryInvalidException(string operatorSafeMessage, Exception cause)
        : base(operatorSafeMessage, cause)
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.ContactQueryInvalid;

    /// <summary>Refuses a page size outside the range the book serves.</summary>
    /// <param name="maximumPageSize">The greatest page size a listing may ask for.</param>
    /// <returns>The failure to raise.</returns>
    public static ContactQueryInvalidException PageSizeOutOfRange(int maximumPageSize) => new(
        string.Format(
            CultureInfo.InvariantCulture,
            "A contact listing holds between 1 and {0} contacts.",
            maximumPageSize));

    /// <summary>Refuses text naming no origin a contact can have.</summary>
    /// <returns>The failure to raise.</returns>
    public static ContactQueryInvalidException NotAnOrigin() =>
        new("A contact origin is either the ones somebody wrote down or the ones this deployment collected.");

    /// <summary>Refuses search text the book cannot look anybody up by.</summary>
    /// <param name="cause">The validation failure the domain value raised, kept as the inner exception.</param>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// The cause travels as the inner exception, where an operator reads it and no client does. It names no value
    /// either — the domain refusals this wraps state the rule rather than the text — but it is kept inside the boundary
    /// regardless, because that is where an inner exception belongs whatever it happens to say.
    /// </remarks>
    public static ContactQueryInvalidException NotASearch(Exception cause) => new(
        string.Format(
            CultureInfo.InvariantCulture,
            "A contact search carries text of at most {0} characters and no character that renders as nothing.",
            ContactSearch.MaximumLength),
        cause);
}
