// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Emails.GetEmailContent;

/// <summary>The failure raised when one read names the same email more than once.</summary>
/// <remarks>
/// <para>
/// Neither answer to a repeated identifier is one this system may choose for the caller. Serving it twice spends the
/// read's character budget on content the caller already holds and displaces an email it has not read; collapsing it
/// returns fewer entries than were named, which a caller reading results positionally cannot detect.
/// </para>
/// <para>
/// The message names no identifier. Which one was repeated is the caller's own input on its way into a client-readable
/// result, and the caller holds the list it sent.
/// </para>
/// </remarks>
public sealed class EmailContentReadDuplicateEmailException : MailFathomException
{
    /// <summary>Initializes the failure.</summary>
    public EmailContentReadDuplicateEmailException()
        : base("A content read names each email at most once.")
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.EmailContentReadDuplicateEmail;
}
