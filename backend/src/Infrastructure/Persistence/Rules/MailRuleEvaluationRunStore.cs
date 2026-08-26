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
        MailAccountIdentity account,
        CancellationToken cancellationToken)
    {
        var ownerId = account.Owner.Value;
        var mailboxAccountId = account.Id.Value;
        var outstanding = await dbContext.MailRuleEvaluationRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(
                run => run.OwnerId == ownerId && run.MailboxAccountId == mailboxAccountId && run.EndedAt == null,
                cancellationToken);

        return outstanding is null ? null : Read(outstanding, account);
    }

    /// <inheritdoc />
    /// <remarks>One row per account, so the account's key is the whole of the lookup and no ordering is needed.</remarks>
    public async Task<MailRuleEvaluationRun?> FindLatestAsync(
        MailAccountIdentity account,
        CancellationToken cancellationToken)
    {
        var ownerId = account.Owner.Value;
        var mailboxAccountId = account.Id.Value;
        var latest = await dbContext.MailRuleEvaluationRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(
                run => run.OwnerId == ownerId && run.MailboxAccountId == mailboxAccountId,
                cancellationToken);

        return latest is null ? null : Read(latest, account);
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

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var stored = await sessionContext.MailRuleEvaluationRuns.FindAsync(
            [run.Account.Id.Value],
            cancellationToken);

        if (stored is null)
        {
            stored = NewRunFor(run.Account);
            sessionContext.MailRuleEvaluationRuns.Add(stored);
        }

        Write(stored, run);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The read this decides from is the session's own, taken immediately before the write, so the window a request's
    /// earlier read left open closes here: a run another request or another schedule's occasion committed in between is
    /// found and stood down from rather than overwritten. What that read cannot see is a competing insert that has not
    /// committed yet, and it does not have to — both inserts meet the account's primary key, and the loser is retried
    /// from a fresh read that finds the winner's row.
    /// </remarks>
    public async Task<MailRuleEvaluationRun?> TryStartAsync(
        IPersistenceSession session,
        MailRuleEvaluationRun run,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var stored = await sessionContext.MailRuleEvaluationRuns.FindAsync(
            [run.Account.Id.Value],
            cancellationToken);

        if (stored is null)
        {
            stored = NewRunFor(run.Account);
            sessionContext.MailRuleEvaluationRuns.Add(stored);
        }
        else
        {
            var claimed = Read(stored, run.Account);

            if (!run.Supersedes(claimed))
            {
                return claimed;
            }
        }

        Write(stored, run);

        return null;
    }

    /// <summary>Composes the row a run that has never been recorded starts from, with the owner it belongs to.</summary>
    /// <remarks>
    /// The owner comes off the identity the pass was started for rather than from a read of the account, which is what
    /// makes the pair on the row the same pair the caller resolved.
    /// </remarks>
    private static MailRuleEvaluationRunEntity NewRunFor(MailAccountIdentity account) => new()
    {
        MailboxAccountId = account.Id.Value,
        OwnerId = account.Owner.Value,
    };

    private static MailRuleEvaluationRun Read(MailRuleEvaluationRunEntity entity, MailAccountIdentity account) => new()
    {
        Account = account,
        RequestedAt = entity.RequestedAt,
        Trigger = entity.Trigger,
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
        entity.Trigger = run.Trigger;
        entity.Revision = run.Revision.IsSpecified ? run.Revision.Value : null;
        entity.Position = run.Position?.Value;
        entity.EvaluatedEmailCount = run.EvaluatedEmailCount;
        entity.MatchedEmailCount = run.MatchedEmailCount;
        entity.SkippedEmailCount = run.SkippedEmailCount;
        entity.EndedAt = run.EndedAt;
        entity.Ending = run.Ending;
    }
}
