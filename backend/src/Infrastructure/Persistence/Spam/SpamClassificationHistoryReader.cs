// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.History;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Spam;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Spam;

/// <summary>Reads back an account's classifications and the changes they asked the mailbox for, in PostgreSQL.</summary>
/// <remarks>
/// <para>
/// Two bounded reads rather than one join. The page of classifications is the query the order and the cursor belong to;
/// the changes each verdict asked for are read afterwards for exactly the occurrences that page holds, because a join
/// would repeat every classification once per mutation and make the page size mean something different per row.
/// </para>
/// <para>
/// Both are projections rather than entity loads, and the first one is the reason: a signal's observation is text a mail
/// server wrote and never leaves the database here, so the column is not read at all rather than read and then dropped.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class SpamClassificationHistoryReader(MailFathomDbContext dbContext) : ISpamClassificationHistoryReader
{
    /// <inheritdoc />
    /// <remarks>The read takes one row past the page, for the reason <see cref="KeysetPageSplit" /> states.</remarks>
    public async Task<SpamClassificationHistoryPage> ReadPageAsync(
        SpamClassificationHistoryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var ownerValue = query.Account.Owner.Value;
        var accountValue = query.Account.Id.Value;

        var rows = await this.Filter(query)
            .Where(classification => classification.StoredEmail!.OwnerId == ownerValue
                && classification.StoredEmail.MailboxAccountId == accountValue)
            .OrderByDescending(classification => classification.EvaluatedAt)
            .ThenByDescending(classification => classification.StoredEmailId)
            .Take(query.PageSize + 1)
            .Select(classification => new ClassificationRow(
                classification.StoredEmailId,
                classification.StoredEmail!.MailFolder.Alias,
                classification.Verdict,
                classification.DecidedBy,
                classification.Score,
                classification.Threshold,
                classification.CorpusRevision,
                classification.Profile,
                classification.EvaluatedAt,
                classification.Signals
                    .OrderBy(signal => signal.Ordinal)
                    .Select(signal => signal.Name)
                    .ToList()))
            .ToArrayAsync(cancellationToken);

        var (pageRows, hasMore) = KeysetPageSplit.Of(rows, query.PageSize);
        var requestedMutations = await this.ReadRequestedMutationsAsync(pageRows, cancellationToken);

        var entries = pageRows
            .Select(row => new SpamClassificationHistoryEntry(
                StoredEmailId.Create(row.StoredEmailId),
                MailFolderAlias.Create(row.FolderAlias),
                row.Verdict,
                row.DecidedBy,
                row.Score is { } score && row.Threshold is { } threshold
                    ? SpamAssessment.Create(score, threshold)
                    : null,
                row.CorpusRevision,
                row.Profile is { } profile ? SpamClassificationProfile.Restore(profile) : default,
                row.SignalNames,
                row.EvaluatedAt,
                requestedMutations.TryGetValue(row.StoredEmailId, out var mutations) ? mutations : []))
            .ToArray();

        return new SpamClassificationHistoryPage(
            entries,
            hasMore
                ? SpamClassificationHistoryCursor.After(
                    pageRows[^1].EvaluatedAt,
                    StoredEmailId.Create(pageRows[^1].StoredEmailId),
                    query.FilterFingerprint)
                : null);
    }

    /// <summary>Reads the changes a classification asked for, for exactly the occurrences one page holds.</summary>
    /// <remarks>
    /// Narrowed by the requester's origin, which is what keeps a filing somebody authored by hand and a filing a verdict
    /// asked for two different answers. A name the permitted set no longer holds is left out rather than reconstructed,
    /// so a row from a build that permitted something this one does not costs its own line and nothing else.
    /// </remarks>
    private async Task<Dictionary<Guid, IReadOnlyList<SpamClassificationRequestedMutation>>> ReadRequestedMutationsAsync(
        IReadOnlyList<ClassificationRow> pageRows,
        CancellationToken cancellationToken)
    {
        if (pageRows.Count == 0)
        {
            return [];
        }

        Guid[] emailIds = [.. pageRows.Select(static row => row.StoredEmailId)];

        var rows = await dbContext.MailboxMutations
            .AsNoTracking()
            .Where(mutation => emailIds.Contains(mutation.StoredEmailId)
                && mutation.RequesterOrigin == MailboxMutationOrigin.Classification)
            .OrderBy(mutation => mutation.RecordedAt)
            .ThenBy(mutation => mutation.Id)
            .Select(mutation => new MutationRow(mutation.Id, mutation.StoredEmailId, mutation.Mutation))
            .ToArrayAsync(cancellationToken);

        return rows
            .Select(row => (row.StoredEmailId, Requested: Read(row)))
            .Where(static pair => pair.Requested is not null)
            .GroupBy(static pair => pair.StoredEmailId)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<SpamClassificationRequestedMutation>)
                    [.. group.Select(static pair => pair.Requested!)]);
    }

    private static SpamClassificationRequestedMutation? Read(MutationRow row) =>
        MailboxMutation.TryParseName(row.Mutation, out var mutation)
            ? new SpamClassificationRequestedMutation(MailboxMutationRecordId.Create(row.Id), mutation)
            : null;

    /// <summary>Applies the filters a query names, leaving the account and the ordering to the caller.</summary>
    private IQueryable<EmailSpamClassificationEntity> Filter(SpamClassificationHistoryQuery query)
    {
        var classifications = dbContext.EmailSpamClassifications.AsNoTracking();

        if (query.StoredEmailId is { } storedEmailId)
        {
            var emailValue = storedEmailId.Value;

            classifications = classifications.Where(classification => classification.StoredEmailId == emailValue);
        }

        if (query.Verdict is { } verdict)
        {
            classifications = classifications.Where(classification => classification.Verdict == verdict);
        }

        if (query.EvaluatedFrom is { } evaluatedFrom)
        {
            classifications = classifications.Where(classification => classification.EvaluatedAt >= evaluatedFrom);
        }

        if (query.EvaluatedBefore is { } evaluatedBefore)
        {
            classifications = classifications.Where(classification => classification.EvaluatedAt < evaluatedBefore);
        }

        // The keyset boundary is the pair the order is taken on, so a record evaluated in the same instant as the last
        // one of the previous page is served exactly once rather than skipped or repeated. The identifier comparison is
        // evaluated by PostgreSQL as a `uuid` comparison, which is what the index is ordered by, so it never has to
        // agree with how the CLR happens to compare two `Guid` values.
        if (query.Cursor is { } cursor)
        {
            var boundaryEvaluatedAt = cursor.EvaluatedAt;
            var boundaryId = cursor.EmailId.Value;

            classifications = classifications.Where(classification =>
                classification.EvaluatedAt < boundaryEvaluatedAt
                || (classification.EvaluatedAt == boundaryEvaluatedAt && classification.StoredEmailId < boundaryId));
        }

        return classifications;
    }

    /// <summary>One classification as the database returns it, before the domain values are rebuilt from it.</summary>
    private sealed record ClassificationRow(
        Guid StoredEmailId,
        string FolderAlias,
        SpamVerdict Verdict,
        SpamClassificationStage DecidedBy,
        double? Score,
        double? Threshold,
        string? CorpusRevision,
        string? Profile,
        DateTimeOffset EvaluatedAt,
        IReadOnlyList<string> SignalNames);

    /// <summary>One requested change as the database returns it.</summary>
    private sealed record MutationRow(Guid Id, Guid StoredEmailId, string Mutation);
}
