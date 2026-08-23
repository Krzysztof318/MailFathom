// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Maintenance;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>EF Core state for the one re-derivation run a scope may have, from the request to the counts it ended with.</summary>
/// <remarks>
/// One row per scope, keyed the way the walk's own position row is, so a whole-account run and a run over one folder of
/// the same account are two rows rather than one that answers about both.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredMailRederivationRunStore(MailFathomDbContext dbContext) : IStoredMailRederivationRunStore
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    public async Task<StoredMailRederivationRun?> FindAsync(
        StoredMailScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var account = scope.Account.Value;
        var folder = KeyedFolderOf(scope);

        var recorded = await dbContext.MailRederivationRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(
                run => run.MailboxAccountId == account && run.FolderAlias == folder,
                cancellationToken);

        return recorded is null ? null : Read(recorded);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="run" /> is <see langword="null" />.</exception>
    public async Task SaveAsync(
        IPersistenceSession session,
        StoredMailRederivationRun run,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var account = run.Scope.Account.Value;
        var folder = KeyedFolderOf(run.Scope);

        // FindAsync resolves a row this session already staged from the change tracker, so a session that writes the
        // run twice updates one row rather than inserting a second under the same key.
        var recorded = await sessionContext.MailRederivationRuns.FindAsync([account, folder], cancellationToken);

        if (recorded is null)
        {
            recorded = new MailRederivationRunEntity
            {
                MailboxAccountId = account,
                FolderAlias = folder,
            };

            sessionContext.MailRederivationRuns.Add(recorded);
        }

        Write(recorded, run);
    }

    /// <summary>Reads the folder value the scope's row is keyed by, which a whole-account run has its own value for.</summary>
    private static string KeyedFolderOf(StoredMailScope scope) =>
        scope.Folder?.Value ?? MailRederivationPositionEntity.WholeAccountFolder;

    private static StoredMailRederivationRun Read(MailRederivationRunEntity recorded) => new()
    {
        RunId = StoredMailRederivationRunId.Create(recorded.RunId),
        Scope = new StoredMailScope(
            MailAccountId.Create(recorded.MailboxAccountId),
            recorded.FolderAlias is { Length: > 0 } alias ? MailFolderAlias.Create(alias) : null),
        RequestedAt = recorded.RequestedAt,
        SegmentCount = recorded.SegmentCount,
        RederivedEmailCount = recorded.RederivedEmailCount,
        UnreadableEmailCount = recorded.UnreadableEmailCount,
        MissingContentEmailCount = recorded.MissingContentEmailCount,
        EndedAt = recorded.EndedAt,
    };

    /// <summary>Writes the run onto its row, leaving the key alone because the scope is what the row is keyed by.</summary>
    private static void Write(MailRederivationRunEntity recorded, StoredMailRederivationRun run)
    {
        recorded.RunId = run.RunId.Value;
        recorded.RequestedAt = run.RequestedAt;
        recorded.SegmentCount = run.SegmentCount;
        recorded.RederivedEmailCount = run.RederivedEmailCount;
        recorded.UnreadableEmailCount = run.UnreadableEmailCount;
        recorded.MissingContentEmailCount = run.MissingContentEmailCount;
        recorded.EndedAt = run.EndedAt;
    }
}
