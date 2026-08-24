// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.CodeCoverage;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Asks each of the four content tables whether it holds a row naming the object backend.</summary>
/// <remarks>
/// Four reads rather than one union, and short-circuited, because the answer is a presence: the common case on a
/// deployment that never selected the object backend is four index-free existence probes that each stop at the first
/// row, and the case that matters stops at whichever table holds one.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class ObjectBackedContentInventory(MailFathomDbContext dbContext) : IObjectBackedContentInventory
{
    /// <inheritdoc />
    public async Task<bool> HoldsObjectBackedContentAsync(CancellationToken cancellationToken) =>
        await dbContext.EmailMessageContents
            .AnyAsync(content => content.Backend == ContentStorageBackend.ObjectStorage, cancellationToken)
        || await dbContext.OutgoingEmailContents
            .AnyAsync(content => content.Backend == ContentStorageBackend.ObjectStorage, cancellationToken)
        || await dbContext.MailDraftContents
            .AnyAsync(content => content.Backend == ContentStorageBackend.ObjectStorage, cancellationToken)
        || await dbContext.RecurringSendDrafts
            .AnyAsync(draft => draft.Backend == ContentStorageBackend.ObjectStorage, cancellationToken);
}
