// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Mail.Delivery.Tracking;

/// <summary>The failure raised when a question or an instruction about one queued send is not answered at all.</summary>
/// <remarks>
/// <para>
/// The three refusals it carries are the whole of what these use cases decline, and none says anything about the
/// message. One means the text names no send this system could have issued; one means the caller may not be told a
/// record exists; the last means the record exists, the caller queued it, and the moment in which it could still have
/// been withdrawn has passed.
/// </para>
/// <para>
/// The messages name nothing about the send. A recipient, a subject, and an account are all things a caller learns
/// from the answer it is entitled to rather than from a refusal it is not.
/// </para>
/// </remarks>
public sealed class QueuedSendRefusedException : MailFathomException
{
    private QueuedSendRefusedException(MailFathomErrorCode errorCode, string clientSafeMessage)
        : base(clientSafeMessage) => this.ErrorCode = errorCode;

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode { get; }

    /// <summary>Reports text that names no send this system could have issued an identifier for.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// It is separate from a send the caller may not be told about, because the two are different mistakes: this one is
    /// true whatever this deployment has queued, so answering it as a send nobody holds would tell a caller its own
    /// malformed argument was somebody else's record. It carries no payload, since the only thing to name would be the
    /// refused text, which is the caller's own input on its way into a client-readable result.
    /// </remarks>
    public static QueuedSendRefusedException IdentifierMalformed() => new(
        MailFathomErrorCode.OutgoingEmailIdentifierMalformed,
        "The queued message identifier is not one this system issues.");

    /// <summary>Reports a send the caller may not be told about, whether or not one exists.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// A record another principal queued and a record nobody queued are one answer deliberately. A caller able to tell
    /// them apart would learn, from an identifier alone, that this mailbox sent something — which is the enumeration
    /// this surface refuses to offer, reached one guess at a time instead of through a listing.
    /// </remarks>
    public static QueuedSendRefusedException NotFound() => new(
        MailFathomErrorCode.OutgoingEmailNotFound,
        "No message this caller queued is held under that identifier.");

    /// <summary>Reports a send that can no longer be withdrawn.</summary>
    /// <returns>The failure to raise.</returns>
    /// <remarks>
    /// It states what is true of every send it covers — nothing was withdrawn — rather than which of the three states
    /// the record is in, because the record is read for that and a refusal that guessed would be a second account of
    /// the same fact.
    /// </remarks>
    public static QueuedSendRefusedException NoLongerCancellable() => new(
        MailFathomErrorCode.OutgoingEmailNoLongerCancellable,
        "This message can no longer be withdrawn: it is being transmitted, has been transmitted, or was already given up on.");
}
