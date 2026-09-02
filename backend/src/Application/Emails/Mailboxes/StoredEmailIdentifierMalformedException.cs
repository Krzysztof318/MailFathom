// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Emails.Mailboxes;

/// <summary>The failure raised when a request names an email with text that is no identifier this system issues.</summary>
/// <remarks>
/// <para>
/// A protocol adapter converts a caller's text into the domain identity a read is expressed in, and text that is
/// neither is refused there, before anything is looked up. The failure lives in this assembly rather than in the
/// adapter for the reason <see cref="MailboxQueryFilterInvalidException" /> does: the identity it is about is the
/// application's, so a second entrypoint refusing the same text reports it under the same code instead of inventing
/// one.
/// </para>
/// <para>
/// It is deliberately distinct from the per-email failure a read reports for an email this deployment does not hold.
/// That one answers a request that named an email, and a caller acts on it by looking the email up again; this one says
/// the request named no email at all, which no repeated read will change. It is also raised rather than returned for
/// that reason: text that names nothing leaves no email to report an outcome against.
/// </para>
/// <para>
/// The message carries no payload of its own, because the only thing there would be to name is the refused text — and
/// that text is the caller's own input, on its way into a client-readable result and the log beside it.
/// </para>
/// </remarks>
public sealed class StoredEmailIdentifierMalformedException : MailFathomException
{
    /// <summary>Initializes the failure for text that names no stored email.</summary>
    public StoredEmailIdentifierMalformedException()
        : base("The email identifier is not one this system issues.")
    {
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.StoredEmailIdentifierMalformed;
}
