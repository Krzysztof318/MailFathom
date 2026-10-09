// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.Infrastructure.Persistence.Users.AccountSettings;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>Reads the settings columns of every account an older build wrote out of the document it wrote.</summary>
/// <remarks>
/// <para>
/// During a rolling upgrade a build older than the columns every question about every account filters on still creates
/// and saves accounts, and it writes the document alone; the migration that added the columns leaves every row it
/// filled the same way, for the host's own binding to read again. Each such row is read again here, on every replica's
/// convergence interval, <see cref="MaximumAccountsPerReading" /> at a time in identifier order. So a row is read again
/// within one interval of its write while no more than that many trail, and a larger backlog — the one the migration
/// leaves above all — drains that many per interval. Adding replicas does not drain it faster, because every replica
/// reads the same rows first.
/// </para>
/// <para>
/// Every replica runs it, and two that reach one row both read the same document: the record is conditional on the
/// version they read, so the slower one either writes the same settings again or, where the account moved on, nothing.
/// </para>
/// <para>
/// One account whose reading fails is reported and stepped over rather than ending the reading, because the rows are
/// read in the same order on every interval and a failure that ended it would leave every account behind that one
/// trailing for as long as it fails.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this service.")]
internal sealed partial class MailAccountSettingsReconciliation(
    IServedMailAccountReader reader,
    IServiceScopeFactory scopes,
    ILogger<MailAccountSettingsReconciliation> logger)
{
    /// <summary>The most accounts read again per interval, so a long backlog is caught up over several rather than in one unbounded read.</summary>
    internal const int MaximumAccountsPerReading = 100;

    /// <summary>Reads again the settings of up to <see cref="MaximumAccountsPerReading" /> accounts whose columns trail their document.</summary>
    /// <param name="cancellationToken">Cancels the reading.</param>
    /// <returns>A task that completes once each of those accounts was recorded or reported.</returns>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "One account that cannot be recorded is reported and stepped over, so the accounts behind it in identifier order are not held back by it on every interval.")]
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        var trailing = await reader.ReadTrailingSettingsAsync(MaximumAccountsPerReading, cancellationToken);

        if (trailing.Count == 0)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IMailAccountRecordStore>();

        foreach (var account in trailing)
        {
            try
            {
                var settings = account.Account is { } record
                    ? MailAccountQueryableSettingsReading.Of(record)
                    : MailAccountQueryableSettings.Unreadable;

                await accounts.RecordSettingsAsync(account.Id, account.Version, settings, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception failure)
            {
                this.LogAccountNotRecorded(account.Id, failure);
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The settings of mail account {AccountId} could not be read again out of its document, so every question asked of all accounts at once goes on answering for it from what its columns held; it is read again on the next interval.")]
    private partial void LogAccountNotRecorded(Guid accountId, Exception exception);
}
