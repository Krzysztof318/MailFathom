// Copyright © 2026 Krzysztof Kasprowicz

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Synchronization;
using MailMcp.Domain.Transport;
using MailMcp.Host.Configuration;
using MailMcp.Infrastructure.Certificates;
using MailMcp.Infrastructure.Mail;
using MailMcp.Infrastructure.Secrets;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailMcp.Host.UnitTests;

public sealed class MailSynchronizationOptionsTests
{
    [Fact]
    public void ValidateForSynchronization_DisabledWithNoAccounts_ReportsNoError()
    {
        // Arrange
        var options = new MailSynchronizationOptions();

        // Act
        var results = options.ValidateForSynchronization().ToArray();

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void ValidateForSynchronization_EnabledWithNoAccounts_RequiresAnAccount()
    {
        // Arrange
        var options = new MailSynchronizationOptions { Enabled = true };

        // Act
        var results = options.ValidateForSynchronization().ToArray();

        // Assert
        var result = Assert.Single(results);
        Assert.Contains("At least one account", result.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>A ceiling below the interval would defer a failing account by less than a healthy one waits.</summary>
    [Fact]
    public void ValidateForSynchronization_FailureBackoffCeilingBelowTheInterval_IsRejected()
    {
        // Arrange
        var options = new MailSynchronizationOptions
        {
            Interval = TimeSpan.FromMinutes(10),
            MaxFailureBackoff = TimeSpan.FromMinutes(5),
        };

        // Act
        var results = options.ValidateForSynchronization().ToArray();

        // Assert
        var result = Assert.Single(results);
        Assert.Contains("shorter than the synchronization interval", result.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>No backoff at all is a legitimate choice, and it is the one a ceiling equal to the interval expresses.</summary>
    [Fact]
    public void ValidateForSynchronization_FailureBackoffCeilingEqualsTheInterval_ReportsNoError()
    {
        // Arrange
        var options = new MailSynchronizationOptions
        {
            Interval = TimeSpan.FromMinutes(10),
            MaxFailureBackoff = TimeSpan.FromMinutes(10),
        };

        // Act
        var results = options.ValidateForSynchronization().ToArray();

        // Assert
        Assert.Empty(results);
    }

    /// <summary>Both bounds are enforced at run time, so a value outside their range has to fail startup rather than reach a scheduler.</summary>
    [Theory]
    [InlineData("MaxConcurrentAccounts", 0)]
    [InlineData("MaxConcurrentAccounts", 101)]
    [InlineData("MaxConcurrentFoldersPerAccount", 0)]
    [InlineData("MaxConcurrentFoldersPerAccount", 21)]
    public void Bind_ConcurrencyBoundOutsideItsRange_FailsDataAnnotationValidation(string settingName, int configuredValue)
    {
        // Arrange
        var options = new MailSynchronizationOptions();
        new ConfigurationBuilder()
            .AddInMemoryCollection([
                new KeyValuePair<string, string?>(settingName, configuredValue.ToString(CultureInfo.InvariantCulture)),
            ])
            .Build()
            .Bind(options);

        // Act
        var results = Validator.TryValidateObject(options, new ValidationContext(options), null, validateAllProperties: true);

        // Assert
        Assert.False(results);
    }

    /// <summary>A drain the host stops waiting for is a drain that was never honored, so the budget has to cover it.</summary>
    [Theory]
    [InlineData("00:00:00", "00:00:30")]
    [InlineData("00:00:10", "00:00:30")]
    [InlineData("00:00:25", "00:00:30")]
    [InlineData("00:00:40", "00:00:45")]
    [InlineData("00:02:00", "00:02:05")]
    public void ResolveHostShutdownBudget_ConfiguredDrain_CoversItWithoutFallingBelowTheFrameworkDefault(
        string configuredDrain,
        string expectedBudget)
    {
        // Arrange
        var shutdownDrainTimeout = TimeSpan.Parse(configuredDrain, CultureInfo.InvariantCulture);

        // Act
        var budget = MailSynchronizationOptions.ResolveHostShutdownBudget(shutdownDrainTimeout);

        // Assert
        Assert.Equal(TimeSpan.Parse(expectedBudget, CultureInfo.InvariantCulture), budget);
        Assert.True(budget > shutdownDrainTimeout);
    }

    /// <summary>Configuration defines the served accounts, normalized and ordered the way a resolved query scope needs them.</summary>
    [Fact]
    public void ServedAccountIds_ConfiguredAccounts_AreNormalizedDeduplicatedAndOrdered()
    {
        // Arrange
        var options = new MailSynchronizationOptions
        {
            Accounts = [CreateAccount("  secondary  "), CreateAccount("primary"), CreateAccount("secondary")],
        };

        // Act
        var servedAccountIds = options.ServedAccountIds;

        // Assert
        Assert.Equal([MailAccountId.Create("primary"), MailAccountId.Create("secondary")], servedAccountIds);
    }

    /// <summary>Casing is part of an account identifier, so two spellings of one name are two accounts here.</summary>
    [Fact]
    public void ServedAccountIds_AccountNamedInAnotherCase_IsNotTheConfiguredAccount()
    {
        // Arrange
        var options = new MailSynchronizationOptions { Accounts = [CreateAccount("primary")] };

        // Act
        var servedAccountIds = options.ServedAccountIds;

        // Assert
        Assert.DoesNotContain(MailAccountId.Create("PRIMARY"), servedAccountIds);
    }

    /// <summary>Switching synchronization off stops runs from fetching mail; it does not hide the copy already stored.</summary>
    [Fact]
    public void ServedAccountIds_SynchronizationDisabled_StillNamesTheConfiguredAccount()
    {
        // Arrange
        var options = new MailSynchronizationOptions { Enabled = false, Accounts = [CreateAccount("primary")] };

        // Act, Assert
        Assert.Equal(MailAccountId.Create("primary"), Assert.Single(options.ServedAccountIds));
    }

    [Fact]
    public void ServedAccountIds_NoAccountsConfigured_ServesNothing()
    {
        // Arrange
        var options = new MailSynchronizationOptions();

        // Act, Assert
        Assert.Empty(options.ServedAccountIds);
    }

    /// <summary>An account whose identifier never bound is not a served account, and reading the set does not fail on it.</summary>
    [Fact]
    public void ServedAccountIds_AccountWithNoIdentifier_IsSkipped()
    {
        // Arrange
        var options = new MailSynchronizationOptions { Accounts = [CreateAccount("primary"), CreateAccount("   ")] };

        // Act, Assert
        Assert.Equal(MailAccountId.Create("primary"), Assert.Single(options.ServedAccountIds));
    }

    [Fact]
    public void ValidateForSynchronization_AccountIdsDifferingOnlyByNormalization_ReportsThemAsDuplicates()
    {
        // Arrange
        var options = new MailSynchronizationOptions
        {
            Accounts =
            [
                CreateAccount("primary"),
                CreateAccount("  primary  "),
            ],
        };

        // Act
        var results = options.ValidateForSynchronization().ToArray();

        // Assert
        Assert.Contains(results, result => result.ErrorMessage!.Contains("unique after normalization", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateForSynchronization_EnabledAccountMissingHostAndUserName_ReportsBoth()
    {
        // Arrange
        var options = new MailSynchronizationOptions
        {
            Enabled = true,
            Accounts = [new MailSynchronizationAccountOptions { AccountId = "primary" }],
        };

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.Contains(messages, message => message!.Contains("IMAP host is required", StringComparison.Ordinal));
        Assert.Contains(messages, message => message!.Contains("IMAP user name is required", StringComparison.Ordinal));
    }

    /// <summary>The password is no longer a configuration value, so its absence is a resolution failure rather than a binding rule.</summary>
    [Fact]
    public void ValidateForSynchronization_EnabledAccountWithoutASecretReference_ReportsNoPasswordRule()
    {
        // Arrange
        var options = new MailSynchronizationOptions
        {
            Enabled = true,
            Accounts = [CreateAccount("primary", secretReference: string.Empty)],
        };

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.DoesNotContain(messages, message => message!.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateForSynchronization_UnsafeTransportSecurity_NamesTheAccountAndTheViolationIdentity()
    {
        // Arrange
        var account = CreateAccount("primary");
        account.TransportSecurity.ConnectionSecurity = MailConnectionSecurity.None;
        var options = new MailSynchronizationOptions { Enabled = true, Accounts = [account] };

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.Contains(messages, message => message!.Contains("Account 'primary'", StringComparison.Ordinal)
            && message.Contains(nameof(MailTransportSecurityViolation.UnencryptedConnectionRequiresExplicitOptIn), StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateForSynchronization_UnsafeTransportSecurity_NeverNamesTheUserNameOrTheSecretReference()
    {
        // Arrange
        var account = CreateAccount("primary", secretReference: "systemd-credential:imap-primary-password");
        account.UserName = "mailmcp@example.test";
        account.TransportSecurity.ConnectionSecurity = MailConnectionSecurity.None;
        var options = new MailSynchronizationOptions { Enabled = true, Accounts = [account] };

        // Act
        var messages = string.Join(' ', options.ValidateForSynchronization().Select(result => result.ErrorMessage));

        // Assert
        Assert.DoesNotContain("mailmcp@example.test", messages, StringComparison.Ordinal);
        Assert.DoesNotContain("imap-primary-password", messages, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateForSynchronization_DuplicateFolderAliasesAfterNormalization_ReportsThem()
    {
        // Arrange
        var account = CreateAccount("primary");
        account.Folders =
        [
            new MailFolderMappingOptions { Alias = "inbox", SpecialUse = "Inbox" },
            new MailFolderMappingOptions { Alias = "  INBOX  ", RemotePath = "INBOX" },
        ];
        var options = new MailSynchronizationOptions { Accounts = [account] };

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.Contains(messages, message => message!.Contains("Configured folder aliases must be unique", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("INBOX", "Inbox")]
    [InlineData(null, "NotARole")]
    public void ValidateForSynchronization_FolderNamingNeitherOrBothTargets_ReportsIt(string? remotePath, string? specialUse)
    {
        // Arrange
        var account = CreateAccount("primary");
        account.Folders = [new MailFolderMappingOptions { Alias = "inbox", RemotePath = remotePath, SpecialUse = specialUse }];
        var options = new MailSynchronizationOptions { Accounts = [account] };

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.Contains(messages, message => message!.Contains("Folder alias 'inbox'", StringComparison.Ordinal));
    }

    /// <summary>The default names the inbox by role, so a server that calls it something else still synchronizes.</summary>
    [Fact]
    public void EffectiveFolders_FoldersOmitted_AppliesThePostBindingInboxRoleDefault()
    {
        // Arrange
        var account = new MailSynchronizationAccountOptions();

        // Act
        var mapping = Assert.Single(account.EffectiveFolders).CreateMapping();

        // Assert
        Assert.Equal("INBOX", mapping.Alias.Value);
        Assert.Equal(MailFolderSpecialUse.Inbox, mapping.SpecialUse);
        Assert.Null(mapping.RemotePath);
    }

    [Fact]
    public void GetPolicy_ConfiguredAccount_ReturnsTheAccountsValidatedDomainPolicy()
    {
        // Arrange
        var options = new MailSynchronizationOptions { Accounts = [CreateAccount("  primary  ")] };

        // Act
        var policy = options.GetPolicy(MailAccountId.Create("primary"));

        // Assert
        Assert.Equal(MailConnectionSecurity.TlsOnConnect, policy.ConnectionSecurity);
    }

    [Fact]
    public void GetWindow_AccountBoundingHowFarBackToReach_ReturnsThatDateAsTheWindow()
    {
        // Arrange
        var account = CreateAccount("primary");
        account.EarliestEmailReceivedDate = new DateOnly(2024, 1, 1);
        var options = new MailSynchronizationOptions { Accounts = [account] };

        // Act
        var window = options.GetWindow(MailAccountId.Create("primary"));

        // Assert
        Assert.Equal(new DateOnly(2024, 1, 1), window.EarliestEmailReceivedDate);
    }

    /// <summary>Configuring no bound keeps the behavior every existing deployment has, which is to reach everything.</summary>
    [Fact]
    public void GetWindow_AccountWithNoConfiguredDate_ReturnsAnUnboundedWindow()
    {
        // Arrange
        var options = new MailSynchronizationOptions { Accounts = [CreateAccount("primary")] };

        // Act
        var window = options.GetWindow(MailAccountId.Create("primary"));

        // Assert
        Assert.Equal(MailSynchronizationWindow.Unbounded, window);
    }

    /// <summary>The two accounts of one deployment can answer differently, which is the reason the setting is per account.</summary>
    [Fact]
    public void GetDisposition_AccountsConfiguringDifferentDispositions_AnswersPerAccount()
    {
        // Arrange
        var followingServer = CreateAccount("following-server");
        followingServer.RemotelyDeletedEmailDisposition = RemotelyDeletedEmailDisposition.EraseLocalCopy;
        var options = new MailSynchronizationOptions { Accounts = [followingServer, CreateAccount("archive")] };

        // Act
        var dispositions = options.ServedAccountIds
            .Select(options.GetDisposition)
            .ToArray();

        // Assert
        Assert.Equal(
            [RemotelyDeletedEmailDisposition.RetainTombstone, RemotelyDeletedEmailDisposition.EraseLocalCopy],
            dispositions);
    }

    /// <summary>Configuring nothing keeps the reversible outcome, so no deployment loses mail by omission.</summary>
    [Fact]
    public void GetDisposition_AccountConfiguringNoDisposition_KeepsTheLocalRowAsATombstone()
    {
        // Arrange
        var options = new MailSynchronizationOptions { Accounts = [CreateAccount("primary")] };

        // Act
        var disposition = options.GetDisposition(MailAccountId.Create("primary"));

        // Assert
        Assert.Equal(RemotelyDeletedEmailDisposition.RetainTombstone, disposition);
    }

    [Fact]
    public void Bind_RemotelyDeletedEmailDisposition_ReadsItByName()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MailSynchronization:Accounts:0:AccountId"] = "primary",
                ["MailSynchronization:Accounts:0:RemotelyDeletedEmailDisposition"] = "EraseLocalCopy",
            })
            .Build();

        // Act
        var options = configuration.GetSection("MailSynchronization").Get<MailSynchronizationOptions>()!;

        // Assert
        Assert.Equal(
            RemotelyDeletedEmailDisposition.EraseLocalCopy,
            Assert.Single(options.Accounts).RemotelyDeletedEmailDisposition);
    }

    /// <summary>
    /// This setting decides whether stored mail is destroyed, so a name nobody can interpret must fail startup rather
    /// than fall back to a default or take the account out of synchronization altogether.
    /// </summary>
    [Fact]
    public void Bind_RemotelyDeletedEmailDispositionThatNamesNothing_FailsInsteadOfDroppingTheAccount()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MailSynchronization:Accounts:0:AccountId"] = "primary",
                ["MailSynchronization:Accounts:0:RemotelyDeletedEmailDisposition"] = "delete",
            })
            .Build();
        var options = new MailSynchronizationOptions();

        // Act, Assert
        Assert.Throws<InvalidOperationException>(() => configuration
            .GetSection("MailSynchronization")
            .Bind(options, binderOptions => binderOptions.ErrorOnUnknownConfiguration = true));
    }

    /// <summary>
    /// A bare number binds onto an enum whether or not a member carries it, and strict binding does not catch that
    /// because the conversion succeeds. Left unvalidated, an undefined value would reach reconciliation, which reads
    /// anything that is not <c>EraseLocalCopy</c> as the tombstone — a destructive setting silently doing the other
    /// thing.
    /// </summary>
    [Fact]
    public void ValidateForSynchronization_DispositionNumberNoMemberCarries_IsRejected()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MailSynchronization:Accounts:0:AccountId"] = "primary",
                ["MailSynchronization:Accounts:0:RemotelyDeletedEmailDisposition"] = "2",
            })
            .Build();
        var options = configuration.GetSection("MailSynchronization").Get<MailSynchronizationOptions>()!;

        // Act
        var results = options.ValidateForSynchronization().ToArray();

        // Assert
        var result = Assert.Single(results);
        Assert.Contains("RetainTombstone", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("EraseLocalCopy", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void FindSynchronizationWindowErrors_DateLaterThanToday_ReportsTheAccountAndTheProperty()
    {
        // Arrange
        var account = CreateAccount("primary");
        account.EarliestEmailReceivedDate = new DateOnly(2026, 8, 1);
        var options = new MailSynchronizationOptions { Accounts = [account] };

        // Act
        var result = Assert.Single(options.FindSynchronizationWindowErrors(new DateOnly(2026, 7, 24)));

        // Assert
        Assert.Contains("Account 'primary'", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("2026-08-01", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal([nameof(MailSynchronizationAccountOptions.EarliestEmailReceivedDate)], result.MemberNames);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2026-07-24")]
    [InlineData("2019-12-31")]
    public void FindSynchronizationWindowErrors_DateTodayOrEarlierOrAbsent_ReportsNoError(string? earliestEmailReceivedDate)
    {
        // Arrange
        var account = CreateAccount("primary");
        account.EarliestEmailReceivedDate = earliestEmailReceivedDate is null ? null : DateOnly.Parse(earliestEmailReceivedDate, CultureInfo.InvariantCulture);
        var options = new MailSynchronizationOptions { Accounts = [account] };

        // Act
        var results = options.FindSynchronizationWindowErrors(new DateOnly(2026, 7, 24)).ToArray();

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task ResolveSettingsAsync_ConfiguredAccount_ResolvesTheAccountPasswordForTheCallerToOwn()
    {
        // Arrange
        var account = CreateAccount("  primary  ", secretReference: "plaintext:dev-password");
        account.Host = "  imap.example.test  ";
        account.UserName = "mailmcp@example.test";
        var options = new MailSynchronizationOptions { Accounts = [account] };

        // Act
        using var settings = (await options.ResolveSettingsAsync(
            "primary",
            new PlaintextOnlySecretReferenceResolver(),
            CreateTrustAnchorLoader(),
            CancellationToken.None)).Material;

        // Assert
        Assert.Equal("dev-password", settings.Password.RevealAsString());
    }

    [Fact]
    public async Task ResolveSettingsAsync_ConfiguredAccount_CarriesTheEndpointSettingsUnchanged()
    {
        // Arrange
        var account = CreateAccount("primary", secretReference: "plaintext:dev-password");
        account.Host = " imap.example.test ";
        account.Port = 1993;
        account.UserName = "mailmcp@example.test";
        var options = new MailSynchronizationOptions { Accounts = [account] };

        // Act
        var settings = await options.ResolveSettingsAsync(
            "  primary ",
            new PlaintextOnlySecretReferenceResolver(),
            CreateTrustAnchorLoader(),
            CancellationToken.None);

        // Assert
        using (settings.Material)
        {
            Assert.Equal("primary", settings.AccountId);
            Assert.Equal("imap.example.test", settings.Host);
            Assert.Equal(1993, settings.Port);
            Assert.Equal("mailmcp@example.test", settings.UserName);
        }
    }

    [Fact]
    public async Task ResolveSettingsAsync_UnresolvableReference_FailsClosedInsteadOfReturningSettings()
    {
        // Arrange
        var options = new MailSynchronizationOptions
        {
            Accounts = [CreateAccount("primary", secretReference: "file:/run/secrets/absent")],
        };

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => options.ResolveSettingsAsync(
            "primary",
            new PlaintextOnlySecretReferenceResolver(),
            CreateTrustAnchorLoader(),
            CancellationToken.None));
    }

    [Fact]
    public void Bind_FlatColonSeparatedKeys_ProducesTheSameAccountShapeAsAJsonDocument()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MailSynchronization:Enabled"] = "true",
                ["MailSynchronization:Accounts:0:AccountId"] = "primary",
                ["MailSynchronization:Accounts:0:Host"] = "imap.example.test",
                ["MailSynchronization:Accounts:0:UserName"] = "mailmcp@example.test",
                ["MailSynchronization:Accounts:0:Secrets:Password:SecretReference"] = "systemd-credential:imap-primary-password",
            })
            .Build();

        // Act
        var options = configuration.GetSection("MailSynchronization").Get<MailSynchronizationOptions>()!;

        // Assert
        var account = Assert.Single(options.Accounts);
        Assert.Equal("systemd-credential:imap-primary-password", account.Secrets.Password.SecretReference);
        Assert.Empty(options.ValidateForSynchronization());
    }

    [Fact]
    public void Bind_EarliestEmailReceivedDate_ReadsItAsAPlainDate()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MailSynchronization:Accounts:0:AccountId"] = "primary",
                ["MailSynchronization:Accounts:0:EarliestEmailReceivedDate"] = "2024-01-01",
            })
            .Build();

        // Act
        var options = configuration.GetSection("MailSynchronization").Get<MailSynchronizationOptions>()!;

        // Assert
        Assert.Equal(new DateOnly(2024, 1, 1), Assert.Single(options.Accounts).EarliestEmailReceivedDate);
    }

    /// <summary>
    /// A bound nobody can interpret has to fail startup, and only the strict binding the host uses makes it do so: the
    /// binder treats an account as a collection item and otherwise drops the whole item, which would remove an account
    /// from synchronization over a typo in one of its dates.
    /// </summary>
    [Fact]
    public void Bind_EarliestEmailReceivedDateThatIsNotADate_FailsInsteadOfDroppingTheAccount()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MailSynchronization:Accounts:0:AccountId"] = "primary",
                ["MailSynchronization:Accounts:0:EarliestEmailReceivedDate"] = "last January",
            })
            .Build();
        var options = new MailSynchronizationOptions();

        // Act, Assert
        Assert.Throws<InvalidOperationException>(() => configuration
            .GetSection("MailSynchronization")
            .Bind(options, binderOptions => binderOptions.ErrorOnUnknownConfiguration = true));
    }

    private static TrustAnchorLoader CreateTrustAnchorLoader() =>
        new(new PlaintextOnlySecretReferenceResolver());

    private static MailSynchronizationAccountOptions CreateAccount(
        string accountId,
        string secretReference = "systemd-credential:imap-primary-password") => new()
        {
            AccountId = accountId,
            Host = "imap.example.test",
            UserName = "mailmcp@example.test",
            Secrets = new MailAccountSecretOptions
            {
                Password = new ConfiguredSecret { SecretReference = secretReference },
            },
        };
}
