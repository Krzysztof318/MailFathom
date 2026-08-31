// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Actions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam.Runs;

/// <summary>Carries an account's whole-mailbox classification run as far as one pass's batch budget reaches.</summary>
/// <remarks>
/// <para>
/// A step of the account's synchronization run rather than a schedule of its own, exactly as rule evaluation is. That
/// run already has per-account isolation, a jittered backoff, a slot count that stops one account starving another, and
/// a failure path that defers the account instead of the process; a classification walk needs every one of those and
/// none of them differently. It follows that only one pass per account is ever in flight, structurally rather than by a
/// lock — which is the other half of what makes one outstanding run per account mean one walk of one mailbox.
/// </para>
/// <para>
/// It reaches no mail server for the mail it reads. Every message it scores was committed by an earlier run and is read
/// through the local content store, so the MCP-reads-are-local invariant and the remote <c>\Seen</c> flag are both
/// untouched however long a walk takes. What it can reach the network for is a scanner sidecar, which is a call the
/// adapter bounds, and — where the run acts and a filing has to find its folder — the destination resolution every
/// author of a mutation goes through.
/// </para>
/// <para>
/// The run's terms are read from the run rather than from configuration. A walk that spans hours must mean the same
/// thing at its end as at its start, so the scope, the posture, and whether it rescores are what the operator asked for
/// and not what the file says now. What configuration is still read for is the profile, and a profile that has moved
/// ends the run rather than being applied to the half of the mailbox that is left.
/// </para>
/// <para>
/// Each batch commits the position it reached, so a restart resumes at the message nobody scored. The classification of
/// one message is committed by the use case that reaches it rather than in the batch's own transaction, which is a
/// deliberate difference from the rule pass and costs exactly one thing: a crash inside a batch re-reaches that batch's
/// messages, whose records are upserts of the same verdict and whose mutation requests are refused as already asked for,
/// so nothing is duplicated in the database or on the mail server and only the run's own counts can double-count what
/// the lost attempt did.
/// </para>
/// </remarks>
public sealed class SpamClassificationPass
{
    private readonly ISpamClassificationRunStore runStore;
    private readonly IClassifiableEmailReader emails;
    private readonly IEmailSpamClassificationStore classifications;
    private readonly EmailSpamClassifier classifier;
    private readonly SpamActionRecorder actionRecorder;
    private readonly ISpamClassificationSettingsReader settingsReader;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly SpamClassificationRunOptions options;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the pass from the run it carries and the local state it walks.</summary>
    /// <param name="runStore">Reads the requested run and records how far it has been carried.</param>
    /// <param name="emails">Reads the account's stored occurrences in the run's scope, in identity order.</param>
    /// <param name="classifications">Answers what an occurrence was already decided as, and under which terms.</param>
    /// <param name="classifier">Scores an occurrence and records the verdict.</param>
    /// <param name="actionRecorder">Applies the run's posture to a verdict, writing the changes down or only working them out.</param>
    /// <param name="settingsReader">Answers whether the account's owner classifies and what profile their mail runs under.</param>
    /// <param name="commitPolicy">Commits the run's position and counts.</param>
    /// <param name="options">Bounds one pass.</param>
    /// <param name="timeProvider">Stamps the instant a run ends.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a configured bound is below one.</exception>
    public SpamClassificationPass(
        ISpamClassificationRunStore runStore,
        IClassifiableEmailReader emails,
        IEmailSpamClassificationStore classifications,
        EmailSpamClassifier classifier,
        SpamActionRecorder actionRecorder,
        ISpamClassificationSettingsReader settingsReader,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        SpamClassificationRunOptions options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(emails);
        ArgumentNullException.ThrowIfNull(classifications);
        ArgumentNullException.ThrowIfNull(classifier);
        ArgumentNullException.ThrowIfNull(actionRecorder);
        ArgumentNullException.ThrowIfNull(settingsReader);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.BatchSize, 1, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxBatchesPerPass, 1, nameof(options));

