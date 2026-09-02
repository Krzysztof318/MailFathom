// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Mail.Delivery.Operations;

namespace MailFathom.Application.Mail.Delivery.Outbox;

/// <summary>Reports what one pass over an account's outbox did, and what it left standing there.</summary>
/// <remarks>
/// The results are per record and in the order they were claimed, so whoever publishes the pass reports one line and
/// one measurement per send rather than a total that hides which one needs a person.
/// <para>
/// The depth beside them is what the pass left rather than what it did, and it is here because the pass is the one
/// thing that runs on the outbox's own cadence: a level published from a collector's interval would be a database
/// query on whatever schedule an exporter happened to be configured with. It covers the stages a send can still move
/// from and no others, so it stays the backlog an operator alerts on rather than a count of everything ever sent.
/// </para>
/// </remarks>
/// <param name="Results">What each claimed send ended in.</param>
/// <param name="FilingResults">What each attempt to put a copy of one of these messages into a folder did.</param>
/// <param name="DraftResults">What each attempt to bring the drafts folder into step with a held draft did.</param>
/// <param name="MarkedUnknownCount">How many records this pass found stuck mid-transmission and stamped with the reason.</param>
/// <param name="BatchFilled">Whether the claim took as much as it was allowed, which means there is more waiting.</param>
/// <param name="OutstandingByStage">How much the pass left standing at each non-terminal stage, zeros included, and empty where it measured nothing at all.</param>
public sealed record MailOutboxPassReport(
    IReadOnlyList<MailOutboxDeliveryResult> Results,
    IReadOnlyList<OutgoingMailFilingResult> FilingResults,
    IReadOnlyList<MailDraftFilingResult> DraftResults,
    int MarkedUnknownCount,
    bool BatchFilled,
    IReadOnlyList<OutboxStageCount> OutstandingByStage)
{
    /// <summary>A pass that never ran produces this: one that ended in a shutdown, a conflict, or a failure.</summary>
    /// <remarks>
    /// An account with nothing outstanding does not produce this. Its pass runs, settles nothing, and reports a real
    /// measurement of zero at every unfinished stage — which is the distinction the empty list here carries: it
    /// measures no depth rather than reporting zero, because an account this pass never reached says nothing about how
    /// much is waiting for it, and publishing a zero would clear a backlog on a dashboard that nothing had drained.
    /// </remarks>
    public static MailOutboxPassReport Empty { get; } =
        new([], [], [], MarkedUnknownCount: 0, BatchFilled: false, OutstandingByStage: []);

    /// <summary>Reports a pass that settled an account's drafts and reached no outbox at all.</summary>
    /// <param name="draftResults">What each attempt to bring the drafts folder into step with a held draft did.</param>
    /// <returns>The report, which measures no outbox depth for the reason <see cref="Empty" /> gives.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="draftResults" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// It is what an account with no submission endpoint produces. Such an account still keeps drafts, because a draft
    /// is written over IMAP, so the sweep that resumes them runs for it — and everything the outbox would have said is
    /// absent rather than zero, since nothing about the outbox was read.
    /// </remarks>
    public static MailOutboxPassReport WithDraftsAlone(IReadOnlyList<MailDraftFilingResult> draftResults)
    {
        ArgumentNullException.ThrowIfNull(draftResults);

        return new([], [], draftResults, MarkedUnknownCount: 0, BatchFilled: false, OutstandingByStage: []);
    }

    /// <summary>Gets how many copies this pass put into a folder of the mailbox.</summary>
    public int FiledCount => this.FilingResults.Count(result => result.Outcome == OutgoingMailFilingOutcome.Filed);

    /// <summary>Gets how many copies this pass could not put where the account asked for them.</summary>
    /// <remarks>
    /// It is deliberately not part of <see cref="AccountDeferred" /> and of nothing else that decides what happens
    /// next. A copy that was not filed is a message the owner cannot see in their own client; it is never a message
    /// that failed to reach anybody, and no send is attempted again because of it.
    /// </remarks>
    public int NotFiledCount => this.FilingResults.Count(result => result.Outcome
        is OutgoingMailFilingOutcome.DestinationUnavailable
        or OutgoingMailFilingOutcome.OutcomeUnknown
        or OutgoingMailFilingOutcome.Failed);

    /// <summary>Gets how many drafts this pass brought the mailbox into step with.</summary>
    /// <remarks>
    /// It counts the three endings that changed something in the folder — a first copy appended, a copy replaced, and a
    /// given-up draft taken back out — rather than the drafts the pass looked at, because a draft that was already
    /// settled is one the pass answered with a read.
    /// </remarks>
    public int DraftsSettledCount => this.DraftResults.Count(result => result.Outcome
        is MailDraftFilingOutcome.Filed
        or MailDraftFilingOutcome.Replaced
        or MailDraftFilingOutcome.Discarded);

    /// <summary>Gets how many drafts this pass could not bring the mailbox into step with.</summary>
    /// <remarks>
    /// It is deliberately not part of <see cref="AccountDeferred" /> and of nothing else that decides what happens
    /// next, for the reason an unfiled copy is not: a draft the folder does not show is a message its author cannot
    /// reach from their own client, and no send is attempted again because of it. A diverged copy is counted here as
    /// well, because what an operator has to know is that MailFathom stopped acting on a message it had put there.
    /// </remarks>
    public int DraftsNotSettledCount => this.DraftResults.Count(result => result.Outcome
        is MailDraftFilingOutcome.DestinationUnavailable
        or MailDraftFilingOutcome.Diverged
        or MailDraftFilingOutcome.OutcomeUnknown
        or MailDraftFilingOutcome.Failed);

    /// <summary>Gets how many of the claimed sends the server acknowledged.</summary>
    public int SentCount => this.CountOf(MailOutboxDeliveryOutcome.Sent);

    /// <summary>Gets how many of the claimed sends nothing will offer again.</summary>
    public int RefusedCount => this.CountOf(MailOutboxDeliveryOutcome.Refused);

    /// <summary>Gets how many of the claimed sends are waiting for another attempt.</summary>
    public int DeferredCount => this.CountOf(MailOutboxDeliveryOutcome.Deferred);

    /// <summary>Gets how many of the claimed sends ended with nobody able to say what their recipients received.</summary>
    public int UnknownOutcomeCount => this.CountOf(MailOutboxDeliveryOutcome.OutcomeUnknown);

    /// <summary>Gets how many of the claimed sends ended with the store refusing to record what happened to them.</summary>
    public int NotRecordedCount => this.CountOf(MailOutboxDeliveryOutcome.NotRecorded);

    /// <summary>Gets whether the pass met a submission server that would not serve it, which is what defers the account.</summary>
    /// <remarks>
    /// A send given back for another attempt is the shape a provider's unavailability takes here, and it is the one
    /// outcome that can say something about the account rather than about the message. A refusal never does: a server
    /// that answered is a server that is working.
    /// <para>
    /// It is a signal rather than a diagnosis, because one other thing produces the same outcome: a transmission the
    /// server took whose envelope one address was temporarily refused for is deferred as well, so those addresses are
    /// offered again. That send says nothing about the provider. Reading a true value as "the endpoint is down" is
    /// therefore wrong on its own; what it says is that this account has work coming back.
    /// </para>
    /// </remarks>
    public bool AccountDeferred => this.DeferredCount > 0;

    private int CountOf(MailOutboxDeliveryOutcome outcome) =>
        this.Results.Count(result => result.Outcome == outcome);
}
