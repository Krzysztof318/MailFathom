// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Persistence;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Emails.ThreadStates;

/// <summary>Derives where the account's conversations stand, once each time one of them changes, and writes it down.</summary>
/// <remarks>
/// <para>
/// Behind the cut and behind the enrichment beside it, because a statement cites messages and a conversation derived
/// from before its newest message was settled would be describing an exchange this deployment had not finished storing.
/// Everything the earlier stages settle is settled by the time this runs.
/// </para>
/// <para>
/// A step of the account's synchronization run rather than a schedule of its own, for the reason every other pass is:
/// that run already has per-account isolation, a slot count, a jittered backoff, and a failure path that defers the
/// account instead of the process, and only one pass per account is ever in flight because of it.
/// </para>
/// <para>
/// It needs no cursor. A conversation leaves the selection by having a state recorded against the shape it currently
/// has, so an interrupted pass repeats nothing and skips nothing, and what one pass's bound leaves behind is the next
/// run's. That is also the whole of how an existing mailbox is filled in, and the whole of how a state is kept current:
/// a conversation that gains a reply no longer matches the state stored against it, so the next pass reaches it again.
/// </para>
/// <para>
/// Derivations are made one after another and the first withheld one ends the pass, for the reason enrichment's are:
/// a derivation is a provider call, the endpoint's concurrency limiter admits a few invocations at a time, and every
/// reason a derivation is withheld outlives one conversation.
/// </para>
/// </remarks>
public sealed class ThreadStateDerivationPass
{
    /// <summary>How many conversations one pass derives a state for.</summary>
    /// <remarks>
    /// Smaller than the enrichment pass's bound, because one derivation here reads a whole exchange rather than the
    /// opening of one message: a batch of the same size would hold an account run's slot for several times as long. It
    /// is a constant rather than a setting because it bounds this pass's latency rather than describing a deployment —
    /// what a deployment declares about the spend is the answering period's own ceilings, which every derivation is
    /// admitted against.
    /// </remarks>
    public const int MaximumThreadsPerPass = 4;

    /// <summary>How many messages of a conversation one derivation takes in before it is recorded as too large instead.</summary>
    /// <remarks>
    /// Set where reading an exchange stops and reproducing a mailing list begins. A conversation people follow runs to
    /// a few dozen messages; past that the text alone outgrows what one turn may carry, and the honest answer is to say
    /// the conversation is too long rather than to summarize the part that fits.
    /// </remarks>
    public const int MaximumMessagesPerThread = 40;

    /// <summary>How much of one message travels with the batch.</summary>
    /// <remarks>
    /// What each message *added* is already trimmed of the history it quoted before it is stored, so this bounds a
    /// genuinely long message rather than a quoted thread. It is per message rather than per conversation so that one
    /// long message cannot crowd out every other message's opening.
    /// </remarks>
    public const int MaximumCharactersPerMessage = 4_000;

    private readonly IStoredThreadStateStore stateStore;
    private readonly IThreadStateDeriver deriver;
    private readonly IMailUserLanguages languages;
    private readonly SensitiveContentEgressGuard egressGuard;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the pass from the state it walks and the derivation it asks.</summary>
    /// <param name="stateStore">Reads which conversations are awaiting a state and writes down what one derivation produced.</param>
    /// <param name="deriver">Derives one conversation's state, in whichever state the deployment left it.</param>
    /// <param name="languages">Answers which language the account's owner reads, which the statements are written in.</param>
    /// <param name="egressGuard">States whose mail the conversation is, so the derivation scans it under that user's posture.</param>
    /// <param name="commitPolicy">Commits one conversation's record, retrying a conflict with a competing writer.</param>
    /// <param name="timeProvider">Reads when a derivation ran.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public ThreadStateDerivationPass(
        IStoredThreadStateStore stateStore,
        IThreadStateDeriver deriver,
        IMailUserLanguages languages,
        SensitiveContentEgressGuard egressGuard,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(deriver);
        ArgumentNullException.ThrowIfNull(languages);
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.stateStore = stateStore;
        this.deriver = deriver;
        this.languages = languages;
        this.egressGuard = egressGuard;
        this.commitPolicy = commitPolicy;
        this.timeProvider = timeProvider;
    }

    /// <summary>Takes one bounded pass over the account's conversations awaiting a state.</summary>
    /// <param name="account">The account whose conversations are derived from.</param>
    /// <param name="cancellationToken">Cancels the pass between conversations; committed records stay durable.</param>
    /// <returns>How many conversations this pass derived a state for, what stopped it, and whether more remain.</returns>
    /// <exception cref="PersistenceConcurrencyConflictException">
    /// Thrown when a competing writer wins a race the bounded retries could not resolve. Records already written stay
    /// durable and the next run resumes by asking the same question.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels. Committed records stay durable.</exception>
    public async Task<ThreadStateDerivationPassReport> RunAsync(
        MailAccountIdentity account,
        CancellationToken cancellationToken)
    {
        // The switch is honoured here rather than at composition, so a deployment that has not turned the derivation on
        // runs a pass that answers in one comparison and issues no query at all — the selection is a grouped scan per
        // account per run, and nothing would act on what it returned.
        if (!this.deriver.IsActive)
        {
            return new ThreadStateDerivationPassReport(
                DerivedThreadCount: 0,
                StatedThreadCount: 0,
                TooLargeThreadCount: 0,
                StoppedBy: ThreadStateWithholding.NotActivated,
                ThreadsRemain: false);
        }

        // Established for the whole pass rather than per conversation, because whose mail a message is decides the
        // posture it is scanned under and every conversation in the batch belongs to the one account this pass walks.
        using var actingFor = this.egressGuard.ActingFor(account.User);

        // Resolved once for the same reason and from the same fact: every conversation in this batch is one person's,
        // and what they read is what every statement derived from it is written in.
        var language = this.languages.ForUser(account.User);

        var batch = await this.stateStore.GetThreadsAwaitingStateAsync(
            account,
            MaximumThreadsPerPass,
            MaximumMessagesPerThread,
            MaximumCharactersPerMessage,
            cancellationToken);

        var derivedCount = 0;
        var statedCount = 0;
        var tooLargeCount = 0;

        foreach (var thread in batch)
        {
            var derivation = await this.deriver.DeriveAsync(thread, language, cancellationToken);

            if (derivation.Withheld is { } withholding)
            {
                return new ThreadStateDerivationPassReport(
                    derivedCount,
                    statedCount,
                    tooLargeCount,
                    withholding,
                    ThreadsRemain: true);
            }

            var state = new EmailThreadState(
                thread.ThreadId,
                derivation.Coverage,
                derivation.Entries,
                thread.Revision,
                this.timeProvider.GetUtcNow(),
                IsCurrent: true);

            // Committed one conversation at a time rather than one batch at a time, because a derivation that is
            // settled has already been paid for: holding it until the last of the batch had answered would lose every
            // one of them to a provider that stopped answering half way through.
            await this.commitPolicy.CommitAsync(
                (session, attemptCancellationToken) => this.stateStore.SaveAsync(
                    session,
                    state,
                    attemptCancellationToken),
                cancellationToken);

            derivedCount++;

            if (derivation.Coverage is ThreadStateCoverage.ThreadTooLarge)
            {
                tooLargeCount++;
            }
            else if (derivation.Entries.Count > 0)
            {
                statedCount++;
            }
        }

        return new ThreadStateDerivationPassReport(
            derivedCount,
            statedCount,
            tooLargeCount,
            StoppedBy: null,
            ThreadsRemain: batch.Count == MaximumThreadsPerPass);
    }
}
