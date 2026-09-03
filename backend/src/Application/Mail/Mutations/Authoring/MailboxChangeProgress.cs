// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations.Authoring;

/// <summary>Where one change somebody authored has got to, as the caller that authored it asks.</summary>
/// <param name="RecordId">The record this answers for, which is what the authoring call handed back.</param>
/// <param name="StoredEmailId">The email the change is about, so a caller holding several records can put each beside its message.</param>
/// <param name="Mutation">The change that was asked for, under the name every log line and counter uses for it.</param>
/// <param name="Lifecycle">Whether the change is waiting, on its way, done, stuck, or withdrawn.</param>
/// <param name="IsOutcomeUnknown">Whether the one command that may never be issued twice went out and its answer never came back.</param>
/// <param name="AttemptCount">How many times the change has been attempted, which is what says a retried change is making no progress.</param>
/// <param name="LastFailure">The code identifying what the last attempt ended in, or <see langword="null" /> while none has failed.</param>
/// <param name="RecordedAt">When the change was written down.</param>
/// <param name="StageChangedAt">When it last moved, which is what says how long a stuck change has been stuck.</param>
/// <remarks>
/// <para>
/// The unknown outcome is reported rather than folded into the lifecycle, because it is the one state a person has to
/// act on rather than wait through. A relocation a server without <c>MOVE</c> carries is a copy followed by a delete,
/// and a placement whose acknowledgement never arrived is the moment where the message may be in both folders or in
/// neither — so it is reported as itself instead of appearing as an ordinary change still converging.
/// </para>
/// <para>
/// Nothing here is mail content. A record identity, a local email identity the caller already holds, a mutation name, a
/// lifecycle name, a count, an error code, and two timestamps are all MailFathom's own words for its own work.
/// </para>
/// </remarks>
public sealed record MailboxChangeProgress(
    MailboxMutationRecordId RecordId,
    StoredEmailId StoredEmailId,
    MailboxMutation Mutation,
    MailboxMutationLifecycle Lifecycle,
    bool IsOutcomeUnknown,
    int AttemptCount,
    MailFathomErrorCode? LastFailure,
    DateTimeOffset RecordedAt,
    DateTimeOffset StageChangedAt)
{
    /// <summary>Describes where one durable record stands.</summary>
    /// <param name="record">The record as it was read.</param>
    /// <returns>The progress a caller reads.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record" /> is <see langword="null" />.</exception>
    public static MailboxChangeProgress Of(MailboxMutationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new MailboxChangeProgress(
            record.Id,
            record.Request.StoredEmailId,
            record.Request.Mutation,
            record.Lifecycle,
            record.HasUnknownOutcome,
            record.AttemptCount,
            record.LastFailure,
            record.RecordedAt,
            record.StageChangedAt);
    }
}
