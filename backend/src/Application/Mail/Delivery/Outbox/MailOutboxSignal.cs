// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Mail.Delivery.Outbox;

/// <summary>Carries the news that an account has something to send, from whoever wrote it down to whoever delivers it.</summary>
/// <remarks>
/// <para>
/// It exists because of latency and nothing else. Everything outstanding is already drained by the account's own
/// synchronization run, which is what makes the outbox correct without this; what a run cannot do is leave promptly,
/// and a message somebody authored — or a tool call that answered with a queued identifier — must not wait behind a
/// mailbox scan. So this is the fast path and the run is the guarantee, which is why a signal that is never delivered
/// delays a send rather than losing one.
/// </para>
/// <para>
/// The queue is bounded, and what it holds is accounts rather than messages. An account already signalled is not
/// signalled again, so a hundred messages written at once produce one pass over that account's outbox instead of a
/// hundred, and the depth cannot grow past the number of configured accounts however much is enqueued. That is the
/// backpressure: a signal that finds the queue full is refused rather than queued, the caller is told so, and the
/// account's next run picks the work up.
/// </para>
/// <para>
/// Writing to it never blocks and never waits, because the caller is finishing an authored send: making an operator's
/// tool call wait on a background queue would turn a bounded queue into a bounded API.
/// </para>
/// </remarks>
public sealed class MailOutboxSignal
{
    private readonly Channel<MailAccountIdentity> accounts;
    private readonly HashSet<MailAccountIdentity> pending = [];
    private readonly Lock gate = new();

    /// <summary>Creates the queue at the depth this deployment allows it.</summary>
    /// <param name="capacity">The greatest number of accounts that may be waiting for a pass at once.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="capacity" /> is not positive.</exception>
    public MailOutboxSignal(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        // Waiting is the mode a refusal is reportable in: a full queue makes TryWrite answer false, where either
        // dropping mode answers true and loses the signal without telling anybody. Nothing here ever waits, because
        // nothing calls WriteAsync. One reader, because one loop takes the passes and the ceiling on work in flight is
        // the pass's own.
        this.accounts = Channel.CreateBounded<MailAccountIdentity>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
            });
    }

    /// <summary>Gets how many accounts are waiting for a pass.</summary>
    public int Depth => this.accounts.Reader.Count;

    /// <summary>Says that an account has something outstanding to deliver.</summary>
    /// <param name="account">The account whose outbox is worth a pass, named by its owner and its identifier.</param>
    /// <returns><see langword="true" /> when the account is queued for a pass or was already queued for one; <see langword="false" /> when the queue was full and the signal was refused.</returns>
    /// <remarks>
    /// An account already waiting is reported as signalled, because it is: the pass it is waiting for reads the outbox
    /// rather than the signal, so it will find whatever was written between the two calls.
    /// </remarks>
    public bool Signal(MailAccountIdentity account)
    {
        lock (this.gate)
        {
            if (!this.pending.Add(account))
            {
                return true;
            }

            if (this.accounts.Writer.TryWrite(account))
            {
                return true;
            }

            // The queue refused it, so nothing will take the account out of the set; leaving it there would suppress
            // every later signal for that account until the process restarted.
            this.pending.Remove(account);

            return false;
        }
    }

    /// <summary>Reads the accounts to take a pass over, as they are signalled.</summary>
    /// <param name="cancellationToken">Stops the enumeration when the host stops.</param>
    /// <returns>Each signalled account, once per signal that landed.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    /// <remarks>
    /// <para>
    /// An account is removed from the pending set as it is handed out rather than when its pass finishes, so a message
    /// written while that pass is running signals the account again instead of being absorbed into a pass that had
    /// already read the outbox.
    /// </para>
    /// <para>
    /// The token is read before every account rather than only when there is nothing to read. A channel observes
    /// cancellation where it would have to wait, so a queue that is refilled as fast as it is drained — which is what a
    /// backlog large enough to fill every batch produces — would otherwise be enumerated straight past the host's
    /// shutdown, and the loop reading it would never end.
    /// </para>
    /// </remarks>
    public async IAsyncEnumerable<MailAccountIdentity> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var account in this.accounts.Reader.ReadAllAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (this.gate)
            {
                this.pending.Remove(account);
            }

            yield return account;
        }
    }
}
