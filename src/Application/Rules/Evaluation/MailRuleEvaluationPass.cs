// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Facts;
using MailFathom.Application.Rules.History;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Rules.Evaluation;

/// <summary>Runs one account's rules over the mail that has arrived, and over its whole mailbox where somebody asked.</summary>
/// <remarks>
/// <para>
/// A step of the account's synchronization run rather than a schedule of its own. That run already has per-account
/// isolation, a jittered backoff, a slot count that stops one account starving another, and a failure path that defers
/// the account instead of the process; a rule pass needs every one of those and none of them differently, so it takes
/// the ones that exist. It follows that only one pass per account is ever in flight, structurally rather than by a lock.
/// </para>
/// <para>
/// It runs after the mail and its content are committed and never inside the synchronization transaction, so a provider
/// redelivery or a synchronization retry cannot produce a different processing boundary than a clean run — and nothing
/// an MCP read does waits on a rule.
/// </para>
/// <para>
/// The two walks are the two triggers. The arrival walk reaches mail no pass has evaluated, and recording an evaluation
/// is what takes an email out of it, which is what makes a rule apply to mail arriving from now on. The requested walk
/// is the only way mail already evaluated is evaluated again: reprocessing under a newer rule set is something an owner
/// asks for, never something an edit sets off.
/// </para>
/// <para>
/// Both walks are bounded per batch and commit each batch with the position it reached, so an interrupted pass resumes
/// at the email nobody read rather than replaying one or stepping over one. What a batch budget leaves behind is the
/// next account run's, which is the same answer synchronization itself gives to a folder it could not finish.
/// </para>
/// <para>
/// What a match asks the mailbox for is written down in the batch's own transaction, as the durable mutation record
/// every requester uses, and never issued from here: a pass reaches no mail server, and the account's convergence pass
/// is what carries each record to a completed or a dead-lettered ending. Committing the requests with the evaluations
/// is what makes the pair atomic — an email is never recorded as evaluated while the change its rules asked for was
/// lost, and a rolled-back batch asks again under the same identity.
/// </para>
/// <para>
/// The history of what each rule concluded is written in that same transaction and for the same reason. An explanation
/// committed apart from the decision it explains is one that can outlive a rolled-back batch or go missing from a
/// committed one, and either way an operator reading it would be told something that did not happen.
/// </para>
/// </remarks>
public sealed class MailRuleEvaluationPass
{
    private readonly IMailRuleSetSource ruleSetSource;
    private readonly MailRuleSetEvaluator evaluator;
    private readonly IMailRuleEvaluationStore store;
    private readonly IMailRuleEvaluationRunStore runStore;
    private readonly MailRuleActionRecorder actionRecorder;
    private readonly IMailRuleExecutionStore executionStore;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly MailRuleEvaluationOptions options;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the pass from the rule set it applies and the local state it walks.</summary>
    /// <param name="ruleSetSource">Hands out the rule set the pass runs under, read once when the pass begins.</param>
    /// <param name="evaluator">Runs a rule set over one email and classifies every way a condition can fail to answer.</param>
    /// <param name="store">Reads the candidates and records which emails have been evaluated.</param>
    /// <param name="runStore">Reads the requested whole-mailbox run and records how far it has been carried.</param>
    /// <param name="actionRecorder">Writes down the changes a matching rule asks the mailbox for.</param>
    /// <param name="executionStore">Keeps the record of what each rule concluded and what became of what it asked for.</param>
    /// <param name="commitPolicy">Commits a batch's evaluations together with the position they account for.</param>
    /// <param name="options">Bounds one walk.</param>
    /// <param name="timeProvider">Supplies the instant each email is evaluated at and each record is stamped with.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a configured bound is below one.</exception>
    public MailRuleEvaluationPass(
        IMailRuleSetSource ruleSetSource,
        MailRuleSetEvaluator evaluator,
        IMailRuleEvaluationStore store,
        IMailRuleEvaluationRunStore runStore,
        MailRuleActionRecorder actionRecorder,
        IMailRuleExecutionStore executionStore,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        MailRuleEvaluationOptions options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(ruleSetSource);
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(actionRecorder);
        ArgumentNullException.ThrowIfNull(executionStore);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.BatchSize, 1, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxBatchesPerPass, 1, nameof(options));

