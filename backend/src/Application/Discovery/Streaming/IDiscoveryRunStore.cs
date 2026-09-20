// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Discovery.Streaming;

/// <summary>Where the deployment holds the Discover runs it is executing and the answers they have written so far.</summary>
/// <remarks>
/// <para>
/// A run's output is durable rather than held in the process that composed it, which is what makes it reachable from
/// every replica: the client reading it is routed wherever the load balancer likes, its screen may be a second one the
/// same person opened, and a rolling upgrade moves it between builds.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0035-delivering-a-running-ai-answer-from-a-persisted-run-by-cursor-signal-and-re-read.md">ADR 0035</see>
/// is that decision, and this is the seam it is applied through.
/// </para>
/// <para>
/// <strong>What it holds is mail-derived and sensitive throughout.</strong> A block quotes somebody's correspondence
/// and a citation names the message it was drawn from, so nothing read or written here reaches a log, a span, a metric,
/// or a failure message, and the retention the run's own bounds fix is enforced by this store rather than left to a
/// reader to remember.
/// </para>
/// <para>
/// <strong>Every statement is one round trip and takes its own decision.</strong> Opening sweeps, counts, and inserts
/// together, so the bound is the deployment's rather than a number each replica finds room under separately; appending
/// assigns the sequence where the row is written, so nothing outside the database decides what a run has reached; and
/// reading stamps the run as used, so the retention window runs from the last read as the page says it does.
/// </para>
/// </remarks>
public interface IDiscoveryRunStore
{
    /// <summary>Opens a run for one user, unless that person is already running as many as they may.</summary>
    /// <param name="id">The identifier the run will be addressed by.</param>
    /// <param name="user">Whose mail the run will read, which is who may read what it writes.</param>
    /// <param name="now">The instant the run starts, which its retention and its ceiling are both measured from.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the run was opened; <see langword="false" /> when this person is at <see cref="DiscoveryRunBounds.MaximumConcurrentRunsPerUser" />.</returns>
    /// <remarks>The bound is one person's across the whole deployment rather than one process's, which is what lets a client be told a number that means something: what somebody may start no longer depends on which replica the start request reached.</remarks>
    Task<bool> TryOpenAsync(
        DiscoveryRunId id,
        MailUserId user,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Writes one event into a run, giving it the next place in that run.</summary>
    /// <param name="id">The run the event belongs to.</param>
    /// <param name="written">What happened, carrying neither a run nor a sequence of its own.</param>
    /// <param name="now">The instant the event was written.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The sequence the event was written under, or <see langword="null" /> when nothing was written.</returns>
    /// <remarks>
    /// <para>
    /// The sequence starts at <c>1</c> and never skips, because it is derived from what the run already holds inside
    /// the statement that writes the row rather than by anything counting outside the database.
    /// </para>
    /// <para>
    /// Nothing is written for a run that has been forgotten, for one that has already published its ending, or for an
    /// event that is not itself an ending where the run has been asked to stop or has no room left for anything but its
    /// ending. Each of those is the run being over rather than a fault, so the caller ends the run rather than retrying.
    /// </para>
    /// </remarks>
    Task<long?> AppendAsync(
        DiscoveryRunId id,
        DiscoveryRunEvent written,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Reads a run this user started, from a stated point, and records that it was read.</summary>
    /// <param name="id">The run the caller is reading.</param>
    /// <param name="user">The user the caller was admitted for.</param>
    /// <param name="afterSequence">The last sequence the caller already holds, or <c>0</c> to read the run from its beginning.</param>
    /// <param name="now">The instant of the read, which the retention window is measured from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The run's standing and everything after that point, or <see langword="null" /> where this user has no such run.</returns>
    /// <remarks>
    /// A sequence this run has not reached reads as the beginning rather than as a point to wait at, which is the safe
    /// direction: no client can hold one honestly, so what such a value means is a cursor belonging to some other run,
    /// and replaying costs a few events where honouring it would hand back a run missing everything before the number.
    /// A run belonging to somebody else is reported as no such run, exactly as one that never existed is.
    /// </remarks>
    Task<DiscoveryRunReading?> ReadAsync(
        DiscoveryRunId id,
        MailUserId user,
        long afterSequence,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Records that a run this user started has been asked to stop, so it composes nothing further.</summary>
    /// <param name="id">The run the caller is stopping.</param>
    /// <param name="user">The user the caller was admitted for.</param>
    /// <param name="now">The instant the stop was asked for.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the run was this user's and had not been forgotten; <see langword="false" /> otherwise.</returns>
    /// <remarks>
    /// <para>
    /// It records the request rather than writing the ending, because what a run spent is the executing replica's
    /// ledger to report and an ending composed anywhere else would carry a cost figure of nothing. The next event that
    /// replica tries to write is refused, which is how the request reaches it however it is routed, and the ending it
    /// then writes carries the true counts.
    /// </para>
    /// <para>
    /// A run that has already ended is reported as found and nothing is recorded, because whoever asked could not have
    /// known it finished a moment earlier — and reporting that as a failure would make a control that worked look
    /// broken.
    /// </para>
    /// </remarks>
    Task<bool> TryRequestStopAsync(
        DiscoveryRunId id,
        MailUserId user,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Removes the runs nobody can come back for, and the ones no execution can still be reporting.</summary>
    /// <param name="now">The instant both windows are measured against.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>How many runs were removed, which is a count and never anything about them.</returns>
    /// <remarks>
    /// Two windows rather than one, for the reason the run's bounds state: a run that has ended goes
    /// <see cref="DiscoveryRunBounds.RetentionAfterLastUse" /> after it was last read, and one that never reported at
    /// all goes at the longest a run may take and that window together — the replica executing it having gone away
    /// without ending it. Every event the run wrote goes with the run.
    /// </remarks>
    Task<int> RemoveForgottenAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
