// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Discovery.Streaming;

/// <summary>The runs this replica is executing, so a stop that lands here reaches the work rather than only the record.</summary>
/// <remarks>
/// <para>
/// A run's output is rows and its bounds are the deployment's, so almost nothing about a run is per process any more.
/// One thing still is: the run executes where it was started, and the cancellation that abandons its provider call and
/// its retrieval is an object in that process. This holds those, and nothing else.
/// </para>
/// <para>
/// <strong>It is not how a stop is guaranteed to arrive.</strong> A stop request lands on whichever replica the load
/// balancer chose, so what makes it reach the work is the record against the run: the executing replica is refused the
/// next event it writes and cancels itself. Asking here as well is what makes a stop immediate on the replica that
/// happens to hold the run, rather than a provider call late.
/// </para>
/// <para>
/// A run is registered for exactly as long as its execution lasts, which is what keeps this bounded without a sweep of
/// its own: the launcher releases it in the same place it ends the run, and a process that went away holds nothing
/// because it holds nothing at all.
/// </para>
/// </remarks>
public sealed class ExecutingDiscoveryRuns
{
    private readonly ConcurrentDictionary<DiscoveryRunId, DiscoveryRunJournal> executing = new();

    /// <summary>Gets how many runs this replica is executing.</summary>
    public int Count => this.executing.Count;

    /// <summary>Records that this replica has begun executing a run.</summary>
    /// <param name="journal">The run's journal, which is what a stop reaches its execution through.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="journal" /> is <see langword="null" />.</exception>
    public void Register(DiscoveryRunJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);

        this.executing[journal.Id] = journal;
    }

    /// <summary>Records that this replica has finished with a run, after which a stop reaches nothing here.</summary>
    /// <param name="id">The run that has finished executing.</param>
    public void Release(DiscoveryRunId id) => this.executing.TryRemove(id, out _);

    /// <summary>Asks a run this user started and this replica is executing to stop, where it is one.</summary>
    /// <param name="id">The run the caller is stopping.</param>
    /// <param name="user">The user the caller was admitted for.</param>
    /// <returns><see langword="true" /> when this replica was executing that run and has asked it to stop.</returns>
    /// <remarks>
    /// Checked against the user for the reason every other lookup of a run is: an identifier is a bearer value that
    /// travelled to a client and back, so somebody else's run is not reachable through it. Answering
    /// <see langword="false" /> says nothing about whether the run exists — it may be executing on another replica —
    /// which is why the caller's answer to the client comes from the record rather than from here.
    /// </remarks>
    public bool TryRequestStop(DiscoveryRunId id, UserId user)
    {
        if (!this.executing.TryGetValue(id, out var journal) || journal.User != user)
        {
            return false;
        }

        journal.RequestStop();

        return true;
    }
}
