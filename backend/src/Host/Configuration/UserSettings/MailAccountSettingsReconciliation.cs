// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Infrastructure.Persistence.Users;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>Reads the settings columns of every account an older build wrote out of the document it wrote.</summary>
/// <remarks>
/// <para>
/// During a rolling upgrade a build older than the columns every question about every account filters on still creates
/// and saves accounts, and it writes the document alone. Each such row is read again here, on every replica's
/// convergence interval, so the columns trail its document by at most one interval once the write commits.
/// </para>
/// <para>
/// Every replica runs it, and two that reach one row both read the same document: the record is conditional on the
/// version they read, so the slower one either writes the same settings again or, where the account moved on, nothing.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this service.")]
internal sealed class MailAccountSettingsReconciliation(IServiceScopeFactory scopes)
{
    /// <summary>The most accounts read again per interval, so a long rolling upgrade is caught up over several rather than in one unbounded read.</summary>
    internal const int MaximumAccountsPerReading = 100;

    /// <summary>Reads again the settings of up to <see cref="MaximumAccountsPerReading" /> accounts whose columns trail their document.</summary>
    /// <param name="cancellationToken">Cancels the reading.</param>
    /// <returns>A task that completes once those accounts were recorded.</returns>
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IMailAccountRecordStore>();

        foreach (var account in await accounts.ReadWithUnreadSettingsAsync(MaximumAccountsPerReading, cancellationToken))
        {
            await accounts.RecordSettingsAsync(account, MailAccountQueryableSettingsReading.Of(account), cancellationToken);
        }
    }
}
