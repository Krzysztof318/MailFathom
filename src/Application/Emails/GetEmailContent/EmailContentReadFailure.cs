// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.EmailContent;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Emails.GetEmailContent;

/// <summary>Why one named email carries no content, stated as a result rather than raised.</summary>
/// <param name="ErrorCode">The stable code identifying the failure.</param>
/// <param name="Message">The sentence written for whoever, or whatever, reads the result.</param>
/// <remarks>
/// <para>
/// It is a result because the caller acts on it and continues: a read names several emails, and one this deployment
/// cannot serve must not discard the content of the others. An exception would travel past the loop that is in a
/// position to decide what it means, which is exactly the distinction the repository's failure rules draw.
/// </para>
/// <para>
/// The codes are the ones the single-email read already published, so a caller matching on `53002` or `55001` reads the
/// same fact whether it named one email or ten. The message is written here rather than at the protocol boundary, so
/// one text exists for one failure and the tests of this use case cover the wording a client is shown.
/// </para>
/// <para>
/// A message names the email's own identifier, which is MailFathom's own handle for it and carries nothing the caller
/// did not already write. Nothing else may enter it: no subject, no address, no body text, no defect the caller cannot
/// act on beyond the one this system found in its own storage.
/// </para>
/// </remarks>
public sealed record EmailContentReadFailure(MailFathomErrorCode ErrorCode, string Message)
{
    /// <summary>Reports an email the local mailbox copy holds no row for.</summary>
    /// <param name="storedEmailId">The email the read named.</param>
    /// <returns>The failure to publish for it.</returns>
    /// <remarks>
    /// One failure covers an identifier that never existed, one whose email was expunged and collected, and one
    /// belonging to an account this deployment has stopped serving. A caller that could tell them apart could learn
    /// which identifiers exist by asking, and none of the three is anything it can act on differently.
    /// </remarks>
    public static EmailContentReadFailure NotFound(StoredEmailId storedEmailId) => new(
        MailFathomErrorCode.StoredEmailNotFound,
        string.Format(
            CultureInfo.InvariantCulture,
            "Email '{0}' is not stored in this mailbox copy.",
            storedEmailId.Value));

    /// <summary>Reports an email that is stored and whose content cannot be served.</summary>
    /// <param name="storedEmailId">The email the read named.</param>
    /// <param name="defect">What was found wrong with its stored content.</param>
    /// <returns>The failure to publish for it.</returns>
    /// <remarks>
    /// It stays distinct from <see cref="NotFound" /> because the two say different things about the same request: one
    /// names an email that was never stored here, the other one that is stored and whose body this deployment cannot
    /// currently serve. Only the second schedules repair and only the second is worth repeating.
    /// </remarks>
    public static EmailContentReadFailure ContentUnavailable(
        StoredEmailId storedEmailId,
        EmailContentDefect defect) => new(
        MailFathomErrorCode.EmailContentUnavailable,
        string.Format(
            CultureInfo.InvariantCulture,
            "The locally stored content of email '{0}' cannot be served [{1}].",
            storedEmailId.Value,
            defect));
}
