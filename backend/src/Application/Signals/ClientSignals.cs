// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Signals;

/// <summary>The one place a signal is raised, folded, and handed to every registered channel.</summary>
/// <remarks>
/// <para>
/// Raising is the caller's whole obligation and it never fails: the work a signal describes is already committed by the
/// time one is raised, so a delivery that could not happen is the client's own interval to close rather than a reason
/// to fail a synchronization run. <see cref="Publish" /> therefore returns without awaiting anything.
/// </para>
/// <para>
/// <b>Signals are folded per scope over a short window.</b> A run that commits forty messages into one folder is one
/// arrival to the person who was away from the screen, exactly as it is one notification, so the statements are held
/// for <see cref="FoldingWindow" /> and the fold is what a channel is handed. The scope is the owner, the kind, and the
/// place, so two folders' arrivals stay two statements — folding them into one would leave a client told that mail
/// arrived without being told where to look.
/// </para>
/// <para>
/// The window is measured against an injected <see cref="TimeProvider" />, so a test decides it rather than waiting it
/// out, and <see cref="DrainAsync" /> is how a caller — a test, or this type's own disposal — waits for what a tick
/// started.
/// </para>
/// <para>
/// A deployment with no channel registered buffers nothing at all and starts no timer, which is what keeps a service
/// serving no client from holding state about signals nobody can receive.
/// </para>
/// </remarks>
public sealed class ClientSignals : IAsyncDisposable
{
    /// <summary>How long statements about one scope are held before the fold of them is delivered.</summary>
    /// <remarks>
    /// Long enough that a run committing a folder's worth of mail produces one statement rather than one per batch, and
    /// short enough that a person watching the screen sees the change as it happens rather than as a delay they notice.
    /// It is a constant rather than a setting because it is a property of what a person perceives rather than of a
    /// deployment.
    /// </remarks>
    public static readonly TimeSpan FoldingWindow = TimeSpan.FromMilliseconds(500);

    /// <summary>The most scopes held at once before a further one is delivered unfolded.</summary>
    /// <remarks>
    /// A bound rather than a tuning value: the buffer is keyed by owner, kind, and place, so a deployment serving many
    /// owners whose accounts all run at once would otherwise grow it without limit. A scope arriving past the bound is
    /// delivered straight away, which is the accurate degradation — more statements rather than lost ones.
    /// </remarks>
    public const int MostFoldedScopes = 1_000;

    private readonly IReadOnlyList<IClientSignalChannel> channels;
    private readonly TimeProvider timeProvider;
    private readonly Lock gate = new();
    private readonly Dictionary<ClientSignalScope, PendingSignal> pending = new();
    private readonly ITimer? timer;

    private Task delivering = Task.CompletedTask;

    /// <summary>Initializes the publisher over whatever channels this deployment registered.</summary>
    /// <param name="channels">Every registered delivery channel, which is empty on a deployment that serves no client.</param>
    /// <param name="timeProvider">Measures the folding window.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public ClientSignals(IEnumerable<IClientSignalChannel> channels, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.channels = [.. channels];
        this.timeProvider = timeProvider;

