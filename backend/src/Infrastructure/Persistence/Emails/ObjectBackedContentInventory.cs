// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.CodeCoverage;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Asks each of the four content tables whether it holds a row naming the object backend.</summary>
/// <remarks>
/// <para>
/// Four reads rather than one union, and short-circuited, because the answer is a presence: a deployment that holds
/// object-backed mail stops at whichever table holds the first of it, and one that holds none pays for all four.
/// </para>
/// <para>
/// <b>Each read resolves through a partial index rather than by scanning a table</b>, which is what makes this
/// affordable on a readiness probe. `ix_&lt;table&gt;_object_backed` is filtered to this backend, so on the deployment
/// that runs this most — the one that configured no endpoint, where the answer is always no — every one of those
/// indexes is empty and the four reads together cost nothing proportional to the mail stored. Without the filter the
/// negative answer would be four sequential scans on every scrape, which is the shape this looked like before the
/// index existed.
/// </para>
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
