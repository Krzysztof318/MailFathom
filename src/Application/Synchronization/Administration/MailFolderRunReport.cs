// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Synchronization.Administration;

/// <summary>What one folder's most recent turn through a synchronization run did.</summary>
/// <remarks>
/// <para>
/// The counts describe a run that reached its folder and are zero for every other outcome, because a folder that was
/// never opened stored nothing rather than storing none. The two factory methods are what keeps that true: a caller
/// cannot report a failure and a count together.
/// </para>
/// <para>
/// Nothing here is mail or derived from it. An outcome, four counts, and the moment the folder finished are the whole of
/// it, which is what makes it safe to serve from the administrative endpoint.
/// </para>
/// </remarks>
/// <param name="Outcome">How the folder's turn ended.</param>
/// <param name="EndedAt">When it ended.</param>
/// <param name="StoredEmailCount">How many occurrences the run stored with their content.</param>
/// <param name="SkippedOversizedEmailCount">How many it stored as metadata only, because their content exceeded a configured limit.</param>
/// <param name="UnreadableMimeEmailCount">How many stored occurrences carried MIME that enrichment could not read.</param>
/// <param name="HasMoreEmails">Whether the folder still held unprocessed mail when the run's batch budget ran out, which is what the next run resumes.</param>
public sealed record MailFolderRunReport(
    MailFolderRunOutcome Outcome,
    DateTimeOffset EndedAt,
    int StoredEmailCount,
    int SkippedOversizedEmailCount,
    int UnreadableMimeEmailCount,
    bool HasMoreEmails)
{
    /// <summary>Reports a folder the run reached.</summary>
    /// <param name="endedAt">When the folder finished.</param>
    /// <param name="storedEmailCount">How many occurrences were stored with their content.</param>
    /// <param name="skippedOversizedEmailCount">How many were stored as metadata only.</param>
    /// <param name="unreadableMimeEmailCount">How many stored occurrences carried unreadable MIME.</param>
    /// <param name="hasMoreEmails">Whether unprocessed mail remains.</param>
    /// <returns>The report.</returns>
    public static MailFolderRunReport Synchronized(
        DateTimeOffset endedAt,
        int storedEmailCount,
        int skippedOversizedEmailCount,
        int unreadableMimeEmailCount,
        bool hasMoreEmails) =>
        new(
            MailFolderRunOutcome.Synchronized,
            endedAt,
            storedEmailCount,
            skippedOversizedEmailCount,
            unreadableMimeEmailCount,
            hasMoreEmails);

    /// <summary>Reports a folder the run did not synchronize, whatever kept it from doing so.</summary>
    /// <param name="outcome">Which of the unsynchronized outcomes ended the folder's turn.</param>
    /// <param name="endedAt">When it ended.</param>
    /// <returns>The report, carrying no counts.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="outcome" /> reports a folder that was synchronized, which is described by the other factory.</exception>
    public static MailFolderRunReport Unsynchronized(MailFolderRunOutcome outcome, DateTimeOffset endedAt)
    {
        if (outcome == MailFolderRunOutcome.Synchronized)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "A folder the run reached is reported through Synchronized, which carries what it stored.");
        }

        return new MailFolderRunReport(
            outcome,
            endedAt,
            StoredEmailCount: 0,
            SkippedOversizedEmailCount: 0,
            UnreadableMimeEmailCount: 0,
            HasMoreEmails: false);
    }
}
