// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Maintenance;
using MailFathom.CodeCoverage;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>EF Core count of the mail one maintenance scope holds.</summary>
/// <remarks>
/// A read that joins no transaction, so it uses the scoped context. The scope is narrowed by the folder's alias rather
/// than by its binding, because an operator naming a folder means the folder rather than one of the bindings a
/// repointed alias has had, and every one of those bindings holds mail the same command would act on.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredMailCounter(MailFathomDbContext dbContext) : IStoredMailCounter
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    public Task<int> CountStoredEmailsAsync(StoredMailScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return StoredMailInScope.Within(dbContext.StoredEmails.AsNoTracking(), scope).CountAsync(cancellationToken);
    }
}
