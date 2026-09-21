// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.StoredFiles;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.StoredFiles;

/// <summary>Keeps users' binary files in the <c>stored_files</c> table, with their octets wherever content is stored next.</summary>
/// <remarks>
/// <para>
/// The octets are placed through <see cref="IEmailContentStore.PlaceContentAsync" />, the placement every mail payload
/// takes, so a file follows <c>ContentStorage:Backend</c> by the same rule and under the same ordering: the object is
/// written before the row that points at it, and no transaction is open across the endpoint call. A row that never
/// commits leaves an object nothing points at, which the object sweep removes.
/// </para>
/// <para>
/// Every read and every removal names the owner in its predicate, so a file of somebody else reads as absent.
/// </para>
/// <para>
/// Nothing logs. A file is something one identified person supplied, and the identifiers are what a failure carries.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredFileStore(
    MailFathomDbContext dbContext,
    IEmailContentStore contentStore,
    ReleasedContentObjectEraser releasedObjects,
    TimeProvider timeProvider,
    IEmailContentObjectStore? objectStore = null) : IStoredFileStore
{
    /// <summary>Names the files older than a floor whose identifier their owner's document does not carry.</summary>
    /// <remarks>
    /// Raw rather than composed in LINQ because the document is a <c>jsonb</c> column the model maps as text, and the
    /// containment test has to run over its rendering. The rendering is lowered so an identifier written in capitals is
    /// still a mention.
    /// </remarks>
    private const string UnmentionedFiles =
        """
        SELECT f."Id" AS "File", f."UserId" AS "Owner"
        FROM stored_files AS f
        JOIN settings_accounts AS u ON u."Id" = f."UserId"
        WHERE f."CreatedAt" < {0}
          AND strpos(lower(u."Document"::text), f."Id"::text) = 0
        ORDER BY f."CreatedAt"
        LIMIT {1}
        """;

    /// <inheritdoc />
    public async Task<StoredFileId?> WriteAsync(
        UserId owner,
        string mediaType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        RequireNamed(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(mediaType.Length, StoredFileEntity.MaximumMediaTypeLength);

        if (content.IsEmpty)
        {
            throw new ArgumentException("A stored file carries octets.", nameof(content));
        }

        var ownerValue = owner.Value;

        if (!await dbContext.UserAccounts.AnyAsync(user => user.Id == ownerValue, cancellationToken))
        {
            return null;
        }

        var placed = await contentStore.PlaceContentAsync(EmailContentKind.StoredFile, content, cancellationToken);

        var file = new StoredFileEntity
        {
            Id = Guid.NewGuid(),
            UserId = ownerValue,
            MediaType = mediaType,
            ByteLength = placed.ByteLength,
            Sha256Hash = placed.Sha256Hash.ToArray(),
            Backend = placed.Backend,
            Content = placed.Backend == ContentStorageBackend.Database ? placed.RawMime.ToArray() : null,
            ObjectLocator = placed.ObjectLocator,
            CreatedAt = timeProvider.GetUtcNow(),
        };

        var entry = dbContext.StoredFiles.Add(file);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException refused)
            when (refused.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // The user was erased between the read above and this insert, which is the same answer as never having
            // been held. An object already placed is left to the sweep, because nothing points at it.
            return null;
        }
        finally
        {
            entry.State = EntityState.Detached;
        }

        return StoredFileId.Create(file.Id);
    }

    /// <inheritdoc />
    /// <remarks>
    /// An object-backed file whose object is absent is answered from the copy a move left in the database while one is
    /// still there, for the reason a mail payload is: the deployment still has the bytes.
    /// </remarks>
    public async Task<ReadOnlyMemory<byte>?> ReadAsync(
        UserId owner,
        StoredFileId file,
        CancellationToken cancellationToken)
    {
        RequireNamed(owner, file);

        var ownerValue = owner.Value;
        var fileValue = file.Value;

        var stored = await dbContext.StoredFiles
            .AsNoTracking()
            .Where(held => held.Id == fileValue && held.UserId == ownerValue)
            .Select(held => new { held.Backend, held.Content, held.ObjectLocator, held.ByteLength })
            .SingleOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            return null;
        }

        if (stored is { Backend: ContentStorageBackend.ObjectStorage, ObjectLocator: { } objectLocator }
            && objectStore is not null
            && await objectStore.FindAsync(objectLocator, stored.ByteLength, cancellationToken) is { } held)
        {
            return held;
        }

        if (stored.Content is null)
        {
            return null;
        }

        return new ReadOnlyMemory<byte>(stored.Content);
    }

    /// <inheritdoc />
    public Task<bool> HoldsAsync(UserId owner, StoredFileId file, CancellationToken cancellationToken)
    {
        RequireNamed(owner, file);

        var ownerValue = owner.Value;
        var fileValue = file.Value;

        return dbContext.StoredFiles.AnyAsync(
            held => held.Id == fileValue && held.UserId == ownerValue,
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The key is read before the row goes, because afterwards nothing names the object. A move repointing the row
    /// between the read and the removal leaves its object unnamed here, which is an orphan the object sweep removes.
    /// </remarks>
    public async Task RemoveAsync(UserId owner, StoredFileId file, CancellationToken cancellationToken)
    {
        RequireNamed(owner, file);

        var ownerValue = owner.Value;
        var fileValue = file.Value;

        var objectLocator = await dbContext.StoredFiles
            .AsNoTracking()
            .Where(held => held.Id == fileValue
                && held.UserId == ownerValue
                && held.Backend == ContentStorageBackend.ObjectStorage)
            .Select(held => held.ObjectLocator)
            .SingleOrDefaultAsync(cancellationToken);

        var removed = await dbContext.StoredFiles
            .Where(held => held.Id == fileValue && held.UserId == ownerValue)
            .ExecuteDeleteAsync(cancellationToken);

        if (removed > 0 && objectLocator is not null)
        {
            await releasedObjects.EraseAsync([objectLocator], cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HeldStoredFile>> FindUnmentionedAsync(
        DateTimeOffset writtenBefore,
        int maxFiles,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFiles);

        var rows = await dbContext.Database
            .SqlQueryRaw<UnmentionedFileRow>(UnmentionedFiles, writtenBefore, maxFiles)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new HeldStoredFile(StoredFileId.Create(row.File), UserId.Create(row.Owner)))];
    }

    private static void RequireNamed(UserId owner)
    {
        if (!owner.IsSpecified)
        {
            throw new ArgumentException("A stored file belongs to a named user, and the value names nobody.", nameof(owner));
        }
    }

    private static void RequireNamed(UserId owner, StoredFileId file)
    {
        RequireNamed(owner);

        if (!file.IsSpecified)
        {
            throw new ArgumentException("The value names no stored file.", nameof(file));
        }
    }

    /// <summary>One candidate as the raw query answers it.</summary>
    private sealed class UnmentionedFileRow
    {
        public Guid File { get; init; }

        public Guid Owner { get; init; }
    }
}
