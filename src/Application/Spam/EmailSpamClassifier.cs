// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Scanning;
using MailFathom.Application.Spam.Signals;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam;

/// <summary>Classifies one stored occurrence and records what it concluded.</summary>
/// <remarks>
/// <para>
/// The operation is keyed to the occurrence and is idempotent: repeating it either leaves an existing record alone or
/// replaces it with the same verdict the same inputs produce, so it is safe for a caller to retry and safe for two
/// callers to ask together. It carries no scheduler, no retry policy of its own, and no execution identity — whatever
/// asks for a classification owns those.
/// </para>
/// <para>
/// It reaches no mail server. Content comes from the local store, which already holds it, so classification never
/// triggers an IMAP fetch and cannot affect a remote <c>\Seen</c> flag. It writes nothing but the classification: no
/// folder, no flag, and nothing about where the message lives.
/// </para>
/// <para>
/// Nothing it reads is loggable. The occurrence identifier, the folder alias, the outcome, and the verdict are safe to
/// report; a header value, an authentication detail, and a subject are not, and none of the three reaches a log line
/// from here.
/// </para>
/// </remarks>
public sealed class EmailSpamClassifier
{
    private readonly IClassifiableEmailReader emailReader;
    private readonly IEmailContentStore contentStore;
    private readonly IEmailSpamHeaderReader headerReader;
    private readonly IJunkMailFolderCatalog junkFolders;
    private readonly DeterministicSpamClassifier deterministicClassifier;
    private readonly ISpamClassificationSettingsReader settingsReader;
    private readonly IEmailSpamClassificationStore classificationStore;
    private readonly OptimisticConcurrencyRetryPolicy retryPolicy;
    private readonly TimeProvider timeProvider;
    private readonly ISpamScanner? scanner;

    /// <summary>Initializes the use case.</summary>
    /// <param name="emailReader">Finds the account and folder of the occurrence.</param>
    /// <param name="contentStore">Reads the raw MIME already stored for it.</param>
    /// <param name="headerReader">Reads the spam-relevant headers out of that content.</param>
    /// <param name="junkFolders">Answers whether the occurrence's folder is its account's junk folder.</param>
    /// <param name="deterministicClassifier">Reaches a verdict from what the message already carried.</param>
    /// <param name="settingsReader">Answers what the operator decided.</param>
    /// <param name="classificationStore">Records the classification.</param>
    /// <param name="retryPolicy">Commits the record from a fresh read when a concurrent write conflicts.</param>
    /// <param name="timeProvider">Stamps the evaluation time.</param>
    /// <param name="scanner">Scores the whole message, or <see langword="null" /> when no scanner is registered.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument but <paramref name="scanner" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The scanner is the one optional dependency, because a deployment with no sidecar registers no implementation of
    /// the port and the deterministic stage is the whole working feature without one. An absent scanner is therefore a
    /// supported deployment rather than a misconfiguration, which is why it is a nullable dependency instead of a guard.
    /// </remarks>
    public EmailSpamClassifier(
        IClassifiableEmailReader emailReader,
        IEmailContentStore contentStore,
        IEmailSpamHeaderReader headerReader,
        IJunkMailFolderCatalog junkFolders,
        DeterministicSpamClassifier deterministicClassifier,
        ISpamClassificationSettingsReader settingsReader,
        IEmailSpamClassificationStore classificationStore,
        OptimisticConcurrencyRetryPolicy retryPolicy,
        TimeProvider timeProvider,
        ISpamScanner? scanner = null)
    {
        ArgumentNullException.ThrowIfNull(emailReader);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(headerReader);
        ArgumentNullException.ThrowIfNull(junkFolders);
        ArgumentNullException.ThrowIfNull(deterministicClassifier);
        ArgumentNullException.ThrowIfNull(settingsReader);
        ArgumentNullException.ThrowIfNull(classificationStore);
        ArgumentNullException.ThrowIfNull(retryPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.emailReader = emailReader;
        this.contentStore = contentStore;
        this.headerReader = headerReader;
        this.junkFolders = junkFolders;
        this.deterministicClassifier = deterministicClassifier;
        this.settingsReader = settingsReader;
        this.classificationStore = classificationStore;
        this.retryPolicy = retryPolicy;
        this.timeProvider = timeProvider;
        this.scanner = scanner;
    }

    /// <summary>Classifies one occurrence.</summary>
    /// <param name="emailId">The occurrence to classify.</param>
    /// <param name="mode">What to do about an occurrence that already carries a classification.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>What was recorded, or the reason nothing was.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="mode" /> is not a defined member.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when every allowed commit attempt conflicted.</exception>
    /// <remarks>
    /// The order of the checks is the order of what they cost. Whether classification is on at all is free, the scope
    /// and the existing record are one lookup each, and only then is content read — so an occurrence outside the scope
    /// costs no read of its mail, which is the property that keeps a switched-off feature free.
    /// </remarks>
    public async Task<SpamClassificationResult> ClassifyAsync(
        StoredEmailId emailId,
        SpamClassificationMode mode,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "A classification either leaves an existing record alone or replaces it.");
        }

