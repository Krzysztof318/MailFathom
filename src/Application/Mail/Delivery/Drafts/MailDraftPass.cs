// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Drafts;

/// <summary>Keeps one account's drafts folder in step with the drafts this deployment holds.</summary>
/// <remarks>
/// <para>
/// Saving, revising, and discarding a draft each act on the mailbox where they are asked for, so what is left for a
/// pass is the half nobody is standing there for: a process that stopped between the two commands of a replacement, a
/// mail server that was briefly unreachable, and a promoted draft whose message has since been delivered. Each of those
/// is a draft the record already describes exactly, which is why the pass reads the record rather than the folder.
/// </para>
/// <para>
/// It runs where the outbox pass runs, which is both what a signal wakes and what an account's own synchronization run
/// drains. That is the guarantee resuming rests on: whatever is outstanding is reached again on every run, so nothing
/// has to remember that a process died mid-replacement.
/// </para>
/// <para>
/// An account whose drafts are all settled costs one bounded query. The read answers only drafts that owe the mail
/// server something, so a deployment nobody drafts in spends nothing per pass.
/// </para>
/// </remarks>
public sealed class MailDraftPass
{
    private readonly IMailDraftStore drafts;
    private readonly IOutgoingEmailStore outgoingEmails;
    private readonly MailDraftFiler filer;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly TimeProvider timeProvider;
    private readonly int maxDraftsPerPass;

    /// <summary>Initializes the pass from the drafts it reads and the filer it acts through.</summary>
    /// <param name="drafts">Holds the drafts whose copies this pass keeps in step.</param>
    /// <param name="outgoingEmails">Says whether a promoted draft's message has actually been delivered yet.</param>
    /// <param name="filer">Appends, replaces, and withdraws one draft's copies.</param>
    /// <param name="commitPolicy">Commits the mark that gives a delivered draft up.</param>
    /// <param name="timeProvider">Stamps what the pass records.</param>
    /// <param name="settings">Bounds how many drafts one pass settles.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    /// <remarks>
    /// The bound is the same number the deliveries use, because both are one conversation with a server per item and
    /// what one pass leaves the next one takes. A second number would be a second thing to keep in step for no
    /// difference an operator could observe.
    /// </remarks>
    public MailDraftPass(
        IMailDraftStore drafts,
        IOutgoingEmailStore outgoingEmails,
        MailDraftFiler filer,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        TimeProvider timeProvider,
        MailOutboxSettings settings)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        ArgumentNullException.ThrowIfNull(outgoingEmails);
        ArgumentNullException.ThrowIfNull(filer);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(settings);

        this.drafts = drafts;
        this.outgoingEmails = outgoingEmails;
        this.filer = filer;
        this.commitPolicy = commitPolicy;
        this.timeProvider = timeProvider;
        this.maxDraftsPerPass = settings.MaxDeliveriesPerPass;
    }

    /// <summary>Settles every draft of one account that still owes the mail server something.</summary>
    /// <param name="accountId">The account whose drafts are settled.</param>
    /// <param name="cancellationToken">Cancels the reads and the commands.</param>
    /// <returns>What each attempt did, which is empty on an account with nothing outstanding.</returns>
    public async Task<IReadOnlyList<MailDraftFilingResult>> SettleOutstandingAsync(
        MailAccountId accountId,
        CancellationToken cancellationToken)
    {
        var outstanding = await this.drafts.ReadOutstandingAsync(
            accountId,
            this.maxDraftsPerPass,
            cancellationToken);

        var results = new List<MailDraftFilingResult>(outstanding.Count);
        foreach (var draft in outstanding)
        {
            results.Add(await this.filer.SettleAsync(draft, cancellationToken));
        }

        return results;
    }

    /// <summary>Gives up the draft one delivered send was promoted from, and takes its copy out of the folder.</summary>
    /// <param name="outgoingEmailId">The send whose attempt has just ended.</param>
    /// <param name="cancellationToken">Cancels the reads and the commands.</param>
    /// <returns>What the attempt did, which is empty for a send that came from no draft or has not been delivered.</returns>
    /// <remarks>
    /// <para>
    /// It is keyed on the send having actually been delivered rather than on it having been promoted, which is the
    /// whole of what makes a promotion safe to ask for. A message that is refused, deferred, or left with an unknown
    /// outcome leaves the draft exactly as it was, so an owner whose send failed still has the message they wrote.
    /// </para>
    /// <para>
    /// The draft is given up rather than deleted outright, so the removal of its copy follows the same recorded
    /// sequence every other removal does and a process that dies mid-way is resumed by the pass above.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<MailDraftFilingResult>> SettlePromotedAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        if (await this.drafts.FindPromotedToAsync(outgoingEmailId, cancellationToken) is not { } draft)
        {
            return [];
        }

        if (!draft.IsDiscarded)
        {
            if (await this.outgoingEmails.FindAsync(outgoingEmailId, cancellationToken)
                is not { Stage: OutgoingEmailStage.Sent })
            {
                return [];
            }

            await this.commitPolicy.CommitAsync(
                (session, token) => this.drafts.RecordDiscardedAsync(
                    session,
                    draft.Id,
                    this.timeProvider.GetUtcNow(),
                    token),
                cancellationToken);
        }

        // Read again so the filer acts on the mark that was just written rather than on the record that preceded it.
        return await this.drafts.FindAsync(draft.Id, cancellationToken) is { } discarded
            ? [await this.filer.SettleAsync(discarded, cancellationToken)]
            : [];
    }
}
