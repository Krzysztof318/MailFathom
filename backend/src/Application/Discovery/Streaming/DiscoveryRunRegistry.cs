// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Discovery.Streaming;

/// <summary>The runs this process is executing or still holding, addressable while a client can still come back for one.</summary>
/// <remarks>
/// <para>
/// A streamed run outlives the request that started it, so something has to hold it between the connection that asked
/// the question and the connection that reads the answer — including the second, third, and fourth of those, where a
/// network dropped. This is that, and it is in memory and per process on purpose: what it protects is a reconnection
/// inside minutes, and a restart ends every run it was holding anyway because the executing work went with it.
/// </para>
/// <para>
/// It is bounded in both directions. A run is refused once this process holds
/// <see cref="DiscoveryRunBounds.MaximumConcurrentRuns" />, so a client looping over the start route is told to wait
/// rather than filling memory; and a run that has ended and which nothing has touched for
/// <see cref="DiscoveryRunBounds.RetentionAfterLastUse" /> is forgotten, so a client that never came back costs nothing
/// for the life of the process. Nothing is held past <see cref="DiscoveryRunBounds.MaximumDuration" /> and that window
/// together, whether it ended or not, so a run whose execution never reported cannot occupy a slot indefinitely.
/// Forgetting happens on the way past rather than on a timer, for the reason the replay
/// store's own sweep does: a process holding no runs needs no sweeping, and a timer would keep it awake to prove it.
/// </para>
/// <para>
/// Every lookup is against an owner. A run holds one person's mail, and an identifier alone is a bearer value that
/// travelled to a client and back, so the owner the caller was admitted for decides what they may be shown — a run
/// belonging to somebody else is reported as no such run rather than as a refusal, which is the same answer a client
/// gets for one this process has already forgotten.
/// </para>
/// </remarks>
public sealed class DiscoveryRunRegistry
{
    private readonly ConcurrentDictionary<DiscoveryRunId, HeldRun> runs = new();
    private readonly object opening = new();
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the registry.</summary>
    /// <param name="timeProvider">The clock retention is judged against.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    public DiscoveryRunRegistry(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;
    }

    /// <summary>Gets how many runs this process is holding, executing or not, including any it has not yet forgotten.</summary>
    public int HeldCount => this.runs.Count;

    /// <summary>Opens a run for one owner, unless this process is already holding as many as it may.</summary>
    /// <param name="owner">Whose mail the run will read, which is who may read what it publishes.</param>
    /// <param name="journal">The run's journal when one was opened; otherwise <see langword="null" />.</param>
    /// <returns><see langword="true" /> when the run was opened; <see langword="false" /> when this process is at its bound.</returns>
    /// <remarks>
    /// <para>
    /// The bound is over the whole process rather than per owner, because what it protects is this process's memory.
    /// Sweeping first is what keeps a deployment answering after a burst: the runs a bound is measured against are the
    /// ones somebody may still come back for, never the ones nobody did.
    /// </para>
    /// <para>
    /// Counting and adding are one act, because two callers that each read a count of seven and then added would leave
    /// this process holding nine. Only opening takes it: a lookup reads the dictionary's own guarantees, and the bound
    /// is a number a client is told, so it has to be the number rather than approximately it.
    /// </para>
    /// </remarks>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the source passes to the held run; it is disposed when the registry forgets the run, and here when the entry was not taken.")]
    public bool TryOpen(MailOwnerId owner, [NotNullWhen(true)] out DiscoveryRunJournal? journal)
    {
        lock (this.opening)
        {
            this.ForgetRunsNobodyCameBackFor();

            if (this.runs.Count >= DiscoveryRunBounds.MaximumConcurrentRuns)
            {
                journal = null;

                return false;
            }

            var stopping = new CancellationTokenSource();
            var opened = new DiscoveryRunJournal(DiscoveryRunId.New(), owner, stopping.Token);

            if (!this.runs.TryAdd(opened.Id, new HeldRun(opened, stopping, this.timeProvider.GetUtcNow())))
            {
                // Unreachable while identifiers are drawn fresh under this lock, and handled rather than assumed away:
                // a run nothing holds is a run nothing will ever forget, so its source would be the one leak here.
                stopping.Dispose();
                journal = null;

                return false;
            }

            journal = opened;

            return true;
        }
    }

    /// <summary>Finds a run this owner started and this process is still holding.</summary>
    /// <param name="id">The run the caller is asking for.</param>
    /// <param name="owner">The owner the caller was admitted for.</param>
    /// <param name="journal">The run's journal when it is this owner's and still held; otherwise <see langword="null" />.</param>
    /// <returns><see langword="true" /> when the run was found; <see langword="false" /> when it belongs to somebody else, has been forgotten, or never existed.</returns>
    /// <remarks>Finding one is use: the retention window runs from here as well as from a publish, so a client that is reading is never forgotten out from under itself.</remarks>
    public bool TryFind(DiscoveryRunId id, MailOwnerId owner, [NotNullWhen(true)] out DiscoveryRunJournal? journal)
    {
        this.ForgetRunsNobodyCameBackFor();

        if (!this.runs.TryGetValue(id, out var held) || held.Journal.Owner != owner)
        {
            journal = null;

            return false;
        }

        this.runs[id] = held with { LastUsedAt = this.timeProvider.GetUtcNow() };
        journal = held.Journal;

        return true;
    }

    /// <summary>Stops a run this owner started, so it makes no further provider call and abandons the retrieval it is waiting on.</summary>
    /// <param name="id">The run the caller is stopping.</param>
    /// <param name="owner">The owner the caller was admitted for.</param>
    /// <returns><see langword="true" /> when the run was found and stopped; <see langword="false" /> when it belongs to somebody else, has been forgotten, or never existed.</returns>
    /// <remarks>
    /// Found the same way a read is, against the same owner, and for the same reason: an identifier is a bearer value
    /// that travelled to a client and back, so somebody else's run is reported as no such run rather than as a refusal.
    /// A run that has already ended is stopped successfully and nothing happens, because whoever asked could not have
    /// known it finished a moment earlier — and reporting that as a failure would make a control that worked look
    /// broken.
    /// </remarks>
    public bool TryStop(DiscoveryRunId id, MailOwnerId owner)
    {
        if (!this.TryFind(id, owner, out var journal) || !this.runs.TryGetValue(journal.Id, out var held))
        {
            return false;
        }

        // A run forgotten between the lookup above and here is one the source was already released for, which reads as
        // no such run rather than as a fault: nothing is executing to stop, and whoever asked could not have known.
        try
        {
            held.Stopping.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        return true;
    }

    /// <summary>Records that a run has finished executing, which is when its retention window starts.</summary>
    /// <param name="id">The run that has ended.</param>
    /// <remarks>
    /// Called by whatever executed the run rather than by the journal, because the window is about how long an answer
    /// stays fetchable and a run that has not ended is held on the wider one instead. A run this process has already
    /// forgotten is not an error here: nothing came back for it and nothing will.
    /// </remarks>
    public void MarkEnded(DiscoveryRunId id)
    {
        if (this.runs.TryGetValue(id, out var held))
        {
            this.runs[id] = held with { LastUsedAt = this.timeProvider.GetUtcNow() };
        }
    }

    /// <summary>Forgets the runs that have ended and nothing has come back for, and any this process cannot account for.</summary>
    /// <remarks>
    /// <para>
    /// A run still executing is not forgotten on the ordinary window, whatever it says. What bounds one of those is the
    /// longest a run may take, which ends it and stamps it; forgetting one while its provider call was outstanding
    /// would tell a client reconnecting that there is no such run while the run was still spending on their question.
    /// </para>
    /// <para>
    /// <strong>Nothing is held past the longest a run may take plus its retention, ended or not.</strong> Every run this
    /// process starts is stopped at that duration and ends there, so a run older than the two together is one whose
    /// execution never reported — a task that never ran, or a fault between opening the run and starting it. Without
    /// this it would occupy one of the process's slots for as long as the process lived, and eight such runs would leave
    /// a deployment refusing every question until it was restarted.
    /// </para>
    /// <para>
    /// On the way past rather than on a timer, and unthrottled rather than claimed for an interval: the dictionary holds
    /// at most <see cref="DiscoveryRunBounds.MaximumConcurrentRuns" /> entries, so a walk of it costs less than the
    /// bookkeeping that would decide whether to walk it — and a throttled sweep would leave a caller refused at the
    /// bound by runs that had already expired.
    /// </para>
    /// </remarks>
    private void ForgetRunsNobodyCameBackFor()
    {
        var now = this.timeProvider.GetUtcNow();

        foreach (var entry in this.runs)
        {
            var held = now - entry.Value.LastUsedAt;
            var forgettable = entry.Value.Journal.HasEnded
                ? held >= DiscoveryRunBounds.RetentionAfterLastUse
                : held >= DiscoveryRunBounds.MaximumDuration + DiscoveryRunBounds.RetentionAfterLastUse;

            if (forgettable && this.runs.TryRemove(entry))
            {
                // Forgetting is where the run's cancellation source is released, this being the point past which
                // nothing can stop the run or read it. Removing first is what keeps two sweepers from disposing one
                // source twice: only the caller whose removal took the entry disposes it.
                entry.Value.Stopping.Dispose();
            }
        }
    }

    /// <summary>One run, the source a stop reaches it through, and when anything last read or wrote it.</summary>
    /// <remarks>
    /// The source is held here rather than on the journal because this is what knows the run's lifetime: it opens the
    /// run, it is where a later request finds it to stop it, and it is what forgets the run — which is the one moment at
    /// which releasing the source is safe.
    /// </remarks>
    private sealed record HeldRun(
        DiscoveryRunJournal Journal,
        CancellationTokenSource Stopping,
        DateTimeOffset LastUsedAt);
}
