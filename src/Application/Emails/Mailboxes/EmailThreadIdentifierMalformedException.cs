// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Emails.Mailboxes;

/// <summary>The failure raised when a request names a conversation with text that is no identifier this system issues.</summary>
/// <remarks>
/// <para>
/// The thread counterpart of <see cref="StoredEmailIdentifierMalformedException" />, and separate from it for the same
/// reason that one is separate from an absent email: a caller reading this knows which of the two arguments it got
/// wrong, which one shared code would have taken away.
/// </para>
/// <para>
/// A conversation this deployment does not hold is not this failure. That request named a conversation, and it is
/// answered with the emptiness of it rather than with a refusal, on the same terms a stored email nobody holds is
/// answered — telling the two apart would let a caller learn which identifiers exist by asking about them.
/// </para>
/// <para>
/// The message names no identifier, because the only thing there would be to name is the caller's own text on its way
/// into a client-readable result and the log line beside it.
/// </para>
/// </remarks>
public sealed class EmailThreadIdentifierMalformedException : MailFathomException
{
    /// <summary>Initializes the failure for text that names no conversation.</summary>
    public EmailThreadIdentifierMalformedException()
        : base("The thread identifier is not one this system issues.")
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.EmailThreadIdentifierMalformed;
}
