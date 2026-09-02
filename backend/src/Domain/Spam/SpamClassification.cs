// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Spam;

/// <summary>What classification concluded about one stored message occurrence, and everything it concluded it from.</summary>
/// <remarks>
/// <para>
/// This is derived data of the same kind as an embedding: computed from a local copy, never mirrored back to the mail
/// server, and never a statement about which folder the message is in or which flags it carries. It inherits the
/// classification, retention, access, and erasure constraints of the message it describes, and it is reachable by
/// whatever reaches that message — the record hangs off the occurrence rather than living beside it.
/// </para>
/// <para>
/// It carries no subject, no address, and no body. What a signal's observation can carry is a sending domain and an
/// authentication outcome, which is why nothing here is loggable and only counts, kinds, scores, and rule names may be
/// reported in telemetry.
/// </para>
/// </remarks>
public sealed record SpamClassification
{
    /// <summary>The greatest number of signals one classification records.</summary>
    /// <remarks>
    /// A scanner corpus can fire dozens of rules on one message and a provider can write several headers, so the bound
    /// is generous against both while keeping one message's derived data bounded. Whatever produces more signals than
    /// this keeps the first of them, because a record that refused the message outright would lose the verdict as well.
    /// </remarks>
    public const int MaximumSignals = 64;

    /// <summary>The greatest length a corpus revision may carry.</summary>
    public const int MaximumCorpusRevisionLength = 128;

    private SpamClassification(
        StoredEmailId emailId,
        SpamVerdict verdict,
        SpamClassificationStage decidedBy,
        SpamAssessment? assessment,
        string? corpusRevision,
        SpamClassificationProfile profile,
        IReadOnlyList<SpamSignal> signals,
        DateTimeOffset evaluatedAt)
    {
        this.EmailId = emailId;
        this.Verdict = verdict;
        this.DecidedBy = decidedBy;
        this.Assessment = assessment;
        this.CorpusRevision = corpusRevision;
        this.Profile = profile;
        this.Signals = signals;
        this.EvaluatedAt = evaluatedAt;
    }

    /// <summary>Gets the stored occurrence this classification is about.</summary>
    public StoredEmailId EmailId { get; }

    /// <summary>Gets what the classification concluded.</summary>
    public SpamVerdict Verdict { get; }

    /// <summary>Gets which stage reached the verdict.</summary>
    public SpamClassificationStage DecidedBy { get; }

    /// <summary>Gets the score and the threshold it was judged against, or <see langword="null" /> when no stage produced a number.</summary>
    public SpamAssessment? Assessment { get; }

    /// <summary>Gets the rule corpus the deciding stage ran under, or <see langword="null" /> when it has none.</summary>
    /// <remarks>
    /// The deterministic stage has no corpus: it reads what the receiving server already wrote, so what identifies its
    /// provenance is the header field name each of its signals names. A scanner has one, and it is what makes
    /// reclassification under a newer corpus a question somebody can ask of an existing record.
    /// </remarks>
    public string? CorpusRevision { get; }

    /// <summary>Gets the settings the verdict was reached under, or the unspecified value for a record that names none.</summary>
    /// <remarks>
    /// The other half of the provenance the corpus revision states. A corpus is the scanner's own identity for what it
    /// scored with, and this is MailFathom's identity for the terms it asked under — whether a scanner was consulted at
    /// all, and the threshold its answer was judged by. Together they are what makes "already decided under the terms in
    /// force now" answerable from the record rather than guessed at from its age, which is what a run over a whole
    /// mailbox skips mail on. A record written before the profile was part of one names none.
    /// </remarks>
    public SpamClassificationProfile Profile { get; }

    /// <summary>Gets every fact the verdict rests on, in the order the stages produced them.</summary>
    public IReadOnlyList<SpamSignal> Signals { get; }

    /// <summary>Gets when the classification was evaluated.</summary>
    public DateTimeOffset EvaluatedAt { get; }

    /// <summary>Records what a stage concluded about one occurrence.</summary>
    /// <param name="emailId">The stored occurrence classified.</param>
    /// <param name="verdict">What was concluded.</param>
    /// <param name="decidedBy">Which stage reached the verdict.</param>
    /// <param name="assessment">The score and threshold, or <see langword="null" /> when no number was produced.</param>
    /// <param name="corpusRevision">The rule corpus the deciding stage ran under, or <see langword="null" /> when it has none.</param>
    /// <param name="profile">The settings the verdict was reached under, or the unspecified value for a record that names none.</param>
    /// <param name="signals">The facts the verdict rests on.</param>
    /// <param name="evaluatedAt">When the classification was evaluated.</param>
    /// <returns>The classification, holding no more than <see cref="MaximumSignals" /> signals.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="signals" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the corpus revision is blank, over-long, or carries a control character.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the verdict or the stage is not a defined member.</exception>
    /// <remarks>
    /// An unspecified profile is accepted rather than refused, because a record written before the profile was part of
    /// one has to be readable: refusing it here would make a row this system itself wrote unreadable to the code that
    /// replaced it.
    /// </remarks>
    public static SpamClassification Create(
        StoredEmailId emailId,
        SpamVerdict verdict,
        SpamClassificationStage decidedBy,
        SpamAssessment? assessment,
        string? corpusRevision,
        SpamClassificationProfile profile,
        IReadOnlyList<SpamSignal> signals,
        DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(signals);

        if (!Enum.IsDefined(verdict))
        {
            throw new ArgumentOutOfRangeException(
                nameof(verdict),
                verdict,
                "A classification concludes one of the verdicts this system reaches.");
        }

        if (!Enum.IsDefined(decidedBy))
        {
            throw new ArgumentOutOfRangeException(
                nameof(decidedBy),
                decidedBy,
                "A classification is reached by one of the stages this system runs.");
        }

        return new SpamClassification(
            emailId,
            verdict,
            decidedBy,
            assessment,
            CheckedCorpusRevision(corpusRevision),
            profile,
            [.. signals.Take(MaximumSignals)],
            evaluatedAt.ToUniversalTime());
    }

    private static string? CheckedCorpusRevision(string? corpusRevision)
    {
        if (corpusRevision is null)
        {
            return null;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(corpusRevision);

        var trimmed = corpusRevision.Trim();

        if (trimmed.Length > MaximumCorpusRevisionLength)
        {
            throw new ArgumentException(
                $"A corpus revision carries at most {MaximumCorpusRevisionLength} characters.",
                nameof(corpusRevision));
        }

        if (trimmed.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A corpus revision cannot contain control characters.",
                nameof(corpusRevision));
        }

        return trimmed;
    }
}