        // No channel means nothing to deliver to, so the buffer and the tick would both be state about work that cannot
        // happen. A deployment serving no client is the ordinary case rather than an edge one.
        this.timer = this.channels.Count == 0
            ? null
            : timeProvider.CreateTimer(_ => this.DeliverPending(everythingHeld: false), state: null, FoldingWindow, FoldingWindow);
    }

    /// <summary>Gets whether anything is registered to deliver a signal, which is what makes raising one worth the fold.</summary>
    public bool Reaches => this.channels.Count > 0;

    /// <summary>Says that something changed, without waiting for anyone to be told.</summary>
    /// <param name="signal">What changed and for whom.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="signal" /> is <see langword="null" />.</exception>
    /// <remarks>A signal in a scope already held folds into what is held; one in a new scope starts a window of its own, unless the buffer is already at <see cref="MostFoldedScopes" />, in which case it is delivered immediately rather than dropped.</remarks>
    public void Publish(ClientSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);

        if (!this.Reaches)
        {
            return;
        }

        var due = this.timeProvider.GetUtcNow() + FoldingWindow;
        var immediate = false;

        lock (this.gate)
        {
            var scope = signal.Scope;

            if (this.pending.TryGetValue(scope, out var held))
            {
                this.pending[scope] = held with { Signal = held.Signal.FoldedWith(signal) };
            }
            else if (this.pending.Count >= MostFoldedScopes)
            {
                immediate = true;
            }
            else
            {
                this.pending[scope] = new PendingSignal(signal, due);
            }
        }

        if (immediate)
        {
            this.Deliver([signal]);
        }
    }

    /// <summary>Waits for whatever the last elapsed window started to have been delivered.</summary>
    /// <returns>A task that completes when no delivery this publisher started is still running.</returns>
    /// <remarks>Reading the field under the lock rather than awaiting it directly, because a delivery that completes while it is being awaited replaces the field, and the caller wants the one that was current when it asked.</remarks>
    public Task DrainAsync()
    {
        lock (this.gate)
        {
            return this.delivering;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (this.timer is not null)
        {
            await this.timer.DisposeAsync();
        }

        this.DeliverPending(everythingHeld: true);

        await this.DrainAsync();
    }

    /// <summary>Delivers the windows that have elapsed, or every one being held where the publisher is shutting down.</summary>
    /// <param name="everythingHeld">Whether a window that has not elapsed is delivered anyway, which is what disposal wants and a tick never does.</param>
    /// <remarks>Disposal ignores the window because the alternative is dropping what a run had already committed: a statement delivered early is one a client acts on, and one dropped at shutdown is a client left reading yesterday until its own interval comes round.</remarks>
    private void DeliverPending(bool everythingHeld)
    {
        var now = this.timeProvider.GetUtcNow();
        ClientSignal[] due;

        lock (this.gate)
        {
            due =
            [
                .. this.pending.Values
                    .Where(held => everythingHeld || held.DueAt <= now)
                    .Select(static held => held.Signal),
            ];

            foreach (var signal in due)
            {
                this.pending.Remove(signal.Scope);
            }
        }

        if (due.Length > 0)
        {
            this.Deliver(due);
        }
    }

    /// <summary>Chains a delivery behind whatever is already running, so channels are never handed two folds at once.</summary>
    /// <remarks>The chain is what makes the order a client sees the order the folds were composed in; a fan-out started beside the previous one would let a later statement about a scope overtake an earlier one.</remarks>
    private void Deliver(IReadOnlyList<ClientSignal> signals)
    {
        lock (this.gate)
        {
            this.delivering = this.delivering.ContinueWith(
                _ => this.PublishToEveryChannelAsync(signals),
                CancellationToken.None,
                TaskContinuationOptions.DenyChildAttach,
                TaskScheduler.Default).Unwrap();
        }
    }

    /// <summary>Hands every fold in one window to every channel, absorbing whatever a channel could not do.</summary>
    /// <remarks>
    /// <para>
    /// A channel that could not deliver is not this publisher's failure to report: the work every signal describes is
    /// already committed, every raise site treats delivery as an optimization, and the client's own interval closes the
    /// gap. The channel is where such a failure is logged, this boundary carrying no logger by design. What the
    /// continuation does is read the aggregate, so an undelivered signal is an absorbed failure rather than an
    /// unobserved task exception the runtime reports out of a finalizer.
    /// </para>
    /// <para>
    /// The folds in one window are handed over together rather than one at a time, because two folds in one window are
    /// two scopes by construction — a second statement about one scope folds into the first — so nothing here needs an
    /// order. Order between windows is what the chain in <see cref="Deliver" /> keeps.
    /// </para>
    /// </remarks>
    private Task PublishToEveryChannelAsync(IReadOnlyList<ClientSignal> signals) =>
        Task.WhenAll(
                signals.SelectMany(signal =>
                    this.channels.Select(channel => channel.PublishAsync(signal, CancellationToken.None))))
            .ContinueWith(
                static delivered =>
                {
                    _ = delivered.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.DenyChildAttach,
                TaskScheduler.Default);

    /// <summary>One scope's fold so far, and when the window holding it closes.</summary>
    private readonly record struct PendingSignal(ClientSignal Signal, DateTimeOffset DueAt);
}
