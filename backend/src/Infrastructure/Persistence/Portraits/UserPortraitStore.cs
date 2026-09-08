// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Portraits;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Portraits;

/// <summary>Keeps the picture one person is drawn by in PostgreSQL, one user's row at a time.</summary>
/// <remarks>
/// <para>
/// The read is a primary-key lookup that projects the octets alone, the write is one statement, and the removal is
/// one more. All three name the user as a parameter and none can be aimed at another row, so an identifier learned
/// elsewhere reaches nobody else's portrait.
/// </para>
/// <para>
/// The write is an upsert rather than a read followed by an insert or an update, for the reason a person's
/// preferences are written that way: two of one person's devices saving at once is exactly the shape that reads
/// nothing twice and then violates the key. One statement settles it in the database, which is what makes
/// last-write-wins true rather than merely intended.
/// </para>
/// <para>
/// It inserts from the user row rather than blindly, so a user this deployment no longer holds affects no row and
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
internal sealed class UserPortraitStore(MailFathomDbContext context, TimeProvider clock) : IUserPortraitStore
{
    /// <inheritdoc />
    public async Task<ReadOnlyMemory<byte>?> ReadAsync(MailUserId user, CancellationToken cancellationToken)
    {
        RequireNamed(user);

        var userValue = user.Value;

        // The provider's own contract is an array, and this is the adapter that turns it into the memory the
        // application boundary is written in.
        var stored = await context.UserPortraits
            .AsNoTracking()
            .Where(portrait => portrait.UserId == userValue)
            .Select(portrait => portrait.Content)
            .FirstOrDefaultAsync(cancellationToken);

        return stored is null ? null : stored.AsMemory();
    }

    /// <inheritdoc />
    public async Task<bool> SaveAsync(MailUserId user, UserPortrait portrait, CancellationToken cancellationToken)
    {
        RequireNamed(user);
        ArgumentNullException.ThrowIfNull(portrait);

        var written = clock.GetUtcNow();

        var rows = await context.Database.ExecuteSqlRawAsync(
            UserPortraitUpsertStatement.Compose(context.Model),
            [user.Value, portrait.Content.ToArray(), written],
            cancellationToken);

        return rows > 0;
    }

    /// <inheritdoc />
    public async Task RemoveAsync(MailUserId user, CancellationToken cancellationToken)
    {
        RequireNamed(user);

        var userValue = user.Value;

        await context.UserPortraits
            .Where(portrait => portrait.UserId == userValue)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static void RequireNamed(MailUserId user)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException(
                "A portrait is read, written, and removed for a named user, and the value names nobody.",
                nameof(user));
        }
    }
}
