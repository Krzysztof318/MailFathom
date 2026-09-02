// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Emails.GetEmailContent;

/// <summary>The failure raised when a read names both the emails and the conversation to read, or neither.</summary>
/// <remarks>
/// <para>
/// The two selections are alternatives rather than filters that compose, so a call carrying both is refused instead of
/// resolved by precedence: honouring the list would ignore a conversation somebody asked for, and honouring the
/// conversation would return messages nobody named. Either way the caller receives mail it did not ask for and has no
/// way to tell that it did.
/// </para>
/// <para>
/// A call carrying neither is the same finding from the other side — it names nothing to read — and is answered the same
/// way rather than with an empty result, which a caller would read as a mailbox holding nothing.
/// </para>
/// </remarks>
public sealed class EmailContentReadSelectionInvalidException : MailFathomException
{
    /// <summary>Initializes the failure.</summary>
    public EmailContentReadSelectionInvalidException()
        : base("A content read names either the emails to read or the thread to read, and exactly one of the two.")
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.EmailContentReadSelectionInvalid;
}
