// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>EF Core state for the passages a message is cut into and the removal that takes them away again.</summary>
/// <remarks>
/// The cut itself is <see cref="EmailChunkWriter" />'s, which every path that produces passages goes through, so this
/// adapter is the port's shape over it rather than a second implementation of the rules. What it adds is the removal,
/// which no other path performs.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailChunkStore(EmailChunkWriter chunkWriter) : IEmailChunkStore
{
    /// <inheritdoc />
    public async Task DeriveChunksAsync(
        IPersistenceSession session,
        StoredEmailId emailId,
        ExtractedEmailText text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        var dbContext = EfCorePersistenceSessionAccessor.DbContextOf(session);

        // Found rather than queried, because the arrival path cuts a message the same session inserted moments ago and
        // has not committed. FindAsync resolves a staged row from the change tracker, which a set-based read could not
        // see at all.
        var storedEmail = await dbContext.StoredEmails.FindAsync([emailId.Value], cancellationToken)
            ?? throw new InvalidOperationException(
                $"No stored email carries the identifier {emailId}, so no passages can be cut for it.");

        await chunkWriter.SaveAsync(dbContext, storedEmail, text, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> DiscardChunksAsync(
        IPersistenceSession session,
        StoredEmailId emailId,
        CancellationToken cancellationToken)
    {
        var dbContext = EfCorePersistenceSessionAccessor.DbContextOf(session);

        // Deleted as one statement rather than through tracked entities: the rows carry nothing this decision reads,
        // and a message that a provider has embedded across many passages would otherwise be loaded whole to be thrown
        // away. The vectors go with them, because the foreign key from a vector to its passage cascades.
        return await dbContext.EmailChunks
            .Where(chunk => chunk.StoredEmailId == emailId.Value)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
