// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Maintenance;

namespace MailFathom.Host.Api;

/// <summary>What a deployment is asked when stored mail is to be brought up to a newer release's properties.</summary>
/// <param name="Account">The account to act on, as the deployment's configuration names it.</param>
/// <param name="Folder">MailFathom's own alias for the one folder to act on, or nothing for every folder the account holds mail in.</param>
/// <remarks>
/// One request shape for both operations, because an operator names the same two things for either and a second record
/// differing in nothing would be two contracts to keep in agreement. Which operation is meant is the route, never a
/// field: a mistyped value must not be the difference between re-reading local bytes and pulling a mailbox over IMAP
/// again.
/// </remarks>
internal sealed record MailboxMaintenanceRequest(string? Account, string? Folder);

/// <summary>What a rewind of one scope would have the next synchronization runs read again.</summary>
/// <param name="Account">The account the assessment is about.</param>
/// <param name="Folder">The normalized alias it was narrowed to, or nothing when it covers the whole account.</param>
/// <param name="StoredEmailCount">How many stored emails the scope holds.</param>
/// <remarks>
/// A count and MailFathom's own names for things. It is what the scope holds rather than what a run would fetch,
/// because the difference between the two is only knowable from a mailbox session and this reads none.
/// </remarks>
internal sealed record MailboxRewindAssessmentResponse(string Account, string? Folder, int StoredEmailCount);

/// <summary>Which of a scope's folders held durable synchronization progress that a rewind discarded.</summary>
/// <param name="Account">The account the rewind ran against.</param>
/// <param name="Folder">The normalized alias it was narrowed to, or nothing when it covered the whole account.</param>
/// <param name="Folders">The aliases whose bindings held progress, ordered and without repeats.</param>
/// <remarks>
/// Aliases rather than remote paths, and no UID, timestamp, or modification sequence. What was discarded is named by
/// MailFathom's own configured names for folders, so an answer says which folders will be read afresh without
/// describing where the mail server keeps them.
/// </remarks>
internal sealed record MailboxRewindResponse(string Account, string? Folder, IReadOnlyList<string> Folders);

/// <summary>What asking for a re-derivation did, as the administrative endpoint serves it.</summary>
/// <param name="Started">Whether this request is what put the run in front of the scope.</param>
/// <param name="Queued">Whether the segment the run is on is waiting in the queue, whoever put it there.</param>
/// <param name="Run">The run the scope now has, which is the one already under way when nothing started.</param>
/// <remarks>
/// The three are reported together because a caller that asked twice needs all of them: the run is the answer either
/// way, whether this request started it tells "I have just begun a walk" from "one was already going", and whether the
/// work is queued is the one thing neither of the other two says — a deployment whose queue was full at that moment has
/// recorded the run and carried nothing, and asking again is what puts it in motion.
/// </remarks>
internal sealed record MailboxRederivationStartResponse(
    bool Started,
    bool Queued,
    MailboxRederivationRunResponse Run);

/// <summary>Where a scope's re-derivation stands, as the administrative endpoint serves it.</summary>
/// <param name="Account">The account the answer is about.</param>
/// <param name="Folder">The normalized alias the question was narrowed to, or nothing when it covers the whole account.</param>
/// <param name="Run">The run, or <see langword="null" /> where the scope has never been asked for one.</param>
/// <remarks>
/// A scope that has never been asked for a run is an outcome rather than a refusal, so the answer carries no run
/// instead of a <c>404</c>: the caller asked a question this deployment can answer, and the answer is that nothing has
/// been asked for.
/// </remarks>
internal sealed record MailboxRederivationStateResponse(
    string Account,
    string? Folder,
    MailboxRederivationRunResponse? Run);

/// <summary>One re-derivation of a scope's stored mail, as the administrative endpoint serves it.</summary>
/// <param name="Account">The account the run walks.</param>
/// <param name="Folder">The normalized alias it was narrowed to, or nothing when it covers the whole account.</param>
/// <param name="RequestedAt">When the run was asked for.</param>
/// <param name="IsOutstanding">Whether the run is still waiting to be carried further.</param>
/// <param name="RederivedEmailCount">How many stored emails the run has re-read and written metadata for.</param>
/// <param name="UnreadableEmailCount">How many carried MIME no reader could parse, which the run stepped over.</param>
/// <param name="MissingContentEmailCount">How many no longer had raw MIME to re-read.</param>
/// <param name="EndedAt">When the run reached the end of its scope, absent while it has not.</param>
/// <remarks>
/// Counts, instants, and MailFathom's own names for a mailbox and a folder. What was re-read is a number rather than a
/// list, because a list of what a deployment has just re-read would be a copy of the part of the mailbox the operator
/// asked it to refresh.
/// </remarks>
internal sealed record MailboxRederivationRunResponse(
    string Account,
    string? Folder,
    DateTimeOffset RequestedAt,
    bool IsOutstanding,
    int RederivedEmailCount,
    int UnreadableEmailCount,
    int MissingContentEmailCount,
    DateTimeOffset? EndedAt)
{
    /// <summary>Describes one run for the wire.</summary>
    /// <param name="run">The run as it stands.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="run" /> is <see langword="null" />.</exception>
    internal static MailboxRederivationRunResponse For(StoredMailRederivationRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new MailboxRederivationRunResponse(
            run.Scope.Account.Value,
            run.Scope.Folder?.Value,
            run.RequestedAt,
            run.IsOutstanding,
            run.RederivedEmailCount,
            run.UnreadableEmailCount,
            run.MissingContentEmailCount,
            run.EndedAt);
    }
}
