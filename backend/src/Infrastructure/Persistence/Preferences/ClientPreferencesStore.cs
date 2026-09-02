// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Preferences;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Preferences;

/// <summary>Keeps one person's client preferences in PostgreSQL, one owner's row at a time.</summary>
/// <remarks>
/// <para>
/// The read is a primary-key lookup that projects the document alone, and the write is one statement. Both name the
/// owner as a parameter and neither can be aimed at another row, so an identifier learned elsewhere reaches nobody
/// else's preferences.
/// </para>
/// <para>
/// The write is an upsert rather than a read followed by an insert or an update, because two of one person's devices
/// saving at once is exactly the shape that reads nothing twice and then violates the key. One statement settles it in
/// the database, which is also what makes last-write-wins true rather than merely intended: the loser of the race
/// overwrites the winner instead of failing, which is the contract this store publishes.
/// </para>
/// <para>
/// It inserts from the owner row rather than blindly, so an owner this deployment no longer holds affects no row and
/// is reported as such — a foreign-key violation would say the same thing as an exception naming a constraint, and the
/// caller here is a person whose answer is that there is nothing of theirs here.
/// </para>
/// <para>
/// The read carries no octet ceiling, unlike the owner record's, and the difference is the documents rather than the
/// care taken over them. That one holds every mail account a person declares and can legitimately grow, so a bound is
/// what separates a large record from a row something went wrong with; this one is three scalars written by
/// <see cref="ClientPreferencesUpsertStatement" /> and by nothing else, so there is no size a correct row could reach
/// and a ceiling would be guarding against this store having been bypassed.
/// </para>
/// <para>
/// Nothing logs. What the row holds is a statement about one identified person, and the identifier is what a failure
/// carries.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class ClientPreferencesStore(MailFathomDbContext context, TimeProvider clock) : IClientPreferencesStore
{
    /// <inheritdoc />
    public async Task<ClientPreferences?> ReadAsync(MailOwnerId owner, CancellationToken cancellationToken)
    {
        RequireNamed(owner);

        var ownerValue = owner.Value;

        var document = await context.ClientPreferences
            .AsNoTracking()
            .Where(preferences => preferences.OwnerId == ownerValue)
            .Select(preferences => preferences.Document)
            .FirstOrDefaultAsync(cancellationToken);

        return document is null ? null : ClientPreferencesDocument.Parse(document);
    }

    /// <inheritdoc />
    public async Task<bool> SaveAsync(
        MailOwnerId owner,
        ClientPreferences preferences,
        CancellationToken cancellationToken)
    {
        RequireNamed(owner);
        ArgumentNullException.ThrowIfNull(preferences);

        var written = clock.GetUtcNow();

        var rows = await context.Database.ExecuteSqlRawAsync(
            ClientPreferencesUpsertStatement.Compose(context.Model),
            [owner.Value, ClientPreferencesDocument.Render(preferences), written],
            cancellationToken);

        return rows > 0;
    }

    private static void RequireNamed(MailOwnerId owner)
    {
        if (!owner.IsSpecified)
        {
            throw new ArgumentException(
                "Client preferences are read and written for a named owner, and the value names nobody.",
                nameof(owner));
        }
    }
}