        this.ruleSetSource = ruleSetSource;
        this.evaluator = evaluator;
        this.store = store;
        this.runStore = runStore;
        this.actionRecorder = actionRecorder;
        this.executionStore = executionStore;
        this.commitPolicy = commitPolicy;
        this.options = options;
        this.timeProvider = timeProvider;
    }

    /// <summary>Takes one bounded pass over the account's arrivals, and over its requested run where it has one.</summary>
    /// <param name="accountId">The account whose mail is evaluated.</param>
    /// <param name="cancellationToken">Cancels the pass between emails and between batches; committed batches stay durable.</param>
    /// <returns>What each walk did, under the revision the pass read when it began.</returns>
    /// <exception cref="PersistenceConcurrencyConflictException">
    /// Thrown when a competing writer wins a race the bounded retries could not resolve. Batches already committed stay
    /// durable and the next account run resumes from them.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels. Committed batches stay durable.</exception>
    /// <remarks>
    /// The rule set is read once here and held for both walks, which is the whole of the reload contract for a pass: an
    /// edit reaches the next pass rather than this one, so what a rule means cannot change halfway through a mailbox.
    /// </remarks>
    public async Task<MailRuleEvaluationReport> RunAsync(MailAccountId accountId, CancellationToken cancellationToken)
    {
        var ruleSet = this.ruleSetSource.Current;
        var requiresExtractedBodyText = RequiresExtractedBodyText(ruleSet, accountId);

        var arrivals = await this.WalkArrivalsAsync(accountId, ruleSet, requiresExtractedBodyText, cancellationToken);
        var requestedRun = await this.WalkRequestedRunAsync(
            accountId,
            ruleSet,
            requiresExtractedBodyText,
            cancellationToken);

        return new MailRuleEvaluationReport(ruleSet.Revision, arrivals, requestedRun.Walk, requestedRun.Ending);
    }

    /// <summary>Reports whether any rule this account's mail reaches names the one fact that costs a stored-content read.</summary>
    /// <remarks>
    /// Answered for the whole pass rather than per email, because it is a property of the rule set and the account
    /// filter. It is what decides whether an email still awaiting extraction is a message to wait for or one to evaluate
    /// now with the fact absent: a rule set that never names the body text has nothing to wait for.
    /// </remarks>
    private static bool RequiresExtractedBodyText(MailRuleSet ruleSet, MailAccountId accountId) => ruleSet.Rules
        .Where(rule => rule.AppliesTo(accountId.Value))
        .Any(rule => rule.Condition.ReferencedFacts.Contains(MailRuleFact.BodyText));

    /// <summary>Walks the mail no pass has evaluated, in identity order, until the queue or the batch budget runs out.</summary>
    /// <remarks>
    /// The resume position is held for the length of the walk and never committed, because the record of an evaluation
    /// is what the queue is made of: an email this walk evaluated is gone from the next batch, and an email it skipped
    /// has to be stepped over within the walk so a message waiting for extraction cannot hold up the mail behind it.
    /// </remarks>
    private async Task<MailRuleEvaluationWalk> WalkArrivalsAsync(
        MailAccountId accountId,
        MailRuleSet ruleSet,
        bool requiresExtractedBodyText,
        CancellationToken cancellationToken)
    {
        var tally = new EvaluationTally();
        StoredEmailId? position = null;
        var emailsRemain = false;

        for (var batchNumber = 1; batchNumber <= this.options.MaxBatchesPerPass; batchNumber++)
        {
            var batch = await this.store.GetEmailsAwaitingFirstEvaluationAsync(
                accountId,
                position,
                this.options.BatchSize,
                cancellationToken);

            if (batch.Count == 0)
            {
                emailsRemain = false;

                break;
            }

            var outcome = await this.EvaluateBatchAsync(
                ruleSet,
                requiresExtractedBodyText,
                batch,
                cancellationToken);

            if (outcome.EvaluatedEmailIds.Count > 0)
            {
                var evaluatedAt = this.timeProvider.GetUtcNow();

                await this.commitPolicy.CommitAsync(
                    async (session, attemptCancellationToken) =>
                    {
                        await this.store.RecordEvaluatedAsync(
                            session,
                            outcome.EvaluatedEmailIds,
                            evaluatedAt,
                            attemptCancellationToken);

                        await this.RecordDecisionsAsync(
                            session,
                            ruleSet.Revision,
                            outcome,
                            MailRuleExecutionTrigger.Arrival,
                            attemptCancellationToken);
                    },
                    cancellationToken);
            }

            tally.Add(outcome);
            position = batch[^1].StoredEmailId;
            emailsRemain = batch.Count == this.options.BatchSize;

            if (!emailsRemain)
            {
                break;
            }
        }

        return tally.ToWalk(emailsRemain);
    }

    /// <summary>Carries a requested whole-mailbox run as far as one pass's batch budget reaches.</summary>
    /// <remarks>
    /// The revision check is the first thing the run meets. A run that has already started under one rule set cannot be
    /// finished under another — MailFathom holds only the set its configuration currently declares — so a set that has
    /// moved ends the run as superseded rather than letting one walk apply two rule sets to one mailbox.
    /// </remarks>
    private async Task<RequestedRunOutcome> WalkRequestedRunAsync(
        MailAccountId accountId,
        MailRuleSet ruleSet,
        bool requiresExtractedBodyText,
        CancellationToken cancellationToken)
    {
        var run = await this.runStore.FindOutstandingAsync(accountId, cancellationToken);

        if (run is null)
        {
            return new RequestedRunOutcome(Walk: null, Ending: null);
        }

        if (run.Revision.IsSpecified && run.Revision != ruleSet.Revision)
        {
            await this.CommitRunAsync(
                run with
                {
                    EndedAt = this.timeProvider.GetUtcNow(),
                    Ending = MailRuleEvaluationRunEnding.Superseded,
                },
                cancellationToken);

            return new RequestedRunOutcome(MailRuleEvaluationWalk.Empty, MailRuleEvaluationRunEnding.Superseded);
        }

        if (!run.Revision.IsSpecified)
        {
            run = run with { Revision = ruleSet.Revision };
        }

        var tally = new EvaluationTally();

        for (var batchNumber = 1; batchNumber <= this.options.MaxBatchesPerPass && run.IsOutstanding; batchNumber++)
        {
            run = await this.CarryRunBatchAsync(run, ruleSet, requiresExtractedBodyText, tally, cancellationToken);
        }

        return new RequestedRunOutcome(tally.ToWalk(run.IsOutstanding), run.Ending);
    }

    /// <summary>Evaluates one batch of the requested run and commits it together with the position it reached.</summary>
    private async Task<MailRuleEvaluationRun> CarryRunBatchAsync(
        MailRuleEvaluationRun run,
        MailRuleSet ruleSet,
        bool requiresExtractedBodyText,
        EvaluationTally tally,
        CancellationToken cancellationToken)
    {
        var batch = await this.store.GetStoredEmailsAsync(
            run.AccountId,
            run.Position,
            this.options.BatchSize,
            cancellationToken);

        if (batch.Count == 0)
        {
            var finished = run with
            {
                EndedAt = this.timeProvider.GetUtcNow(),
                Ending = MailRuleEvaluationRunEnding.Completed,
            };

            await this.CommitRunAsync(finished, cancellationToken);

            return finished;
        }

        var outcome = await this.EvaluateBatchAsync(ruleSet, requiresExtractedBodyText, batch, cancellationToken);
        var reachedTheEnd = batch.Count < this.options.BatchSize;
        var recordedAt = this.timeProvider.GetUtcNow();

        var carried = run with
        {
            Position = batch[^1].StoredEmailId,
            EvaluatedEmailCount = run.EvaluatedEmailCount + outcome.EvaluatedEmailIds.Count,
            MatchedEmailCount = run.MatchedEmailCount + outcome.MatchedEmailCount,
            SkippedEmailCount = run.SkippedEmailCount + outcome.SkippedEmailCount,
            EndedAt = reachedTheEnd ? recordedAt : null,
            Ending = reachedTheEnd ? MailRuleEvaluationRunEnding.Completed : null,
        };

        await this.commitPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                await this.store.RecordEvaluatedAsync(
                    session,
                    outcome.EvaluatedEmailIds,
                    recordedAt,
                    attemptCancellationToken);

                await this.RecordDecisionsAsync(
                    session,
                    ruleSet.Revision,
                    outcome,
                    MailRuleExecutionTrigger.RequestedRun,
                    attemptCancellationToken);

                await this.runStore.SaveAsync(session, carried, attemptCancellationToken);
            },
            cancellationToken);

        tally.Add(outcome);

        return carried;
    }

    /// <summary>Writes down what the batch decided and every change it asked for, inside the batch's own transaction.</summary>
    /// <remarks>
    /// The two are one step because the history has to name the mutation record each requested action opened, and that
    /// identity exists only once the request has been written. Composing the explanation from the recording rather than
    /// from the plan is what keeps the record a pointer at the mutation trail instead of a second copy of it.
    /// <para>
    /// The counts are reset at the start rather than added to, because the commit policy re-runs this whole delegate
    /// when it loses an optimistic race. Adding to them would count the losing attempt's records as well as the winning
    /// one's, and the losing attempt wrote nothing.
    /// </para>
    /// </remarks>
    private async Task RecordDecisionsAsync(
        IPersistenceSession session,
        MailRuleSetRevision revision,
        MailRuleEvaluationBatch outcome,
        MailRuleExecutionTrigger trigger,
        CancellationToken cancellationToken)
    {
        outcome.ForgetRecordedActions();

        var executions = new List<MailRuleExecution>();

        foreach (var evaluated in outcome.EvaluatedEmails)
        {
            var plan = evaluated.Evaluation.ActionPlan;
            var recording = plan.IsEmpty
                ? MailRuleActionRecording.Nothing
                : await this.actionRecorder.RecordAsync(
                    session,
                    evaluated.Candidate.StoredEmailId,
                    evaluated.Candidate.Occurrence,
                    plan,
                    revision,
                    cancellationToken);

            outcome.ActionsRecorded(recording);

            executions.AddRange(MailRuleExecutionComposer.Compose(
                evaluated.Candidate.Occurrence.AccountId,
                evaluated.Candidate.StoredEmailId,
                evaluated.Evaluation,
                trigger,
                recording,
                evaluated.EvaluatedAt));
        }

        await this.executionStore.AppendAsync(session, executions, cancellationToken);
    }

    private Task CommitRunAsync(MailRuleEvaluationRun run, CancellationToken cancellationToken) =>
        this.commitPolicy.CommitAsync(
            (session, attemptCancellationToken) => this.runStore.SaveAsync(session, run, attemptCancellationToken),
            cancellationToken);

    /// <summary>Evaluates the rule set for each email of one batch, outside any transaction.</summary>
    /// <remarks>
    /// The evaluation instant is taken per email rather than per batch, because it is what the age fact is measured
    /// against and an email is one reading of one message at one moment. Nothing here catches a failure: the evaluator
    /// is where totality is applied, so a condition that cannot answer for one email is already a failed rule by the
    /// time it reaches this tally and the emails behind it are unaffected.
    /// </remarks>
    private async Task<MailRuleEvaluationBatch> EvaluateBatchAsync(
        MailRuleSet ruleSet,
        bool requiresExtractedBodyText,
        IReadOnlyList<StoredEmailAwaitingRuleEvaluation> batch,
        CancellationToken cancellationToken)
    {
        var outcome = new MailRuleEvaluationBatch(batch.Count);

        foreach (var candidate in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // An email whose text is still expected is left in the queue rather than evaluated against a fact that
            // would answer absent and then never be reconsidered. One whose content will never yield text is evaluated
            // now, because waiting for it would stall this account's queue behind a message that can never become
            // eligible.
            if (requiresExtractedBodyText && candidate.AwaitsExtraction)
            {
                outcome.Skipped();

                continue;
            }

            var evaluatedAt = this.timeProvider.GetUtcNow();
            var facts = new MailRuleFacts(
                candidate.Facts,
                new StoredEmailBodyTextReader(this.store, candidate.StoredEmailId),
                evaluatedAt);

            var evaluation = await this.evaluator.EvaluateAsync(ruleSet, facts, cancellationToken);

            outcome.Evaluated(candidate, evaluation, evaluatedAt);
        }

        return outcome;
    }

    /// <summary>What one batch's evaluations produced, before any of them were committed.</summary>
    private sealed class MailRuleEvaluationBatch
    {
        internal MailRuleEvaluationBatch(int capacity) => this.EvaluatedEmailIds = new List<StoredEmailId>(capacity);

        internal List<StoredEmailId> EvaluatedEmailIds { get; }

        internal int MatchedEmailCount { get; private set; }

        internal int SkippedEmailCount { get; private set; }

        internal int FailedRuleCount { get; private set; }

        internal int TimedOutRuleCount { get; private set; }

        internal int RequestedActionCount { get; private set; }

        internal int WithheldActionCount { get; private set; }

        internal int FailedActionCount { get; private set; }

        internal SortedSet<string> MatchedRuleNames { get; } = new(StringComparer.Ordinal);

        internal SortedSet<string> FailedRuleNames { get; } = new(StringComparer.Ordinal);

        internal SortedSet<string> UnappliedActionRuleNames { get; } = new(StringComparer.Ordinal);

        internal List<EvaluatedEmail> EvaluatedEmails { get; } = [];

        internal void Skipped() => this.SkippedEmailCount++;

        internal void Evaluated(
            StoredEmailAwaitingRuleEvaluation candidate,
            MailRuleSetEvaluation evaluation,
            DateTimeOffset evaluatedAt)
        {
            this.EvaluatedEmailIds.Add(candidate.StoredEmailId);
            this.EvaluatedEmails.Add(new EvaluatedEmail(candidate, evaluation, evaluatedAt));

            if (evaluation.MatchedRuleNames.Count > 0)
            {
                this.MatchedEmailCount++;
                this.MatchedRuleNames.UnionWith(evaluation.MatchedRuleNames);
            }

            foreach (var failed in evaluation.Evaluations.Where(rule => rule.Outcome == MailRuleOutcome.Failed))
            {
                this.FailedRuleCount++;
                this.FailedRuleNames.Add(failed.RuleName);

                if (failed.Failure == MailRuleConditionFailure.EvaluationTimedOut)
                {
                    this.TimedOutRuleCount++;
                }
            }

            // The withheld actions are counted where the plan is composed rather than where it is recorded, because
            // nothing about them reaches the mutation trail and a retried commit would otherwise count them twice. The
            // count is of actions rather than of the rules that declared them, which is what the walk reports it as: one
            // rule declaring two changes can have one honored and one withheld.
            this.WithheldActionCount += evaluation.ActionPlan.WithheldActions.Count;
            this.UnappliedActionRuleNames.UnionWith(evaluation.ActionPlan.WithheldRuleNames);
        }

        /// <summary>Drops what a commit attempt recorded, so a retry of it counts its own writes and not the lost ones.</summary>
        internal void ForgetRecordedActions()
        {
            this.RequestedActionCount = 0;
            this.FailedActionCount = 0;
        }

        internal void ActionsRecorded(MailRuleActionRecording recording)
        {
            this.RequestedActionCount += recording.RecordedCount;
            this.FailedActionCount += recording.Failures.Count;
            this.UnappliedActionRuleNames.UnionWith(recording.Failures.Select(failure => failure.RuleName));
        }
    }

    /// <summary>One email the batch evaluated, held with its conclusion until the batch commits.</summary>
    /// <remarks>
    /// The whole evaluation rather than only the plan it produced, because the history records what every rule the pass
    /// reached concluded — including the rules that asked for nothing, which are most of them.
    /// </remarks>
    private sealed record EvaluatedEmail(
        StoredEmailAwaitingRuleEvaluation Candidate,
        MailRuleSetEvaluation Evaluation,
        DateTimeOffset EvaluatedAt);

    /// <summary>Adds up the batches of one walk into the counts an operator reads.</summary>
    private sealed class EvaluationTally
    {
        private readonly SortedSet<string> matchedRuleNames = new(StringComparer.Ordinal);
        private readonly SortedSet<string> failedRuleNames = new(StringComparer.Ordinal);
        private readonly SortedSet<string> unappliedActionRuleNames = new(StringComparer.Ordinal);

        private int evaluatedEmailCount;
        private int matchedEmailCount;
        private int skippedEmailCount;
        private int failedRuleCount;
        private int timedOutRuleCount;
        private int requestedActionCount;
        private int withheldActionCount;
        private int failedActionCount;

        internal void Add(MailRuleEvaluationBatch batch)
        {
            this.evaluatedEmailCount += batch.EvaluatedEmailIds.Count;
            this.matchedEmailCount += batch.MatchedEmailCount;
            this.skippedEmailCount += batch.SkippedEmailCount;
            this.failedRuleCount += batch.FailedRuleCount;
            this.timedOutRuleCount += batch.TimedOutRuleCount;
            this.requestedActionCount += batch.RequestedActionCount;
            this.withheldActionCount += batch.WithheldActionCount;
            this.failedActionCount += batch.FailedActionCount;
            this.matchedRuleNames.UnionWith(batch.MatchedRuleNames);
            this.failedRuleNames.UnionWith(batch.FailedRuleNames);
            this.unappliedActionRuleNames.UnionWith(batch.UnappliedActionRuleNames);
        }

        internal MailRuleEvaluationWalk ToWalk(bool emailsRemain) => new()
        {
            EvaluatedEmailCount = this.evaluatedEmailCount,
            MatchedEmailCount = this.matchedEmailCount,
            SkippedEmailCount = this.skippedEmailCount,
            FailedRuleCount = this.failedRuleCount,
            TimedOutRuleCount = this.timedOutRuleCount,
            RequestedActionCount = this.requestedActionCount,
            WithheldActionCount = this.withheldActionCount,
            FailedActionCount = this.failedActionCount,
            MatchedRuleNames = [.. this.matchedRuleNames],
            FailedRuleNames = [.. this.failedRuleNames],
            UnappliedActionRuleNames = [.. this.unappliedActionRuleNames],
            EmailsRemain = emailsRemain,
        };
    }

    /// <summary>What the requested-run walk produced, keeping "no run outstanding" apart from "a run that did nothing".</summary>
    private readonly record struct RequestedRunOutcome(
        MailRuleEvaluationWalk? Walk,
        MailRuleEvaluationRunEnding? Ending);
}
