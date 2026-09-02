// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Delivery;

/// <summary>States how far along its submission sequence one recorded outgoing email has durably reached.</summary>
/// <remarks>
/// <para>
/// The members are the stages an SMTP submission actually has rather than a generic queued, running, and done. That is
/// what makes the value usable for resumption: an attempt reads the stage and continues from it, and the one point at
/// which a repeat would deliver a second copy is recognized by the stage that precedes it rather than by inspecting a
/// mailbox afterwards, which cannot tell a message this system sent twice from one it sent once.
/// </para>
/// <para>
/// <see cref="TransmissionBegun" /> is the whole reason the enumeration exists. A crash immediately after the message
/// body went out and immediately before the server's answer was read leaves an attempt that cannot say whether the
/// message was delivered, and the two possible answers call for opposite actions. Writing the stage before the
/// transmission rather than after it narrows that window to something a restart can recognize; what is then done with a
/// record found here is not decided by this type.
/// </para>
/// <para>
/// A message is <see cref="Sent" /> only from <see cref="TransmissionBegun" />, which is the crash-safety invariant made
/// structural: nothing is recorded as delivered that never recorded a transmission it could have been delivered by.
/// </para>
/// <para>
/// The stage is stored as its name so it stays readable in an ad-hoc audit query and survives any later reordering of
/// this enum, which is the same reason the mutation stage and the stored content availability are stored that way.
/// </para>
/// </remarks>
public enum OutgoingEmailStage
{
    /// <summary>The intent and the message are durable, and no SMTP command has been issued for them.</summary>
    /// <remarks>
    /// Every send starts here, and an attempt from here is safe because nothing has reached a submission server. A
    /// record stays here across an attempt that failed before the body went out, however far into the envelope it got:
    /// what a repeat of that costs is a connection, never a second delivery.
    /// </remarks>
    Recorded = 0,

    /// <summary>The message body has begun to go out, and the server's answer to it has not been read.</summary>
    /// <remarks>
    /// This is the one stage an attempt may not act on. A message transmitted twice is a second delivery rather than a
    /// repeat of the first, and unlike a duplicated local copy it cannot be withdrawn from the recipient it reached, so
    /// a record found here is reported as an unknown outcome rather than re-sent.
    /// </remarks>
    TransmissionBegun = 1,

    /// <summary>The server accepted the message for every recipient it had accepted, and nothing more is owed.</summary>
    Sent = 2,

    /// <summary>The message will not be offered again, and the failure that ended it is on the record.</summary>
    /// <remarks>
    /// It covers a server's permanent refusal and every other way a send stops being attempted, which is what keeps a
    /// failed message visible instead of pending forever. Which of them ended it is read from the recorded failure and
    /// from the per-recipient outcomes rather than from a stage of its own.
    /// </remarks>
    Refused = 3,

    /// <summary>The send was withdrawn before it was delivered, and nothing was transmitted on its behalf.</summary>
    Cancelled = 4,
}
