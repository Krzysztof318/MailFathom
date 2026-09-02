// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Move;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>EF Core state for the one move of stored content a deployment may have.</summary>
/// <remarks>
/// One row under one key, which the table's own check constraint holds it to. Nothing here composes a key or takes one
/// from a caller, so there is no path by which a second move could be written.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredContentMoveRunStore(MailFathomDbContext dbContext) : IStoredContentMoveRunStore
{
    /// <inheritdoc />
    public async Task<StoredContentMoveRun?> FindAsync(CancellationToken cancellationToken)
    {
        var recorded = await dbContext.ContentMoveRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(run => run.Name == ContentMoveRunEntity.DeploymentName, cancellationToken);

        return recorded is null ? null : Read(recorded);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="run" /> is <see langword="null" />.</exception>
    public async Task SaveAsync(
        IPersistenceSession session,
        StoredContentMoveRun run,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        // FindAsync resolves a row this session already staged from the change tracker, so a session that writes the
        // move twice updates one row rather than inserting a second under the same key.
        var recorded = await sessionContext.ContentMoveRuns.FindAsync(
            [ContentMoveRunEntity.DeploymentName],
            cancellationToken);

        if (recorded is null)
        {
            recorded = new ContentMoveRunEntity { Name = ContentMoveRunEntity.DeploymentName };

            sessionContext.ContentMoveRuns.Add(recorded);
        }

        Write(recorded, run);
    }

    private static StoredContentMoveRun Read(ContentMoveRunEntity recorded) => new()
    {
        RequestedAt = recorded.RequestedAt,
        State = recorded.State,
        Kind = recorded.Kind,
        ResumeAfter = recorded.ResumeAfter,
        CopiedPayloadCount = recorded.CopiedPayloadCount,
        FailedPayloadCount = recorded.FailedPayloadCount,
        MovedByteCount = recorded.MovedByteCount,
        EndedAt = recorded.EndedAt,
    };

    /// <summary>Writes the move onto its row, leaving the key alone because the deployment is what the row is keyed by.</summary>
    private static void Write(ContentMoveRunEntity recorded, StoredContentMoveRun run)
    {
        recorded.RequestedAt = run.RequestedAt;
        recorded.State = run.State;
        recorded.Kind = run.Kind;
        recorded.ResumeAfter = run.ResumeAfter;
        recorded.CopiedPayloadCount = run.CopiedPayloadCount;
        recorded.FailedPayloadCount = run.FailedPayloadCount;
        recorded.MovedByteCount = run.MovedByteCount;
        recorded.EndedAt = run.EndedAt;
    }
}
