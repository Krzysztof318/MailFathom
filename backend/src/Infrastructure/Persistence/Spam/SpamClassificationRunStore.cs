// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Runs;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Spam;

/// <summary>EF Core state for the one whole-mailbox classification run an account may have outstanding.</summary>
[RequiresIntegrationCoverage]
internal sealed class SpamClassificationRunStore(MailFathomDbContext dbContext) : ISpamClassificationRunStore
{
    /// <inheritdoc />
    public async Task<SpamClassificationRun?> FindOutstandingAsync(
        MailAccountIdentity account,
        CancellationToken cancellationToken)
    {
        var owner = account.Owner.Value;
        var mailboxAccountId = account.Id.Value;
        var outstanding = await dbContext.SpamClassificationRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(
                run => run.OwnerId == owner
                    && run.MailboxAccountId == mailboxAccountId
                    && run.EndedAt == null,
                cancellationToken);

        return outstanding is null ? null : Read(outstanding, account);
    }

    /// <inheritdoc />
    /// <remarks>One row per account, so the account's key is the whole of the lookup and no ordering is needed.</remarks>
    public async Task<SpamClassificationRun?> FindLatestAsync(
        MailAccountIdentity account,
        CancellationToken cancellationToken)
    {
        var owner = account.Owner.Value;
        var mailboxAccountId = account.Id.Value;
        var latest = await dbContext.SpamClassificationRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(
                run => run.OwnerId == owner && run.MailboxAccountId == mailboxAccountId,
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
        SpamClassificationRun run,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var stored = await sessionContext.SpamClassificationRuns.FindAsync(
            [run.AccountId.Value],
            cancellationToken);

        if (stored is null)
        {
            stored = new SpamClassificationRunEntity
            {
                MailboxAccountId = run.Account.Id.Value,

                // Written from the identity the request resolved through the deployment's catalog, which is the account
                // this run walks. A run belongs to the owner whose mail it classifies.
                OwnerId = run.Account.Owner.Value,
                FolderAliases = [],
            };

            sessionContext.SpamClassificationRuns.Add(stored);
        }

        Write(stored, run);
    }

    private static SpamClassificationRun Read(
        SpamClassificationRunEntity entity,
        MailAccountIdentity account) => new()
        {
            Account = account,
            RequestedAt = entity.RequestedAt,
            Terms = SpamClassificationRunTerms.Create(
                entity.FolderAliases.Select(MailFolderAlias.Create),
                entity.Posture,
                entity.Rescores),
            Profile = entity.Profile is { } profile ? SpamClassificationProfile.Restore(profile) : default,
            Position = entity.Position is { } position ? StoredEmailId.Create(position) : null,
            ClassifiedEmailCount = entity.ClassifiedEmailCount,
            SpamEmailCount = entity.SpamEmailCount,
            UndeterminedEmailCount = entity.UndeterminedEmailCount,
            SkippedEmailCount = entity.SkippedEmailCount,
            UnclassifiableEmailCount = entity.UnclassifiableEmailCount,
            ActedEmailCount = entity.ActedEmailCount,
            EndedAt = entity.EndedAt,
            Ending = entity.Ending,
        };

    private static void Write(SpamClassificationRunEntity entity, SpamClassificationRun run)
    {
        entity.RequestedAt = run.RequestedAt;
        entity.FolderAliases = [.. run.Terms.FolderAliases.Select(static alias => alias.Value)];
        entity.Posture = run.Terms.Posture;
        entity.Rescores = run.Terms.Rescores;
        entity.Profile = run.Profile.IsSpecified ? run.Profile.Value : null;
        entity.Position = run.Position?.Value;
        entity.ClassifiedEmailCount = run.ClassifiedEmailCount;
        entity.SpamEmailCount = run.SpamEmailCount;
        entity.UndeterminedEmailCount = run.UndeterminedEmailCount;
        entity.SkippedEmailCount = run.SkippedEmailCount;
        entity.UnclassifiableEmailCount = run.UnclassifiableEmailCount;
        entity.ActedEmailCount = run.ActedEmailCount;
        entity.EndedAt = run.EndedAt;
        entity.Ending = run.Ending;
    }
}
