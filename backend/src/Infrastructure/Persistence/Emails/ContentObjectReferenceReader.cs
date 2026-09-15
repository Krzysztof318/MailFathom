// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.ObjectStorage;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Reads which of a page of object keys a stored payload or a finished export still points at.</summary>
/// <remarks>
/// <para>
/// A query per table rather than one union, because the tables are unrelated and PostgreSQL plans each of them against
/// its own index over the locator column. Each of the five payload reads is filtered to the object backend as well as
/// to the keys, which is what keeps a deployment that has only ever written to the database answering from an empty
/// index. The export archive has no such column: an archive is written to the object store or the export is refused
/// before it starts, so what stands in for the filter there is the locator being present at all.
/// </para>
/// <para>
/// The read joins no session and no transaction. It is a decision about objects rather than a write, and holding a
/// transaction open across a sweep would keep one for as long as the bucket takes.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class ContentObjectReferenceReader(MailFathomDbContext dbContext) : IContentObjectReferenceReader
{
    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> FindReferencedAsync(
        IReadOnlyCollection<string> objectLocators,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(objectLocators);

        if (objectLocators.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var referenced = new HashSet<string>(StringComparer.Ordinal);

        await CollectAsync(
            referenced,
            dbContext.EmailMessageContents
                .AsNoTracking()
                .Where(content => content.Backend == ContentStorageBackend.ObjectStorage
                    && objectLocators.Contains(content.ObjectLocator!))
                .Select(content => content.ObjectLocator!),
            cancellationToken);

        await CollectAsync(
            referenced,
            dbContext.OutgoingEmailContents
                .AsNoTracking()
                .Where(content => content.Backend == ContentStorageBackend.ObjectStorage
                    && objectLocators.Contains(content.ObjectLocator!))
                .Select(content => content.ObjectLocator!),
            cancellationToken);

        await CollectAsync(
            referenced,
            dbContext.MailDraftContents
                .AsNoTracking()
                .Where(content => content.Backend == ContentStorageBackend.ObjectStorage
                    && objectLocators.Contains(content.ObjectLocator!))
                .Select(content => content.ObjectLocator!),
            cancellationToken);

        await CollectAsync(
            referenced,
            dbContext.RecurringSendDrafts
                .AsNoTracking()
                .Where(draft => draft.Backend == ContentStorageBackend.ObjectStorage
                    && objectLocators.Contains(draft.ObjectLocator!))
                .Select(draft => draft.ObjectLocator!),
            cancellationToken);

        await CollectAsync(
            referenced,
            dbContext.StoredFiles
                .AsNoTracking()
                .Where(file => file.Backend == ContentStorageBackend.ObjectStorage
                    && objectLocators.Contains(file.ObjectLocator!))
                .Select(file => file.ObjectLocator!),
            cancellationToken);

        // The one reference that is not a mail payload. An export's archive is an object like any other and the sweep
        // would remove it the moment it was completed, because nothing else in this deployment points at it.
        await CollectAsync(
            referenced,
            dbContext.MailboxExports
                .AsNoTracking()
                .Where(export => export.ObjectLocator != null
                    && objectLocators.Contains(export.ObjectLocator))
                .Select(export => export.ObjectLocator!),
            cancellationToken);

        return referenced;
    }

    private static async Task CollectAsync(
        HashSet<string> referenced,
        IQueryable<string> objectLocators,
        CancellationToken cancellationToken)
    {
        foreach (var objectLocator in await objectLocators.ToArrayAsync(cancellationToken))
        {
            referenced.Add(objectLocator);
        }
    }
}
