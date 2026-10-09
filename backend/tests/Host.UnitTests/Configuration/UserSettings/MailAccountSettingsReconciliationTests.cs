// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Synchronization;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.Infrastructure.Persistence.Users.AccountSettings;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings;

/// <summary>
/// Covers reading again the settings of an account an older build wrote the document of alone, which is what keeps every
/// question about every account from answering out of settings its document no longer holds.
/// </summary>
public sealed class MailAccountSettingsReconciliationTests
{
    private const string ServableDocument =
        """
        {
          "Host": "imap.example.test",
          "UserName": "alex@example.test",
          "Secrets": { "Password": { "Name": "imap-password", "SecretReference": "systemd-credential:imap-password" } },
          "Mode": "Push"
        }
        """;

    [Fact]
    public async Task ReconcileAsync_AnAccountWhoseSettingsTrailItsDocument_RecordsWhatItsDocumentHolds()
    {
        // Arrange
        var accounts = new InMemoryMailAccountRecordStore();
        var account = Account(1, ServableDocument);
        accounts.HoldAccount(account);

        // Act
        await Reconciliation(accounts, Trailing(account)).ReconcileAsync(TestContext.Current.CancellationToken);

        // Assert
        var recorded = Assert.Contains(account.Id, accounts.Settings);
        Assert.True(recorded.IsReadable);
        Assert.Equal(MailSynchronizationMode.Push, recorded.SynchronizationMode);
    }

    /// <summary>A document past the size MailFathom binds one from is never sent, and is recorded as taking part in nothing.</summary>
    [Fact]
    public async Task ReconcileAsync_ADocumentLeftInTheDatabaseForItsSize_RecordsItAsTakingPartInNothing()
    {
        // Arrange
        var accounts = new InMemoryMailAccountRecordStore();
        var account = Account(1, ServableDocument);
        accounts.HoldAccount(account);

        // Act
        await Reconciliation(accounts, new MailAccountTrailingSettings(account.Id, account.Version, Account: null))
            .ReconcileAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(MailAccountQueryableSettings.Unreadable, Assert.Contains(account.Id, accounts.Settings));
    }

    /// <summary>The rows are read in the same order every interval, so one that cannot be recorded must not hold back the rest.</summary>
    [Fact]
    public async Task ReconcileAsync_OneAccountThatCannotBeRecorded_RecordsTheAccountsBehindItAndReportsIt()
    {
        // Arrange
        var failing = Account(1, ServableDocument);
        var behind = Account(2, ServableDocument);
        var accounts = Substitute.For<IMailAccountRecordStore>();
        accounts
            .RecordSettingsAsync(failing.Id, Arg.Any<long>(), Arg.Any<MailAccountQueryableSettings>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The folder rows were refused."));
        var logger = new RecordingLogger<MailAccountSettingsReconciliation>();

        // Act
        await Reconciliation(accounts, logger, Trailing(failing), Trailing(behind))
            .ReconcileAsync(TestContext.Current.CancellationToken);

        // Assert
        await accounts.Received(1).RecordSettingsAsync(
            behind.Id,
            behind.Version,
            Arg.Is<MailAccountQueryableSettings>(settings => settings != null && settings.IsReadable),
            Arg.Any<CancellationToken>());
        Assert.Contains(failing.Id.ToString(), Assert.Single(logger.Messages), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReconcileAsync_TheCallerCancelling_PropagatesTheCancellation()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var account = Account(1, ServableDocument);
        var accounts = Substitute.For<IMailAccountRecordStore>();
        accounts
            .RecordSettingsAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<MailAccountQueryableSettings>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        // Act, Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Reconciliation(accounts, new RecordingLogger<MailAccountSettingsReconciliation>(), Trailing(account))
                .ReconcileAsync(cancellation.Token));
    }

    private static MailAccountSettingsReconciliation Reconciliation(
        InMemoryMailAccountRecordStore accounts,
        params MailAccountTrailingSettings[] trailing) =>
        Reconciliation(accounts, new RecordingLogger<MailAccountSettingsReconciliation>(), trailing);

    private static MailAccountSettingsReconciliation Reconciliation(
        IMailAccountRecordStore accounts,
        RecordingLogger<MailAccountSettingsReconciliation> logger,
        params MailAccountTrailingSettings[] trailing)
    {
        var reader = Substitute.For<IServedMailAccountReader>();
        reader.ReadTrailingSettingsAsync(MailAccountSettingsReconciliation.MaximumAccountsPerReading, Arg.Any<CancellationToken>())
            .Returns(trailing);

        return new MailAccountSettingsReconciliation(reader, MailAccountRecordScopes.Resolving(accounts), logger);
    }

    private static MailAccountTrailingSettings Trailing(MailAccountRecord account) => new(account.Id, account.Version, account);

    private static MailAccountRecord Account(int ordinal, string document) =>
        new(new Guid($"0199a0c0-0000-7000-8000-{ordinal:D12}"), $"account-{ordinal}@example.test", $"Account {ordinal}", document, Version: 3);
}