        this.runStore = runStore;
        this.emails = emails;
        this.classifications = classifications;
        this.classifier = classifier;
        this.actionRecorder = actionRecorder;
        this.settingsReader = settingsReader;
        this.commitPolicy = commitPolicy;
        this.options = options;
        this.timeProvider = timeProvider;
    }

    /// <summary>Takes one bounded pass over the account's requested run, where it has one.</summary>
    /// <param name="account">The account whose run is carried, named by its owner and its identifier.</param>
    /// <param name="cancellationToken">Cancels the pass between messages and between batches; committed batches stay durable.</param>
    /// <returns>What the pass did, and how the run ended where this pass ended it.</returns>
    /// <exception cref="PersistenceConcurrencyConflictException">
    /// Thrown when a competing writer wins a race the bounded retries could not resolve. Batches already committed stay
    /// durable and the next account run resumes from them.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels. Committed batches stay durable.</exception>
    /// <remarks>
    /// The two ways a run ends without reaching the end of its mail are both decided here and before any message is
    /// read, because both are statements about the whole run rather than about one message: the account's owner
    /// classifying nothing, and a profile that has moved under a walk already half done.
    /// </remarks>
    public async Task<SpamClassificationRunReport> RunAsync(
        MailAccountIdentity account,
        CancellationToken cancellationToken)
    {
        var run = await this.runStore.FindOutstandingAsync(account, cancellationToken);

        if (run is null)
        {
            return SpamClassificationRunReport.NoRun;
        }

        var settings = this.settingsReader.SettingsFor(account.Owner);

        if (!settings.IsEnabled)
        {
            await this.EndRunAsync(run, SpamClassificationRunEnding.Disabled, cancellationToken);

            return new SpamClassificationRunReport(
                run.Profile,
                SpamClassificationWalk.Empty,
                SpamClassificationRunEnding.Disabled);
        }

        if (run.Profile.IsSpecified && run.Profile != settings.Profile)
        {
            await this.EndRunAsync(run, SpamClassificationRunEnding.Superseded, cancellationToken);

            return new SpamClassificationRunReport(
                run.Profile,
                SpamClassificationWalk.Empty,
                SpamClassificationRunEnding.Superseded);
        }

        if (!run.Profile.IsSpecified)
        {
            run = run with { Profile = settings.Profile };
        }

        var tally = new ClassificationTally();

        for (var batchNumber = 1; batchNumber <= this.options.MaxBatchesPerPass && run.IsOutstanding; batchNumber++)
        {
            run = await this.CarryBatchAsync(run, tally, cancellationToken);
        }

        return new SpamClassificationRunReport(run.Profile, tally.ToWalk(run.IsOutstanding), run.Ending);
    }

    /// <summary>Classifies one batch of the run and commits the position and counts it reached.</summary>
    /// <remarks>
    /// A batch shorter than the budget is the end of the scope rather than a batch that happened to be small, because the
    /// read is a keyset walk that stops only when the mail does. Ending the run on it rather than on the next empty read
    /// saves the account a whole run's wait for an answer it already has.
    /// </remarks>
    private async Task<SpamClassificationRun> CarryBatchAsync(
        SpamClassificationRun run,
        ClassificationTally tally,
        CancellationToken cancellationToken)
    {
        var batch = await this.emails.GetStoredEmailsAsync(
            run.Account,
            run.Terms.FolderAliases,
            run.Position,
            this.options.BatchSize,
            cancellationToken);

        if (batch.Count == 0)
        {
            var finished = this.Ended(run, SpamClassificationRunEnding.Completed);

            await this.CommitRunAsync(finished, cancellationToken);

            return finished;
        }

        var batchTally = new ClassificationTally();

        foreach (var candidate in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await this.ClassifyAsync(run, candidate, batchTally, cancellationToken);
        }

        var reachedTheEnd = batch.Count < this.options.BatchSize;
        var walk = batchTally.ToWalk(emailsRemain: !reachedTheEnd);

        var carried = run with
        {
            Position = batch[^1].Id,
            ClassifiedEmailCount = run.ClassifiedEmailCount + walk.ClassifiedEmailCount,
            SpamEmailCount = run.SpamEmailCount + walk.SpamEmailCount,
            UndeterminedEmailCount = run.UndeterminedEmailCount + walk.UndeterminedEmailCount,
            SkippedEmailCount = run.SkippedEmailCount + walk.SkippedEmailCount,
            UnclassifiableEmailCount = run.UnclassifiableEmailCount + walk.UnclassifiableEmailCount,
            ActedEmailCount = run.ActedEmailCount + walk.ActedEmailCount,
        };

        if (reachedTheEnd)
        {
            carried = this.Ended(carried, SpamClassificationRunEnding.Completed);
        }

        await this.CommitRunAsync(carried, cancellationToken);

        tally.Add(batchTally);

        return carried;
    }

    /// <summary>Reaches a verdict about one occurrence, or reuses the one it already carries, and applies the posture.</summary>
    /// <remarks>
    /// The existing record is read first and only where the run does not rescore, which is what makes a run over a
    /// mailbox that has already been scored cost one lookup per message instead of one scanner call. It is reused only
    /// where it names the profile the run is bound to: a record from before the profile was part of one, or from before
    /// the operator moved a threshold, was decided under terms the run cannot compare and is therefore reached again.
    /// <para>
    /// A reused verdict is still put to the recorder. Skipping the scoring is a saving; skipping the action would make
    /// switching filing on a run that files nothing, which is one of the two reasons this run exists.
    /// </para>
    /// </remarks>
    private async Task ClassifyAsync(
        SpamClassificationRun run,
        ClassifiableEmail candidate,
        ClassificationTally tally,
        CancellationToken cancellationToken)
    {
        var existing = run.Terms.Rescores
            ? null
            : await this.classifications.FindAsync(candidate.Id, cancellationToken);

        SpamClassification classification;

        if (existing is not null && existing.Profile.IsSpecified && existing.Profile == run.Profile)
        {
            classification = existing;
            tally.Skipped(existing.Verdict);
        }
        else
        {
            var result = await this.classifier.ClassifyAsync(
                run.Account.Owner,
                candidate.Id,
                SpamClassificationMode.Reclassify,
                cancellationToken);

            if (result.Classification is not { } scored)
            {
                tally.Unclassifiable();

                return;
            }

            classification = scored;
            tally.Classified(scored.Verdict);
        }

        var action = await this.actionRecorder.RecordAsync(
            run.Account.Owner,
            classification,
            run.Terms.Posture,
            cancellationToken);

        if (action.Outcome is SpamActionOutcome.Requested or SpamActionOutcome.WouldRequest)
        {
            tally.Acted();
        }
    }

    private SpamClassificationRun Ended(SpamClassificationRun run, SpamClassificationRunEnding ending) => run with
    {
        EndedAt = this.timeProvider.GetUtcNow(),
        Ending = ending,
    };

    private Task EndRunAsync(
        SpamClassificationRun run,
        SpamClassificationRunEnding ending,
        CancellationToken cancellationToken) =>
        this.CommitRunAsync(this.Ended(run, ending), cancellationToken);

    private Task CommitRunAsync(SpamClassificationRun run, CancellationToken cancellationToken) =>
        this.commitPolicy.CommitAsync(
            (session, attemptCancellationToken) => this.runStore.SaveAsync(session, run, attemptCancellationToken),
            cancellationToken);

    /// <summary>Adds up what a batch, and then a whole pass, did to the mail it reached.</summary>
    private sealed class ClassificationTally
    {
        private int classifiedEmailCount;
        private int spamEmailCount;
        private int undeterminedEmailCount;
        private int skippedEmailCount;
        private int unclassifiableEmailCount;
        private int actedEmailCount;

        internal void Classified(SpamVerdict verdict)
        {
            this.classifiedEmailCount++;
            this.CountVerdict(verdict);
        }

        internal void Skipped(SpamVerdict verdict)
        {
            this.skippedEmailCount++;
            this.CountVerdict(verdict);
        }

        internal void Unclassifiable() => this.unclassifiableEmailCount++;

        internal void Acted() => this.actedEmailCount++;

        internal void Add(SpamClassificationWalk walk)
        {
            this.classifiedEmailCount += walk.ClassifiedEmailCount;
            this.spamEmailCount += walk.SpamEmailCount;
            this.undeterminedEmailCount += walk.UndeterminedEmailCount;
            this.skippedEmailCount += walk.SkippedEmailCount;
            this.unclassifiableEmailCount += walk.UnclassifiableEmailCount;
            this.actedEmailCount += walk.ActedEmailCount;
        }

        internal void Add(ClassificationTally batch) => this.Add(batch.ToWalk(emailsRemain: false));

        internal SpamClassificationWalk ToWalk(bool emailsRemain) => new()
        {
            ClassifiedEmailCount = this.classifiedEmailCount,
            SpamEmailCount = this.spamEmailCount,
            UndeterminedEmailCount = this.undeterminedEmailCount,
            SkippedEmailCount = this.skippedEmailCount,
            UnclassifiableEmailCount = this.unclassifiableEmailCount,
            ActedEmailCount = this.actedEmailCount,
            EmailsRemain = emailsRemain,
        };

        private void CountVerdict(SpamVerdict verdict)
        {
            switch (verdict)
            {
                case SpamVerdict.Spam:
                    this.spamEmailCount++;

                    break;
                case SpamVerdict.Undetermined:
                    this.undeterminedEmailCount++;

                    break;
                default:
                    break;
            }
        }
    }
}
