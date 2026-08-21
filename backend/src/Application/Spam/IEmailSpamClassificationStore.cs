// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam;

/// <summary>Persists the classification recorded for a stored occurrence.</summary>
/// <remarks>
/// <para>
/// One classification per occurrence, which is what makes classifying the same occurrence twice produce one record
/// rather than a history: the record says what is believed about the message now, and a second evaluation replaces it.
/// The store is therefore an upsert keyed by the occurrence, and re-running a classification is safe to repeat.
/// </para>
/// <para>
/// The record is derived data hanging off the occurrence, so it is removed with the occurrence rather than by a pass of
/// its own — which is what keeps it inside whatever erasure and retention already reach the message it describes.
/// </para>
/// </remarks>
public interface IEmailSpamClassificationStore
{
    /// <summary>Finds the classification recorded for one occurrence.</summary>
    /// <param name="emailId">The occurrence to read.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The classification, or <see langword="null" /> when the occurrence has never been classified.</returns>
    Task<SpamClassification?> FindAsync(StoredEmailId emailId, CancellationToken cancellationToken);

    /// <summary>Stages the classification of one occurrence, replacing whatever was recorded for it.</summary>
    /// <param name="session">The explicit persistence session this write participates in.</param>
    /// <param name="classification">What to record.</param>
    /// <param name="cancellationToken">Cancels the lookup before anything is staged.</param>
    /// <returns>A task that completes once the write is staged in the caller's session.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="classification" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Staged rather than committed, so the record and the signals it rests on reach the database as one unit. Replacing
    /// an existing record replaces its signals with it: a classification is what is believed now, and keeping the
    /// signals of a superseded verdict beside the new ones would leave a record nobody could read.
    /// </remarks>
    Task SaveAsync(
        IPersistenceSession session,
        SpamClassification classification,
        CancellationToken cancellationToken);
}
