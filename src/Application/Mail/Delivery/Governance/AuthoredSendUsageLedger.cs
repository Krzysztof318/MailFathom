// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

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
/// A send is charged once by the record it produced, so a caller retrying under the key it first asked under is
/// charged for one message however many times the call is repeated — the outbox answers a repeat with the record the
/// first one wrote, and this ledger recognizes it. What is judged before the write is the present count, so a period
/// that has filled up can refuse a retry of a send already recorded; that costs nothing, exactly as it costs nothing
/// against the deployment's own ceilings, because the record stands and its message is still delivered.
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
    /// is the answer a bound has to give — one that admitted everything once it ran out of room would be no bound.
    /// </remarks>
    public const int MaximumCallersPerPeriod = 4096;

    private readonly Lock gate = new();
    private readonly Dictionary<string, CallerCounts> countsByCaller = new(StringComparer.Ordinal);
    private DateTimeOffset periodStart = DateTimeOffset.MinValue;

    /// <summary>Finds the ceiling one further message would carry a caller's period past.</summary>
    /// <param name="caller">The identity the calling principal was admitted under.</param>
    /// <param name="recipientCount">The people the message being asked for names.</param>
    /// <returns>The ceiling reached, or <see langword="null" /> when the message is within both of them.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="caller" /> is empty or white space, which is a principal nothing can be counted against.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="recipientCount" /> is not positive.</exception>
    public AuthoredSendCeiling? FindReachedCeiling(string caller, int recipientCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caller);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recipientCount);

        if (ceilings.IsUnbounded)
        {
            return null;
        }

        lock (this.gate)
        {
            this.OpenPeriodOf(timeProvider.GetUtcNow());

            if (this.countsByCaller.TryGetValue(caller, out var counts))
            {
                return ceilings.FindReachedCeiling(counts.Usage, recipientCount);
            }

            return this.countsByCaller.Count >= MaximumCallersPerPeriod
                ? AuthoredSendCeiling.CallerMessages
                : ceilings.FindReachedCeiling(AuthoredSendUsage.None, recipientCount);
        }
    }

    /// <summary>Charges one send to the caller that asked for it, and charges a repeat of it to nobody.</summary>
    /// <param name="caller">The identity the calling principal was admitted under.</param>
    /// <param name="outgoingEmailId">The record the send was written down as, which is what makes a repeat recognizable.</param>
    /// <param name="recipientCount">The people that record is addressed to.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="caller" /> is empty or white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="recipientCount" /> is not positive.</exception>
    /// <remarks>
    /// A caller arriving in a period that is already counting the greatest number of them is not recorded, which is the
    /// same posture the judgement above takes: the ledger reports what it can hold rather than growing to hold whatever
    /// it is handed.
    /// </remarks>
    public void Charge(string caller, OutgoingEmailId outgoingEmailId, int recipientCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caller);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recipientCount);

        if (ceilings.IsUnbounded)
        {
            return;
        }

        lock (this.gate)
        {
            this.OpenPeriodOf(timeProvider.GetUtcNow());

            if (!this.countsByCaller.TryGetValue(caller, out var counts))
            {
                if (this.countsByCaller.Count >= MaximumCallersPerPeriod)
                {
                    return;
                }

                counts = new CallerCounts();
                this.countsByCaller[caller] = counts;
            }

            counts.Charge(outgoingEmailId, recipientCount);
        }
    }

    /// <summary>Discards everything counted for an earlier period, which is what a roll-over does.</summary>
    /// <remarks>Called under the gate by both members, so a period never changes underneath a judgement and the charge that follows it.</remarks>
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

    /// <summary>What one caller has been charged inside the period the ledger is in.</summary>
    /// <remarks>
    /// The records are held rather than only counted, because that is what tells a retry of one send from a second send:
    /// the outbox answers both with a record, and only the second one carries an identity this caller has not been
    /// charged for. The set is bounded by the ceilings themselves, since a caller past them is refused before anything
    /// is written down.
    /// </remarks>
    private sealed class CallerCounts
    {
        private readonly HashSet<OutgoingEmailId> chargedRecords = [];
        private long recipientCount;

        public AuthoredSendUsage Usage => new(this.chargedRecords.Count, this.recipientCount);

        public void Charge(OutgoingEmailId outgoingEmailId, int recipients)
        {
            if (this.chargedRecords.Add(outgoingEmailId))
            {
                this.recipientCount += recipients;
            }
        }
    }
}
