// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Rules.History;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Mutations;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Rules;

/// <summary>Keeps the record of what each rule concluded about each email, in PostgreSQL.</summary>
/// <remarks>
/// <para>
/// The append uses the context enlisted in the caller's session, so a batch's evaluations, the requests they produced,
/// and the record of why commit together. The page read uses the scoped context, because it joins no transaction. The
/// erasure uses neither: it is a set-based delete that composes with nothing a caller is holding, and the actions an
/// erased execution recorded go with it through the execution's own foreign key.
/// </para>
/// <para>
/// Nothing here updates a row. An execution states a reading that already happened, so the record only ever grows and
/// shrinks — which is also why no row carries a concurrency token.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailRuleExecutionStore(
    MailFathomDbContext readContext,
    MailRuleHistoryTelemetry telemetry)
    : IMailRuleExecutionStore
{
    /// <inheritdoc />
    /// <remarks>
    /// Nothing is looked up before the insert. An execution carries an identity generated where it was composed, and the
    /// pass composes one per rule per email per commit attempt, so there is no earlier row for this append to find: a
    /// commit that lost an optimistic race rolled its executions back with everything else it staged.
    /// </remarks>
    public Task AppendAsync(
        IPersistenceSession session,
        IReadOnlyList<MailRuleExecution> executions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(executions);

        if (executions.Count == 0)
        {
            return Task.CompletedTask;
        }

        var writeContext = EfCorePersistenceSessionAccessor.DbContextOf(session);

        writeContext.MailRuleExecutions.AddRange(executions.Select(MailRuleExecutionMapping.ToEntity));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>The read takes one row past the page, for the reason <see cref="KeysetPageSplit" /> states.</remarks>
    public async Task<MailRuleExecutionPage> ReadPageAsync(
        MailRuleExecutionQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var accountValue = query.AccountId.Value;

        var entities = await this.Filter(query)
            .Where(execution => execution.MailboxAccountId == accountValue)

            // The actions are the point of an execution that matched, so they are loaded with it rather than left to a
            // second read per row. A rule declares a bounded set of changes and the page is bounded, so the join is too.
            .Include(execution => execution.Actions)
            .OrderByDescending(execution => execution.EvaluatedAt)
            .ThenByDescending(execution => execution.Id)
            .Take(query.PageSize + 1)
            .ToArrayAsync(cancellationToken);

        var (pageEntities, hasMore) = KeysetPageSplit.Of(entities, query.PageSize);
        var executions = new List<MailRuleExecution>(pageEntities.Length);
        var unreadableCount = 0;

        foreach (var entity in pageEntities)
        {
            if (MailRuleExecutionMapping.TryToExecution(entity, out var execution))
            {
                executions.Add(execution);
            }
            else
            {
                unreadableCount++;
            }
        }

        if (unreadableCount > 0)
        {
            telemetry.RecordUnreadableExecutions(query.AccountId, unreadableCount);
        }

        // The boundary is the last row read rather than the last execution presented, so a row this build cannot
        // interpret costs its own place in the page and nothing else: the walk neither stalls on it nor repeats the rows
        // either side of it.
        return new MailRuleExecutionPage(
            executions,
            hasMore
                ? MailRuleExecutionCursor.After(
                    pageEntities[^1].EvaluatedAt,
                    MailRuleExecutionId.Create(pageEntities[^1].Id),
                    query.FilterFingerprint)
                : null);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The bounded set is read first and deleted by key, for the reason
    /// <see cref="MailboxMutationAuditEntryStore.EraseCompletedBeforeAsync" /> states.
    /// </remarks>
    public async Task<int> EraseEvaluatedBeforeAsync(
        MailAccountId accountId,
        DateTimeOffset evaluatedBefore,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var accountValue = accountId.Value;

        var expiringIds = await readContext.MailRuleExecutions
            .AsNoTracking()
            .Where(execution => execution.MailboxAccountId == accountValue && execution.EvaluatedAt < evaluatedBefore)
            .OrderBy(execution => execution.EvaluatedAt)
            .ThenBy(execution => execution.Id)
            .Take(limit)
            .Select(execution => execution.Id)
            .ToArrayAsync(cancellationToken);

        if (expiringIds.Length == 0)
        {
            return 0;
        }

        // The actions go with the executions rather than being deleted beside them, because the foreign key cascades. A
        // second statement here would be a second place the same rule was written.
        return await readContext.MailRuleExecutions
            .Where(execution => expiringIds.Contains(execution.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Applies the filters a query names, leaving the account and the ordering to the caller.</summary>
    private IQueryable<MailRuleExecutionEntity> Filter(MailRuleExecutionQuery query)
    {
        var executions = readContext.MailRuleExecutions.AsNoTracking();

        if (query.RuleName is { } ruleName)
        {
            executions = executions.Where(execution => execution.RuleName == ruleName);
        }

        if (query.StoredEmailId is { } storedEmailId)
        {
            var emailValue = storedEmailId.Value;

            executions = executions.Where(execution => execution.StoredEmailId == emailValue);
        }

        if (query.EvaluatedFrom is { } evaluatedFrom)
        {
            executions = executions.Where(execution => execution.EvaluatedAt >= evaluatedFrom);
        }

        if (query.EvaluatedBefore is { } evaluatedBefore)
        {
            executions = executions.Where(execution => execution.EvaluatedAt < evaluatedBefore);
        }

        // The keyset boundary is the pair the order is taken on, so an execution recorded in the same instant as the
        // last one of the previous page is served exactly once rather than skipped or repeated. The identifier
        // comparison is evaluated by PostgreSQL as a `uuid` comparison, which is what the index is ordered by, so it
        // never has to agree with how the CLR happens to compare two `Guid` values.
        if (query.Cursor is { } cursor)
        {
            var boundaryEvaluatedAt = cursor.EvaluatedAt;
            var boundaryId = cursor.ExecutionId.Value;

            executions = executions.Where(execution => execution.EvaluatedAt < boundaryEvaluatedAt
                || (execution.EvaluatedAt == boundaryEvaluatedAt && execution.Id < boundaryId));
        }

        return executions;
    }
}
