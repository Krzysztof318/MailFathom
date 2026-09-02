// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Owners;

/// <summary>Reads the owner records this deployment holds out of PostgreSQL.</summary>
/// <remarks>
/// <para>
/// The owner rows are the whole of what this reads, and it projects the envelope alone: the document beside it is the
/// owner's own configurable record, and nothing that asks who this deployment holds has any business materializing one
/// — least of all everybody's at once.
/// </para>
/// <para>
/// The order is by the instant an owner was recorded, so "the first owner" is a stable answer rather than whichever
/// row the database returned first. What a caller does about the roster is theirs to decide: this reports what is
/// there, and the startup gate is what reconciles it against what configuration declares.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PersistedMailOwnerDirectory(MailFathomDbContext dbContext) : IMailOwnerDirectory
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MailOwnerRecord>> ReadOwnersAsync(int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var owners = await dbContext.OwnerAccounts
            .AsNoTracking()
            .OrderBy(owner => owner.CreatedAt)
            .ThenBy(owner => owner.Id)
            .Select(owner => new
            {
                owner.Id,
                owner.DisplayName,
                owner.DocumentWrittenAtRuntime,
            })
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        return
        [
            .. owners.Select(owner => new MailOwnerRecord(
                MailOwnerId.Create(owner.Id),
                owner.DisplayName,
                owner.DocumentWrittenAtRuntime)),
        ];
    }
}
