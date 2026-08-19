// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Governance;

namespace MailFathom.Application.Mail.Delivery.Governance;

/// <summary>Counts what each caller has been admitted to send inside the period this process is in.</summary>
/// <remarks>
/// <para>
/// Held in this process rather than in the database, which is the opposite of how the deployment's own ceilings are
/// counted and is deliberate. Two reasons, and the second is the stronger one. What this bounds is one client looping —
/// a fault that happens inside a running process and is caught by a counter inside it — while the fault a durable count
/// exists for, a process restarting and beginning every period afresh, is already bounded by
/// <see cref="OutgoingMailCeilings" />, which is counted from the records themselves. And the key is the calling
/// principal, which for a token is an issuer and a subject: an identifier for a person at somebody else's directory,
/// which writing onto every outgoing record would put into the database for as long as the record lives, under no
/// retention anybody asked for. A count that evaporates when its period rolls over holds it for exactly as long as the
/// bound it serves.
/// </para>
/// <para>
/// <b>Judging and charging are one operation, under one lock.</b> A ceiling read and then charged after an awaited
/// write is a ceiling two concurrent sends from one caller both pass, which is exactly the client this bound exists to
/// stop: a loop that dispatches rather than waits would exceed it by however many calls it had in flight. So a send is
/// weighed and counted in the same breath, and what a caller may still ask for is never a number read before an await.
/// </para>
/// <para>
/// What it is counted under is the send's own idempotency identity — the account it is sent as and the requester that
/// asked — which is the same pair the outbox writes one record per. So a caller retrying under the key it first asked
/// under is charged for one message however many times the call is repeated, and it is charged without waiting to see
/// which record the retry produced.
/// </para>
/// <para>
/// The charge therefore stands whether or not a record follows it. A send this deployment's own bounds refuse after
/// this ledger admitted it has spent the caller's allowance, and that is the honest answer rather than a leak: a client
/// asking repeatedly for a send that is refused every time is the loop being bounded, and the period rolls over. A send
/// already charged is admitted again without the ceiling being consulted at all, however full the period has become
/// since — the charge stands, so asking again buys the caller nothing and refusing it would only strand a client
/// retrying the message it was already admitted for.
/// </para>
/// <para>
/// Everything the ledger holds belongs to one period. The roll-over is not swept on a timer: the first caller to arrive
/// in a new period clears what the last one left, which is what keeps a ledger nobody is using from holding anything at
/// all.
/// </para>
/// </remarks>
/// <param name="ceilings">How much one caller may ask for in a period.</param>
/// <param name="timeProvider">Decides which period the present moment belongs to.</param>
public sealed class AuthoredSendUsageLedger(AuthoredSendCeilings ceilings, TimeProvider timeProvider)
{
    /// <summary>The greatest number of callers one period is counted for.</summary>
    /// <remarks>
    /// Far above the credentials any installation issues, and present because the key is a caller's own identity rather
    /// than anything this deployment allocates: an authorization surface admitting a subject per person could otherwise
    /// grow this without bound. A period that reaches it refuses the sends of callers it is not already counting, which
    /// is the answer a bound has to give — one that admitted everything once it ran out of room would be no bound. The
    /// refusal says that rather than naming a configured ceiling, because this number is not one an operator wrote and
    /// is reached on a deployment that declared no per-caller ceiling at all.
    /// </remarks>
    public const int MaximumCallersPerPeriod = 4096;

    private readonly Lock gate = new();
    private readonly Dictionary<string, CallerCounts> countsByCaller = new(StringComparer.Ordinal);
    private DateTimeOffset periodStart = DateTimeOffset.MinValue;

    /// <summary>Weighs one send against the caller's period and charges it to that caller in the same operation.</summary>
    /// <param name="caller">The identity the calling principal was admitted under.</param>
    /// <param name="request">The send being asked for, which carries both its idempotency identity and its recipients.</param>
    /// <returns>The ceiling the send reached, which is when nothing was charged, or <see langword="null" /> when it was admitted.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="caller" /> is empty or white space, which is a principal nothing can be counted against.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A caller arriving in a period that is already counting the greatest number of them is refused rather than
    /// admitted uncounted: the ledger reports what it can hold rather than growing to hold whatever it is handed.
    /// </remarks>
    public AuthoredSendCeiling? Admit(string caller, OutgoingEmailRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caller);
        ArgumentNullException.ThrowIfNull(request);

        if (ceilings.IsUnbounded)
        {
            return null;
        }

        var send = new SendIdentity(request.AccountId, request.Requester.Origin, request.Requester.Identity);
        var recipientCount = request.Recipients.Count;

        lock (this.gate)
        {
            this.OpenPeriodOf(timeProvider.GetUtcNow());

            if (!this.countsByCaller.TryGetValue(caller, out var counts))
            {
                if (this.countsByCaller.Count >= MaximumCallersPerPeriod)
                {
                    return AuthoredSendCeiling.CallerCount;
                }

                counts = new CallerCounts();
                this.countsByCaller[caller] = counts;
            }

            if (counts.AlreadyCharged(send))
            {
                return null;
            }

            if (ceilings.FindReachedCeiling(counts.Usage, recipientCount) is { } reached)
            {
                return reached;
            }

            counts.Charge(send, recipientCount);

            return null;
        }
    }

    /// <summary>Discards everything counted for an earlier period, which is what a roll-over does.</summary>
    /// <remarks>Called under the gate, so a period never changes between a judgement and the charge it is part of.</remarks>
    private void OpenPeriodOf(DateTimeOffset instant)
    {
        var current = ceilings.PeriodStartAt(instant);

        if (current == this.periodStart)
        {
            return;
        }

        this.periodStart = current;
        this.countsByCaller.Clear();
    }

    /// <summary>What one send is counted under, which is the pair the outbox writes one record per.</summary>
    /// <remarks>
    /// The account and the requester together are an outgoing email's idempotency identity, so two calls that would
    /// produce one record are one charge here without this ledger having to see either record.
    /// </remarks>
    private readonly record struct SendIdentity(MailAccountId AccountId, OutgoingEmailOrigin Origin, string Identity);

    /// <summary>What one caller has been charged inside the period the ledger is in.</summary>
    /// <remarks>
    /// The identities are held rather than only counted, because that is what tells a retry of one send from a second
    /// send. The set is bounded by the ceilings themselves, since a caller past them is charged nothing further.
    /// </remarks>
    private sealed class CallerCounts
    {
        private readonly HashSet<SendIdentity> chargedSends = [];
        private long recipientCount;

        public AuthoredSendUsage Usage => new(this.chargedSends.Count, this.recipientCount);

        public bool AlreadyCharged(SendIdentity send) => this.chargedSends.Contains(send);

        public void Charge(SendIdentity send, int recipients)
        {
            if (this.chargedSends.Add(send))
            {
                this.recipientCount += recipients;
            }
        }
    }
}
