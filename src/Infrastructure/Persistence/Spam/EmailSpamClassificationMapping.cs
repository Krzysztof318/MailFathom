// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Spam;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Spam;

/// <summary>Maps between the classification record and the two rows it is stored as.</summary>
/// <remarks>
/// Written once and used by the read and the write, so a column can never come to mean one thing when it is written and
/// another when it is read back. The domain value validates on the way in from storage exactly as it does on the way in
/// from a classification run, which is what stops a row somebody edited by hand from becoming a record no reader could
/// interpret.
/// </remarks>
internal static class EmailSpamClassificationMapping
{
    /// <summary>Reads the record one classification row and its signals hold.</summary>
    /// <param name="entity">The stored classification, with its signals loaded in ordinal order.</param>
    /// <returns>The classification.</returns>
    internal static SpamClassification Read(EmailSpamClassificationEntity entity) => SpamClassification.Create(
        StoredEmailId.Create(entity.StoredEmailId),
        entity.Verdict,
        entity.DecidedBy,
        entity.Score is { } score && entity.Threshold is { } threshold
            ? SpamAssessment.Create(score, threshold)
            : null,
        entity.CorpusRevision,
        [
            .. entity.Signals
                .OrderBy(static signal => signal.Ordinal)
                .Select(static signal => SpamSignal.Create(
                    signal.Kind,
                    signal.Name,
                    signal.Observation,
                    SpamSignalProvenance.Restore(signal.Source, signal.Origin))),
        ],
        entity.EvaluatedAt);

    /// <summary>Writes a record onto the row that holds it, leaving the signals to the caller that stages them.</summary>
    /// <param name="entity">The row to write.</param>
    /// <param name="classification">What to record.</param>
    internal static void Write(EmailSpamClassificationEntity entity, SpamClassification classification)
    {
        entity.Verdict = classification.Verdict;
        entity.DecidedBy = classification.DecidedBy;
        entity.Score = classification.Assessment?.Score;
        entity.Threshold = classification.Assessment?.Threshold;
        entity.CorpusRevision = classification.CorpusRevision;
        entity.EvaluatedAt = classification.EvaluatedAt;
    }

    /// <summary>Builds the rows one classification's signals are stored as.</summary>
    /// <param name="classification">The record whose signals to stage.</param>
    /// <returns>One row per signal, numbered in the order the stages produced them.</returns>
    internal static IEnumerable<EmailSpamClassificationSignalEntity> SignalRows(SpamClassification classification) =>
        classification.Signals.Select((signal, ordinal) => new EmailSpamClassificationSignalEntity
        {
            StoredEmailId = classification.EmailId.Value,
            Ordinal = ordinal,
            Kind = signal.Kind,
            Name = signal.Name,
            Observation = signal.Observation,
            Source = signal.Provenance.Source,
            Origin = signal.Provenance.Origin,
        });
}
