// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Synchronization;

/// <summary>Ends an account's wait between synchronization runs, where something written here is waiting on that run.</summary>
/// <remarks>
/// <para>
/// It exists because of latency and nothing else, exactly as the outbox's own signal does. A mailbox change somebody
/// authored is written down durably, and the account's next run is what tells the mail server; that run is what makes
/// the change correct, and it happens whether or not anything here is called. What it cannot do is happen promptly —
/// the wait between runs is the configured interval, and where the account is watched in push mode that wait ends only
/// when the mail *server* reports something, which a change authored here is not. So a message somebody deleted stayed
/// under <i>moving to trash</i> until the interval was out.
/// </para>
/// <para>
/// A raise is therefore a hint rather than a queue entry: it is never awaited, never retried, and losing one delays a
/// change rather than dropping it. Nothing carries what the change was, because the run reads the records.
/// </para>
/// <para>
/// A raise arriving while nothing is waiting is kept, which is the case that has to be right rather than a corner: the
/// record is often written while the account is mid-run, and a raise dropped there would be a change waiting out the
/// whole of the next interval. It is kept per account rather than per change, so a hundred messages filed at once bring
/// one run forward instead of a hundred, and it is spent by the wait that takes it — a raise never survives into a
/// second wait, which would be a run brought forward for work already done.
/// </para>
/// </remarks>
public sealed class MailAccountRunSignal
{
    private readonly ConcurrentDictionary<MailAccountId, AccountWait> accounts = new();

    /// <summary>Says that an account has something written down that its next run should not wait an interval for.</summary>
    /// <param name="account">The account whose run is worth bringing forward.</param>
    public void BringForward(MailAccountId account) => this.WaitFor(account).BringForward();

    /// <summary>Registers the wait an account is about to make, so a raise can end it.</summary>
    /// <param name="account">The account about to wait.</param>
    /// <param name="stopping">Ends the wait for the other reason it ends — the host having stopped scheduling.</param>
    /// <returns>The wait, whose token the caller waits under and which the caller disposes when the wait is over.</returns>
    /// <remarks>
    /// A raise that arrived while nothing was registered is spent here, so the token comes back already cancelled and
    /// the wait ends without starting. That is what carries a change written during a run into the run after it rather
    /// than the one after that.
    /// </remarks>
    public Wait Register(MailAccountId account, CancellationToken stopping) =>
        this.WaitFor(account).Register(stopping);

    private AccountWait WaitFor(MailAccountId account) =>
        this.accounts.GetOrAdd(account, static _ => new AccountWait());

    /// <summary>One registered wait, as the thing that ends it.</summary>
    /// <remarks>
    /// Nested because what it holds is one account's registration, which is this class's own state and nothing another
    /// type has business naming. Telling the two endings apart is the caller's and it is one question: a wait that
    /// ended while scheduling continues ended because something local asked for the run.
    /// </remarks>
    public sealed class Wait : IDisposable
    {
        private readonly AccountWait waiting;
        private readonly CancellationTokenSource ending;

        internal Wait(AccountWait waiting, CancellationTokenSource ending)
        {
            this.waiting = waiting;
            this.ending = ending;
        }

        /// <summary>Gets the token the wait is made under, cancelled by a raise or by whatever it was linked to.</summary>
        public CancellationToken Token => this.ending.Token;

        /// <inheritdoc />
        /// <remarks>
        /// Taking the registration off is what makes the next raise a kept one rather than a cancellation of a source
        /// nobody is waiting on — which would be a raise spent on nothing and a change waiting out the interval it was
        /// raised to avoid.
        /// </remarks>
        public void Dispose()
        {
            this.waiting.Unregister(this.ending);
            this.ending.Dispose();
        }
    }

    /// <summary>One account's registered wait and whether a raise is standing unspent against it.</summary>
    internal sealed class AccountWait
    {
        private readonly Lock gate = new();
        private CancellationTokenSource? registered;
        private bool raised;

        internal void BringForward()
        {
            CancellationTokenSource? waiting;

            lock (this.gate)
            {
                waiting = this.registered;

                if (waiting is null)
                {
                    this.raised = true;

                    return;
                }

                // Taken off here rather than when it is disposed, so this raise is the one that ends this wait and a
                // second raise arriving behind it is kept for the wait after rather than cancelling the same source.
                this.registered = null;
            }

            // Cancelled outside the lock, because a cancellation callback runs on the thread that cancels and this one
            // is on the path a person's own request returns through.
            waiting.Cancel();
        }

        internal Wait Register(CancellationToken stopping)
        {
            var ending = CancellationTokenSource.CreateLinkedTokenSource(stopping);
            var alreadyRaised = false;

            lock (this.gate)
            {
                if (this.raised)
                {
                    this.raised = false;
                    alreadyRaised = true;
                }
                else
                {
                    this.registered = ending;
                }
            }

            if (alreadyRaised)
            {
                ending.Cancel();
            }

            return new Wait(this, ending);
        }

        internal void Unregister(CancellationTokenSource ending)
        {
            lock (this.gate)
            {
                if (ReferenceEquals(this.registered, ending))
                {
                    this.registered = null;
                }
            }
        }
    }
}
