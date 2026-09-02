// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Synchronization.Reconciliation;

/// <summary>One locally stored occurrence a reconciliation window has selected to ask the server about.</summary>
/// <param name="StoredEmailId">The stable local identity the outcome is written against.</param>
/// <param name="Uid">The UID the server is asked about, within the folder and UIDVALIDITY the window was opened for.</param>
/// <param name="LastObservation">
/// What the previous reading of this occurrence's remote flags recorded, or <see langword="null" /> when no run has read
/// them yet.
/// </param>
/// <remarks>
/// <para>
/// The three values are everything reconciliation needs and deliberately nothing more. No subject, address, or fragment
/// of a message takes part in deciding whether an email still exists remotely or whether its flags have moved, so none
/// of it is read to decide it.
/// </para>
/// <para>
/// <paramref name="LastObservation" /> is what turns the flags the server reports into a change rather than a reading.
/// Absence is a state of its own here: an occurrence nobody has observed has no previous value to differ from, so its
/// first flag reading is the initial observation and never a change somebody made.
/// </para>
/// </remarks>
public sealed record StoredEmailAwaitingReconciliation(
    StoredEmailId StoredEmailId,
    ImapUid Uid,
    RemoteWritableFlagObservation? LastObservation);

/// <summary>The previous reading of the values a mutation may write on one occurrence, and when it was taken.</summary>
/// <param name="ObservedAt">When the values below were read from the server.</param>
/// <param name="IsSeen">Where the <c>\Seen</c> flag stood then.</param>
/// <param name="IsFlagged">Where the <c>\Flagged</c> flag stood then.</param>
/// <param name="Keywords">The keywords the occurrence carried then.</param>
/// <remarks>
/// <para>
/// The values travel with the moment because neither is meaningful alone. A value says whether that part of the message
/// has moved since; the moment says whether a change MailFathom made could still be the reason it moved, which is what
/// stops a mutation record answering for a mailbox somebody has had the chance to change since.
/// </para>
/// <para>
/// Exactly the three a <c>STORE</c> of MailFathom's may write are carried, and the other flags the same snapshot holds
/// are not. A previous reading exists here to answer whether a change was MailFathom's own, and no record can ever
/// account for <c>\Answered</c>, <c>\Draft</c>, or <c>\Deleted</c> — those are refused mutations, so reading their
/// earlier value into a window would be reading a comparison nothing can consume.
/// </para>
/// </remarks>
public sealed record RemoteWritableFlagObservation(
    DateTimeOffset ObservedAt,
    bool IsSeen,
    bool IsFlagged,
    RemoteEmailKeywords Keywords);