        var settings = this.settingsReader.Settings;

        if (!settings.IsEnabled)
        {
            return SpamClassificationResult.NotClassified(SpamClassificationOutcome.Disabled);
        }

        var email = await this.emailReader.FindAsync(emailId, cancellationToken);

        if (email is null)
        {
            return SpamClassificationResult.NotClassified(SpamClassificationOutcome.OccurrenceMissing);
        }

        if (!settings.Covers(email.FolderAlias))
        {
            return SpamClassificationResult.NotClassified(SpamClassificationOutcome.OutsideConfiguredScope);
        }

        if (mode is SpamClassificationMode.FirstTimeOnly
            && await this.classificationStore.FindAsync(emailId, cancellationToken) is not null)
        {
            return SpamClassificationResult.NotClassified(SpamClassificationOutcome.AlreadyClassified);
        }

        var content = await this.contentStore.FindStoredContentAsync(emailId, cancellationToken);

        if (content is null)
        {
            return SpamClassificationResult.NotClassified(SpamClassificationOutcome.ContentUnavailable);
        }

        var classification = await this.EvaluateAsync(email, settings, content, cancellationToken);

        await this.retryPolicy.CommitAsync(
            (session, attemptCancellationToken) =>
                this.classificationStore.SaveAsync(session, classification, attemptCancellationToken),
            cancellationToken);

        return SpamClassificationResult.Classified(classification);
    }

    /// <summary>Runs both stages over one occurrence and composes the record they produce.</summary>
    private async Task<SpamClassification> EvaluateAsync(
        ClassifiableEmail email,
        SpamClassificationSettings settings,
        StoredEmailContent content,
        CancellationToken cancellationToken)
    {
        var facts = await this.headerReader.ReadAsync(content, cancellationToken);
        var reading = this.deterministicClassifier.Read(
            facts,
            email.FolderAlias,
            this.junkFolders.IsJunkFolder(email.AccountId, email.FolderAlias));

        var scan = settings.UsesScanner && this.scanner is not null
            ? await this.scanner.ScanAsync(content, cancellationToken)
            : null;

        var decision = Decide(reading, JudgedBy(scan, settings.ScannerThreshold));

        return SpamClassification.Create(
            email.Id,
            decision.Verdict,
            decision.DecidedBy,
            decision.Assessment,
            decision.CorpusRevision,
            settings.Profile,
            [.. reading.Signals, .. ScannerSignals(scan)],
            this.timeProvider.GetUtcNow());
    }

    /// <summary>Re-judges a scanner's score by the threshold the operator configured, where they configured one.</summary>
    /// <remarks>
    /// The threshold is replaced rather than compared beside the scanner's own, so the record states one pair of numbers
    /// in one scale and a reader is never left deciding which of two thresholds a verdict was reached under. A scanner
    /// that answered with anything but a score is returned unchanged: there is nothing to re-judge.
    /// </remarks>
    private static SpamScanResult? JudgedBy(SpamScanResult? scan, double? configuredThreshold)
    {
        if (configuredThreshold is not { } threshold
            || scan is not { Assessment: { } assessment, CorpusRevision: { } corpusRevision })
        {
            return scan;
        }

        return SpamScanResult.Scored(
            SpamAssessment.Create(assessment.Score, threshold),
            scan.FiredRules,
            corpusRevision);
    }

    /// <summary>Decides which stage's verdict the record carries.</summary>
    /// <remarks>
    /// A deterministic verdict of spam stands whatever the scanner says. It rests on the provider's own decision or on
    /// where the mailbox filed the message, both taken with context nothing after delivery has, and a scanner that
    /// disagreed would be re-reading the message from a mailbox without the network view the receiving server had.
    /// Otherwise the scanner decides when it scored, which is what an operator who deployed one asked for; a scanner
    /// that did not answer leaves the deterministic verdict exactly as it was, including undetermined.
    /// </remarks>
    private static (SpamVerdict Verdict, SpamClassificationStage DecidedBy, SpamAssessment? Assessment, string? CorpusRevision) Decide(
        DeterministicSpamReading reading,
        SpamScanResult? scan)
    {
        if (reading.Verdict is SpamVerdict.Spam)
        {
            return (SpamVerdict.Spam, SpamClassificationStage.Deterministic, reading.Assessment, null);
        }

        if (scan is { Outcome: SpamScanOutcome.Scored, Assessment: { } assessment, CorpusRevision: { } corpusRevision })
        {
            return (
                assessment.ClearsThreshold ? SpamVerdict.Spam : SpamVerdict.NotSpam,
                SpamClassificationStage.Scanner,
                assessment,
                corpusRevision);
        }

        return (reading.Verdict, SpamClassificationStage.Deterministic, reading.Assessment, null);
    }

    private static IEnumerable<SpamSignal> ScannerSignals(SpamScanResult? scan) =>
        scan is { CorpusRevision: { } corpusRevision }
            ? scan.FiredRules.Select(rule => SpamSignal.Create(
                SpamSignalKind.ScannerRule,
                rule,
                observation: null,
                SpamSignalProvenance.FromScannerCorpus(corpusRevision)))
            : [];
}
