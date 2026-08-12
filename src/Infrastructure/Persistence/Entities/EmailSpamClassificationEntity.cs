// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Spam;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>What classification concluded about one stored occurrence.</summary>
/// <remarks>
/// <para>
/// Keyed by the occurrence, which is what makes one classification per message a property of the schema rather than a
/// check somebody has to remember: classifying the same message twice reaches the same row, so two runs asking together
/// resolve to one record rather than to a history nobody asked for.
/// </para>
/// <para>
/// A table of its own rather than columns on the email, for the reason the repair request is: the rows are sparse — a
/// deployment with classification off has none at all — and what hangs off them is a second table that would otherwise
/// have to reference an email row it does not describe.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailSpamClassificationEntity
{
    /// <summary>The greatest length a stored corpus revision has, which the domain value already refuses to exceed.</summary>
    internal const int MaximumCorpusRevisionLength = SpamClassification.MaximumCorpusRevisionLength;

    /// <summary>The length a stored classification profile has, which is the derived identity's own fixed width.</summary>
    internal const int ProfileLength = SpamClassificationProfile.LengthInCharacters;

    public Guid StoredEmailId { get; set; }

    /// <summary>Gets or sets the email this classification is about, which a write leaves unset.</summary>
    /// <remarks>
    /// Optional rather than required, unlike the navigation on every other table hanging off an email. A classification
    /// is staged by the identifier its caller was given, so requiring the navigation would mean loading a whole email row
    /// to write a record that names it — and the foreign key already refuses a classification of a message that is not
    /// there.
    /// </remarks>
    public StoredEmailEntity? StoredEmail { get; set; }

    public SpamVerdict Verdict { get; set; }

    public SpamClassificationStage DecidedBy { get; set; }

    /// <summary>Gets or sets the score reached, absent when no stage produced a number.</summary>
    /// <remarks>
    /// The score and the threshold are two columns that are present or absent together, because a score without the
    /// threshold it was judged against cannot be read: the same number is spam under one configuration and ordinary
    /// mail under another. Nothing enforces the pairing in the schema; the domain value that builds them refuses to
    /// carry one without the other.
    /// </remarks>
    public double? Score { get; set; }

    /// <summary>Gets or sets the threshold the score was judged against, absent exactly when <see cref="Score" /> is.</summary>
    public double? Threshold { get; set; }

    /// <summary>Gets or sets the rule corpus the deciding stage ran under, absent when it has none.</summary>
    public string? CorpusRevision { get; set; }

    /// <summary>Gets or sets the settings the verdict was reached under, absent on a record written before it named one.</summary>
    /// <remarks>
    /// Nullable rather than defaulted, because the two states mean different things to a run over a whole mailbox: a row
    /// naming the profile in force is mail to leave alone, and a row naming none was decided under terms nothing can
    /// compare and is mail to score again. Filling the column in for existing rows would claim the second was the first.
    /// </remarks>
    public string? Profile { get; set; }

    public DateTimeOffset EvaluatedAt { get; set; }

    /// <summary>Gets or sets the optimistic concurrency token, which is PostgreSQL's own <c>xmin</c> rather than a column.</summary>
    /// <remarks>
    /// Two runs can reach one occurrence: an arrival classifies it while an operator's reclassification replaces it. The
    /// token is what turns that into a conflict the retry policy resolves from a fresh read instead of one writer
    /// overwriting the other's verdict.
    /// </remarks>
    public uint ConcurrencyVersion { get; set; }

    /// <summary>Gets the facts the verdict rests on, in the order the stages produced them.</summary>
    public ICollection<EmailSpamClassificationSignalEntity> Signals { get; } = [];
}
