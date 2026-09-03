// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Portraits;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Portraits;

/// <summary>Keeps the picture one person is drawn by in PostgreSQL, one owner's row at a time.</summary>
/// <remarks>
/// <para>
/// The read is a primary-key lookup that projects the octets alone, the write is one statement, and the removal is
/// one more. All three name the owner as a parameter and none can be aimed at another row, so an identifier learned
/// elsewhere reaches nobody else's portrait.
/// </para>
/// <para>
/// The write is an upsert rather than a read followed by an insert or an update, for the reason a person's
/// preferences are written that way: two of one person's devices saving at once is exactly the shape that reads
/// nothing twice and then violates the key. One statement settles it in the database, which is what makes
/// last-write-wins true rather than merely intended.
/// </para>
/// <para>
/// It inserts from the owner row rather than blindly, so an owner this deployment no longer holds affects no row and
/// is reported as such rather than raising a foreign-key violation inside a request.
/// </para>
/// <para>
/// The read carries no octet ceiling because the transport already refuses a body over the portrait's limit before a
/// handler is entered, and this store is the only writer. A row larger than that bound is a row something other than
/// this wrote.
/// </para>
/// <para>
/// Nothing logs. What the row holds is a picture of one identified person, and the identifier is what a failure
/// carries.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class OwnerPortraitStore(MailFathomDbContext context, TimeProvider clock) : IOwnerPortraitStore
{
    /// <inheritdoc />
    public async Task<ReadOnlyMemory<byte>?> ReadAsync(MailOwnerId owner, CancellationToken cancellationToken)
    {
        RequireNamed(owner);

        var ownerValue = owner.Value;

        // The provider's own contract is an array, and this is the adapter that turns it into the memory the
        // application boundary is written in.
        var stored = await context.OwnerPortraits
            .AsNoTracking()
            .Where(portrait => portrait.OwnerId == ownerValue)
            .Select(portrait => portrait.Content)
            .FirstOrDefaultAsync(cancellationToken);

        return stored is null ? null : stored.AsMemory();
    }

    /// <inheritdoc />
    public async Task<bool> SaveAsync(MailOwnerId owner, OwnerPortrait portrait, CancellationToken cancellationToken)
    {
        RequireNamed(owner);
        ArgumentNullException.ThrowIfNull(portrait);

        var written = clock.GetUtcNow();

        var rows = await context.Database.ExecuteSqlRawAsync(
            OwnerPortraitUpsertStatement.Compose(context.Model),
            [owner.Value, portrait.Content.ToArray(), written],
            cancellationToken);

        return rows > 0;
    }

    /// <inheritdoc />
    public async Task RemoveAsync(MailOwnerId owner, CancellationToken cancellationToken)
    {
        RequireNamed(owner);

        var ownerValue = owner.Value;

        await context.OwnerPortraits
            .Where(portrait => portrait.OwnerId == ownerValue)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static void RequireNamed(MailOwnerId owner)
    {
        if (!owner.IsSpecified)
        {
            throw new ArgumentException(
                "A portrait is read, written, and removed for a named owner, and the value names nobody.",
                nameof(owner));
        }
    }
}
