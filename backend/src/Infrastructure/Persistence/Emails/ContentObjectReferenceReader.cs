// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.ObjectStorage;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Reads which of a page of object keys the four payload tables still point at.</summary>
/// <remarks>
/// <para>
/// Four queries rather than one union, because the four tables are unrelated and PostgreSQL plans each of them against
/// its own index over the locator column. Every one of them is filtered to the object backend as well as to the keys,
/// which is what keeps a deployment that has only ever written to the database answering from an empty index.
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
