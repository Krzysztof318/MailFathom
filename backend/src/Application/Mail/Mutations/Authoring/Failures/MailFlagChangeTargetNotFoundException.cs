// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Mutations.Authoring.Failures;

/// <summary>The failure raised when a change names an email this deployment holds no readable row for.</summary>
/// <remarks>
/// <para>
/// It answers four cases with one sentence: no row carries that identity, the row belongs to an account this
/// deployment no longer serves, the row is in a folder an operator withheld from tools, and the row names a remote
/// occurrence the server has expunged — a local copy retained after MailFathom deleted the message, which a listing
/// still serves because the mail is readable while the UID it carries names nothing the server holds. Telling them
/// apart would let a caller learn which identifiers exist by asking about them, which is the same reading every mailbox
/// read applies to the first three.
/// </para>
/// <para>
/// It carries <see cref="MailFathomErrorCode.StoredEmailNotFound" />, which is the code a request naming an email
/// nothing serves already publishes. A caller meeting it on a read and on a write is meeting the same fact, and a
/// second code for it would suggest otherwise.
/// </para>
/// <para>
/// Nothing about the email reaches the message, including the identifier the caller sent: it is the caller's own input
/// on its way back into a client-readable result and a log line beside it, and the answer does not depend on it.
/// </para>
/// </remarks>
public sealed class MailFlagChangeTargetNotFoundException : MailFathomException
{
    /// <summary>Refuses a change against an email this deployment does not serve to tools.</summary>
    public MailFlagChangeTargetNotFoundException()
        : base("The named email is not one this deployment serves, so no change can be written down against it.")
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.StoredEmailNotFound;
}
