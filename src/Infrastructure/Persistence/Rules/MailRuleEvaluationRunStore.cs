// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Rules;

/// <summary>EF Core state for the one whole-mailbox rule run an account may have outstanding.</summary>
[RequiresIntegrationCoverage]
internal sealed class MailRuleEvaluationRunStore(MailFathomDbContext dbContext) : IMailRuleEvaluationRunStore
{
    /// <inheritdoc />
    public async Task<MailRuleEvaluationRun?> FindOutstandingAsync(
        MailAccountId accountId,
        CancellationToken cancellationToken)
    {
        var mailboxAccountId = accountId.Value;
        var outstanding = await dbContext.MailRuleEvaluationRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(
                run => run.MailboxAccountId == mailboxAccountId && run.EndedAt == null,
                cancellationToken);

        return outstanding is null ? null : Read(outstanding, accountId);
    }

    /// <inheritdoc />
    /// <remarks>One row per account, so the account's key is the whole of the lookup and no ordering is needed.</remarks>
    public async Task<MailRuleEvaluationRun?> FindLatestAsync(
        MailAccountId accountId,
        CancellationToken cancellationToken)
    {
        var mailboxAccountId = accountId.Value;
        var latest = await dbContext.MailRuleEvaluationRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(run => run.MailboxAccountId == mailboxAccountId, cancellationToken);

        return latest is null ? null : Read(latest, accountId);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One row per account, so a request that follows a completed run overwrites it rather than appending. The lookup
    /// resolves a row this session already staged from the change tracker, which is what lets a pass commit several
    /// batches through one session without inserting a second row under the same key.
    /// </remarks>
    public async Task SaveAsync(
        IPersistenceSession session,
        MailRuleEvaluationRun run,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        var sessionContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var stored = await sessionContext.MailRuleEvaluationRuns.FindAsync(
            [run.AccountId.Value],
            cancellationToken);

        if (stored is null)
        {
            stored = new MailRuleEvaluationRunEntity { MailboxAccountId = run.AccountId.Value };
            sessionContext.MailRuleEvaluationRuns.Add(stored);
        }

        Write(stored, run);
    }

    private static MailRuleEvaluationRun Read(MailRuleEvaluationRunEntity entity, MailAccountId accountId) => new()
    {
        AccountId = accountId,
        RequestedAt = entity.RequestedAt,
        Revision = entity.Revision is { } revision
            ? MailRuleSetRevision.Restore(revision)
            : default,
        Position = entity.Position is { } position ? StoredEmailId.Create(position) : null,
        EvaluatedEmailCount = entity.EvaluatedEmailCount,
        MatchedEmailCount = entity.MatchedEmailCount,
        SkippedEmailCount = entity.SkippedEmailCount,
        EndedAt = entity.EndedAt,
        Ending = entity.Ending,
    };

    private static void Write(MailRuleEvaluationRunEntity entity, MailRuleEvaluationRun run)
    {
        entity.RequestedAt = run.RequestedAt;
        entity.Revision = run.Revision.IsSpecified ? run.Revision.Value : null;
        entity.Position = run.Position?.Value;
        entity.EvaluatedEmailCount = run.EvaluatedEmailCount;
        entity.MatchedEmailCount = run.MatchedEmailCount;
        entity.SkippedEmailCount = run.SkippedEmailCount;
        entity.EndedAt = run.EndedAt;
        entity.Ending = run.Ending;
    }
}
