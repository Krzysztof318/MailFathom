// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>Reads the user records this deployment holds out of PostgreSQL.</summary>
/// <remarks>
/// <para>
/// The user rows are the whole of what this reads, and it projects the envelope alone: the document beside it is the
/// user's own configurable record, and nothing that asks who this deployment holds has any business materializing one
/// — least of all everybody's at once.
/// </para>
/// <para>
/// The order is by the instant a user was recorded, so "the first user" is a stable answer rather than whichever
/// row the database returned first. What a caller does about the roster is theirs to decide: this reports what is
/// there, and the startup gate is what reconciles it against what configuration declares.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PersistedMailUserDirectory(MailFathomDbContext dbContext) : IMailUserDirectory
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MailUserRecord>> ReadUsersAsync(int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var users = await dbContext.UserAccounts
            .AsNoTracking()
            .OrderBy(user => user.CreatedAt)
            .ThenBy(user => user.Id)
            .Select(user => new
            {
                user.Id,
                user.DisplayName,
            })
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        return [.. users.Select(user => new MailUserRecord(MailUserId.Create(user.Id), user.DisplayName))];
    }

    /// <inheritdoc />
    public async Task<MailUserRecord?> ReadUserAsync(MailUserId user, CancellationToken cancellationToken)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A user envelope is read for a named user.", nameof(user));
        }

        var userId = user.Value;

        // The same projection the roster read makes, on the primary key: the document beside the envelope is the
        // user's own record and nothing asking what this deployment records about a person materializes one.
        var displayName = await dbContext.UserAccounts
            .AsNoTracking()
            .Where(record => record.Id == userId)
            .Select(record => record.DisplayName)
            .SingleOrDefaultAsync(cancellationToken);

        return displayName is null ? null : new MailUserRecord(user, displayName);
    }
}
