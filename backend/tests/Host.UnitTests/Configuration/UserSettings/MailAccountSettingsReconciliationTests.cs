// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Synchronization;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Users;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings;

/// <summary>
/// Covers reading again the settings of an account an older build wrote the document of alone, which is what keeps every
/// question about every account from answering out of settings its document no longer holds.
/// </summary>
public sealed class MailAccountSettingsReconciliationTests
{
    [Fact]
    public async Task ReconcileAsync_AnAccountWhoseSettingsNothingRead_RecordsWhatItsDocumentHolds()
    {
        // Arrange
        var accounts = new InMemoryMailAccountRecordStore();
        var account = Account(1, """{"Mode":"Push"}""");

        accounts.HoldAccount(account);

        // Act
        await Reconciliation(accounts).ReconcileAsync(TestContext.Current.CancellationToken);

        // Assert
        var recorded = Assert.Contains(account.Id, accounts.Settings);
        Assert.True(recorded.IsReadable);
        Assert.Equal(MailSynchronizationMode.Push, recorded.SynchronizationMode);
    }

    [Fact]
    public async Task ReconcileAsync_AnAccountWhoseDocumentDoesNotBind_RecordsItAsTakingPartInNothing()
    {
        // Arrange
        var accounts = new InMemoryMailAccountRecordStore();
        var account = Account(1, """{"NoSuchSetting":true}""");

        accounts.HoldAccount(account);

        // Act
        await Reconciliation(accounts).ReconcileAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(Assert.Contains(account.Id, accounts.Settings).IsReadable);
    }

    /// <summary>A long rolling upgrade is caught up over several intervals rather than read whole in one.</summary>
    [Fact]
    public async Task ReconcileAsync_MoreAccountsThanOneReadingTakes_RecordsTheRestOnTheNextReading()
    {
        // Arrange
        var accounts = new InMemoryMailAccountRecordStore();
        var reconciliation = Reconciliation(accounts);

        foreach (var ordinal in Enumerable.Range(1, MailAccountSettingsReconciliation.MaximumAccountsPerReading + 1))
        {
            accounts.HoldAccount(Account(ordinal, "{}"));
        }

        // Act
        await reconciliation.ReconcileAsync(TestContext.Current.CancellationToken);
        var recordedByTheFirst = accounts.Settings.Count;
        await reconciliation.ReconcileAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailAccountSettingsReconciliation.MaximumAccountsPerReading, recordedByTheFirst);
        Assert.Equal(MailAccountSettingsReconciliation.MaximumAccountsPerReading + 1, accounts.Settings.Count);
    }

    private static MailAccountSettingsReconciliation Reconciliation(InMemoryMailAccountRecordStore accounts) =>
        new(MailAccountRecordScopes.Resolving(accounts));

    private static MailAccountRecord Account(int ordinal, string document) =>
        new(new Guid($"0199a0c0-0000-7000-8000-{ordinal:D12}"), $"account-{ordinal}@example.test", $"Account {ordinal}", document, Version: 3);
}
