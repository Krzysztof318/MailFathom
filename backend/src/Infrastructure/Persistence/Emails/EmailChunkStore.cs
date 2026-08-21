// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>EF Core state for the removal that takes a message's passages away again.</summary>
/// <remarks>
/// The cut itself is <see cref="EmailChunkWriter" />'s, which every path that produces passages goes through. This
/// adapter performs the removal, which no other path does.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailChunkStore : IEmailChunkStore
{
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
