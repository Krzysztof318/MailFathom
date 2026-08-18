// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Maintenance;

/// <summary>A re-derivation of one scope's stored mail somebody asked for, and how far the jobs carrying it have come.</summary>
/// <remarks>
/// <para>
/// Durable because it has to survive the process. The walk spans as many job attempts as the scope needs, so a restart
/// in the middle of one must resume where the passes committed rather than at the beginning of a mailbox — and a
/// request that arrived seconds before a shutdown must still be a request afterwards.
/// </para>
/// <para>
/// One run per scope, which is what makes a second request an answer rather than a second walk over one mailbox. There
/// is no queue behind it: asking twice for the same thing is asking once, and the reply says the run is already under
/// way. Two scopes are two runs, because an operator refreshing one folder must not be told about another's walk.
/// </para>
/// <para>
/// The counts accumulate across the whole run rather than describing the last pass, which is the figure an operator
/// asking "how far has it got" wants; the position the next pass resumes from is not here, because that is the walk's
/// own cursor and it exists whether a run asked for it or not.
/// </para>
/// <para>
/// Every field is either MailFathom's own identity for something, a count, or an instant. Nothing derived from a
/// message belongs in a record an operator reads to find out what their instance is doing.
/// </para>
/// </remarks>
public sealed record StoredMailRederivationRun
{
    /// <summary>Gets the identity of this run, which the jobs carrying it are keyed by.</summary>
    public required StoredMailRederivationRunId RunId { get; init; }

    /// <summary>Gets the account, and the one folder of it, whose stored mail the run re-reads.</summary>
    public required StoredMailScope Scope { get; init; }

    /// <summary>Gets when the run was asked for.</summary>
    public required DateTimeOffset RequestedAt { get; init; }

    /// <summary>Gets how many segments of the run have been enqueued, which is also the key the next one is enqueued under.</summary>
    /// <remarks>
    /// One attempt carries as many bounded passes as it is given and hands the rest to a job of its own, so a run over a
    /// large mailbox is a chain of segments rather than one job held open for as long as the mailbox takes. The count is
    /// what makes each link's idempotency key its own: the same segment enqueued twice — by an attempt whose lease had
    /// already moved on, or by one whose completion was not recorded — is answered with the job that is already there.
    /// </remarks>
    public required int SegmentCount { get; init; }

    /// <summary>Gets how many stored emails the run re-read and wrote metadata for.</summary>
    public int RederivedEmailCount { get; init; }

    /// <summary>Gets how many stored emails the run stepped over because no reader could parse their MIME.</summary>
    public int UnreadableEmailCount { get; init; }

    /// <summary>Gets how many stored emails the run stepped over because their raw MIME is no longer stored.</summary>
    public int MissingContentEmailCount { get; init; }

    /// <summary>Gets when the run reached the end of its scope, or <see langword="null" /> while it has not.</summary>
    /// <remarks>
    /// It is written by the attempt that found no mail left, so a run whose job is waiting in the queue, running, or
    /// dead-lettered is outstanding alike. Which of those it is, is a question about the queue rather than about the
    /// run, and <c>mfctl jobs dead-letters</c> is where it is answered.
    /// </remarks>
    public DateTimeOffset? EndedAt { get; init; }

    /// <summary>Gets whether the run is still waiting to be carried further.</summary>
    public bool IsOutstanding => this.EndedAt is null;
}
