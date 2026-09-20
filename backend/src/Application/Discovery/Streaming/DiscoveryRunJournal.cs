// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Signals;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Discovery.Streaming;

/// <summary>Writes one run's answer down as it is composed, and says over the hub how far it has got.</summary>
/// <remarks>
/// <para>
/// A run outlives the request that asked the question, and above one replica it outlives the process too: the client
/// reading it is routed wherever the load balancer likes. So the run's output is written into PostgreSQL where it is
/// produced and read back from there over an ordinary route, which is
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0035-delivering-a-running-ai-answer-from-a-persisted-run-by-cursor-signal-and-re-read.md">ADR 0035</see>.
/// This is the writing half: the execution holds one of these and appends to it, and nothing about the reading half
/// passes through here.
/// </para>
/// <para>
/// <strong>The signal says where to read, never what was read.</strong> Each write is announced to the owner's group
/// as the run and the sequence it reached, so every screen that person has open is told rather than only the one that
/// asked. It carries no part of the answer, because the backplane a deployment may point that fan-out at is one an
/// operator is permitted to have somebody else run — and a block quotes mail. A signal that never arrives costs
/// nothing: the row is the guarantee and the client's own re-read is what reaches it.
/// </para>
/// <para>
/// <strong>It also owns the token a stop arrives on.</strong> A stop reaches a run two ways and both end here: a
/// request that landed on this replica asks it directly, and one that landed on another replica is recorded against
/// the run, which this discovers the first time the store refuses a write. Either way the token is cancelled, the
/// execution abandons the provider call and the retrieval it was waiting on, and the ending the run then writes carries
/// the counts that execution actually spent.
/// </para>
/// </remarks>
public sealed class DiscoveryRunJournal : IDisposable
{
    private readonly IDiscoveryRunStore store;
    private readonly ClientSignals signals;
    private readonly TimeProvider timeProvider;
    private readonly CancellationTokenSource stopping = new();

    /// <summary>Initializes the journal of one run that has already been opened in the store.</summary>
    /// <param name="id">The identifier the run is addressed by.</param>
    /// <param name="user">Whose mail the run reads, which is who may read what it writes and whose screens are told.</param>
    /// <param name="store">Where the run's events are written.</param>
    /// <param name="signals">Where the advance is announced, which is best effort and never the guarantee.</param>
    /// <param name="timeProvider">Stamps each write, which is what the retention windows are measured from.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public DiscoveryRunJournal(
        DiscoveryRunId id,
        MailUserId user,
        IDiscoveryRunStore store,
        ClientSignals signals,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.Id = id;
        this.User = user;
        this.store = store;
        this.signals = signals;
        this.timeProvider = timeProvider;
    }

    /// <summary>Gets the identifier the run is addressed by.</summary>
    public DiscoveryRunId Id { get; }

    /// <summary>Gets whose mail the run reads.</summary>
    public MailUserId User { get; }

    /// <summary>Gets the token a stop reaches the execution through, which the run links its own cancellation to.</summary>
    public CancellationToken Stopping => this.stopping.Token;

    /// <summary>Gets whether this execution has written the run's ending, after which it writes nothing further.</summary>
    /// <remarks>This execution's own reading rather than the run's: a run the store has forgotten, or one another replica ended, is over whatever this says.</remarks>
    public bool HasEnded { get; private set; }

    /// <summary>Writes one event, which the store places in the run and stamps with its sequence.</summary>
    /// <param name="event">What happened, carrying neither a run nor a sequence of its own.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the event was written; <see langword="false" /> when the run is over and this execution should stop.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="event" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// A refusal is the run being over rather than a failure to retry: it has been asked to stop, it has already
    /// published an ending, it has no room left for anything but one, or it has been forgotten. All four mean the same
    /// thing to an execution, so the token is cancelled here and the caller ends the run rather than deciding which it
    /// was.
    /// </para>
    /// <para>
    /// The write is deliberately not bounded by the request that started the run — there is no such request left by the
    /// time most of these happen — and the ending is written even while the deployment is stopping, because a run left
    /// pending is one a client watches until the ceiling forgets it.
    /// </para>
    /// </remarks>
    public async Task<bool> AppendAsync(DiscoveryRunEvent @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var sequence = await this.store.AppendAsync(
            this.Id,
            @event,
            this.timeProvider.GetUtcNow(),
            cancellationToken);

        if (sequence is not { } reached)
        {
            this.RequestStop();

            return false;
        }

        this.HasEnded = @event.EndsTheRun;

        this.signals.Publish(ClientSignal.DiscoveryRunAdvanced(this.User, this.Id, reached));

        return true;
    }

    /// <summary>Asks the execution to stop, so it makes no further provider call and abandons the retrieval it is waiting on.</summary>
    /// <remarks>
    /// What it stops is the spending rather than the watching: everything the run has already written stays written,
    /// and the execution publishes the ending that names the stop. A run that has already ended is asked harmlessly,
    /// because whoever asked could not have known it finished a moment earlier.
    /// </remarks>
    public void RequestStop()
    {
        // A journal disposed between a stop request reaching this replica and here is one whose execution has already
        // finished, which reads as nothing to stop rather than as a fault.
        try
        {
            this.stopping.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <inheritdoc />
    public void Dispose() => this.stopping.Dispose();
}
