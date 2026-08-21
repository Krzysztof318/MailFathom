// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Contacts.Failures;

/// <summary>The failure raised when a contact listing presents a continuation cursor this system did not issue.</summary>
/// <remarks>
/// <para>
/// A cursor is opaque, so a caller that cannot read one does not build one: text that does not decode is a cursor from
/// a different deployment, a truncated one, or one somebody wrote by hand. Continuing from it would serve a page from a
/// boundary nothing here chose, which reads as a walk that skipped people.
/// </para>
/// <para>
/// There is no counterpart for a cursor presented with different filters, because a contact cursor is bound to none:
/// the book is walked in one total order whatever narrows the page, so a cursor cut under one search still names a
/// valid boundary under another.
/// </para>
/// </remarks>
public sealed class ContactCursorMalformedException : MailFathomException
{
    /// <summary>Initializes the failure for a cursor this system did not issue.</summary>
    public ContactCursorMalformedException()
        : base("The contact continuation cursor is not one this system issued.")
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.ContactCursorMalformed;
}
