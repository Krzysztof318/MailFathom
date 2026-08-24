// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Owners;

/// <summary>Reads the owner records this deployment holds out of PostgreSQL.</summary>
/// <remarks>
/// <para>
/// The owner rows are the whole of what this reads, and it projects the identity alone: the document beside it is the
/// owner's own configurable record, and nothing that asks how many owners there are has any business materializing one.
/// </para>
/// <para>
/// The order is the one <see cref="OwnerAccountResolver" /> reads in, so "the first owner" means the same owner to both.
/// They stay two readers because they answer differently and run in different places: that one runs inside the caller's
/// transaction while a folder binding is being written and refuses a deployment holding zero or several, and this one
/// reports what is there to a caller that decides for itself what to do about the count.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PersistedMailOwnerDirectory(MailFathomDbContext dbContext) : IMailOwnerDirectory
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MailOwnerId>> ReadOwnersAsync(int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var owners = await dbContext.OwnerAccounts
            .AsNoTracking()
            .OrderBy(owner => owner.CreatedAt)
            .ThenBy(owner => owner.Id)
            .Select(owner => owner.Id)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        return [.. owners.Select(MailOwnerId.Create)];
    }
}
