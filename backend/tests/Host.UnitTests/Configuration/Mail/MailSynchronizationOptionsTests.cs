// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using MailFathom.Application.Retrieval.AskMail.Audit;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.Domain.Transport;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

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

    /// <summary>A run budget below what one message may cost leaves that message unfetchable on every later run too.</summary>
    /// <remarks>
    /// The folder would stop in front of it permanently rather than occasionally, so this is refused at startup instead
    /// of being discovered as a checkpoint that never advances.
    /// </remarks>
    [Fact]
    public void ValidateForSynchronization_PerRunContentBudgetBelowTheMessageSizeLimit_IsRejected()
    {
        // Arrange
        var options = new MailSynchronizationOptions
        {
            MaxRawMimeBytes = 25L * 1024L * 1024L,
            MaxContentBytesPerRun = 1024L * 1024L,
        };

        // Act
        var results = options.ValidateForSynchronization().ToArray();

        // Assert
        var result = Assert.Single(results);
        Assert.Contains("per-run content budget", result.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>A storage ceiling that cannot hold one message would leave nothing storable at all.</summary>
    [Fact]
    public void ValidateForSynchronization_StoredContentCeilingBelowTheMessageSizeLimit_IsRejected()
    {
        // Arrange
        var options = new MailSynchronizationOptions
        {
            MaxRawMimeBytes = 25L * 1024L * 1024L,
            MaxStoredContentBytes = 1024L * 1024L,
        };

        // Act
        var results = options.ValidateForSynchronization().ToArray();

        // Assert
        var result = Assert.Single(results);
        Assert.Contains("stored content ceiling", result.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>An owner's share that cannot hold one message would leave that owner with nothing storable.</summary>
    [Fact]
    public void ValidateForSynchronization_PerOwnerStoredContentCeilingBelowTheMessageSizeLimit_IsRejected()
    {
        // Arrange
        var options = new MailSynchronizationOptions
        {
            MaxRawMimeBytes = 25L * 1024L * 1024L,
            MaxStoredContentBytesPerOwner = 1024L * 1024L,
        };

        // Act
        var results = options.ValidateForSynchronization().ToArray();

        // Assert
        var result = Assert.Single(results);
        Assert.Contains("per-owner stored content ceiling", result.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>An owner's share is unset by default, which is what a deployment serving one owner wants.</summary>
    [Fact]
    public void ValidateForSynchronization_NoPerOwnerStoredContentCeilingConfigured_ReportsNoError()
    {
        // Arrange
        var options = new MailSynchronizationOptions { MaxStoredContentBytesPerOwner = null };

        // Act
        var results = options.ValidateForSynchronization().ToArray();

        // Assert
        Assert.Empty(results);
    }

    /// <summary>An in-flight budget smaller than one message is a wait for room that can never exist.</summary>
    [Fact]
    public void ValidateForSynchronization_InFlightContentBudgetBelowTheMessageSizeLimit_IsRejected()
    {
        // Arrange
        var options = new MailSynchronizationOptions
        {
            MaxRawMimeBytes = 25L * 1024L * 1024L,
            MaxInFlightRawMimeBytes = 1024L * 1024L,
        };

        // Act
        var results = options.ValidateForSynchronization().ToArray();

        // Assert
        var result = Assert.Single(results);
        Assert.Contains("in-flight content budget", result.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>No ceiling is the default, and it is a configuration rather than an omission to complain about.</summary>
    [Fact]
    public void ValidateForSynchronization_NoStoredContentCeilingConfigured_ReportsNoError()
    {
        // Arrange
        var options = new MailSynchronizationOptions { MaxStoredContentBytes = null };

        // Act
        var results = options.ValidateForSynchronization().ToArray();

        // Assert
        Assert.Empty(results);
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

    /// <summary>Every bound is enforced at run time, so a value outside its range has to fail startup rather than reach a scheduler.</summary>
    /// <remarks>
    /// The mutation attempt bound is here rather than beside the connection bounds because it is validated the same
    /// way, and because zero is the value worth catching: it would let a change be recorded and then never attempted,
    /// which is the one state the record exists to make impossible.
    /// </remarks>
    [Theory]
    [InlineData("MaxConcurrentAccounts", 0)]
    [InlineData("MaxConcurrentAccounts", 101)]
    [InlineData("MaxConcurrentFoldersPerAccount", 0)]
    [InlineData("MaxConcurrentFoldersPerAccount", 21)]
    [InlineData("MaxMutationAttempts", 0)]
    [InlineData("MaxMutationAttempts", 101)]
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
    public void ServedAccounts_ConfiguredAccounts_AreNormalizedDeduplicatedAndOrdered()
    {
        // Arrange
        var options = new MailSynchronizationOptions
        {
            Accounts = [CreateAccount("  secondary  "), CreateAccount("primary"), CreateAccount("secondary")],
        };

        // Act
        var servedAccountIds = ConfiguredMailAccounts.CatalogOver(options).ServedAccounts.Select(account => account.Id);

        // Assert
        Assert.Equal([MailAccountId.Create("primary"), MailAccountId.Create("secondary")], servedAccountIds);
    }

    /// <summary>Casing is part of an account identifier, so two spellings of one name are two accounts here.</summary>
    [Fact]
    public void ServedAccounts_AccountNamedInAnotherCase_IsNotTheConfiguredAccount()
    {
        // Arrange
        var options = new MailSynchronizationOptions { Accounts = [CreateAccount("primary")] };

        // Act
        var servedAccountIds = ConfiguredMailAccounts.CatalogOver(options).ServedAccounts.Select(account => account.Id);

        // Assert
        Assert.DoesNotContain(MailAccountId.Create("PRIMARY"), servedAccountIds);
    }

    /// <summary>Switching synchronization off stops runs from fetching mail; it does not hide the copy already stored.</summary>
    [Fact]
    public void ServedAccounts_SynchronizationDisabled_StillNamesTheConfiguredAccount()
    {
        // Arrange
        var options = new MailSynchronizationOptions { Enabled = false, Accounts = [CreateAccount("primary")] };

        // Act, Assert
        Assert.Equal(MailAccountId.Create("primary"), Assert.Single(ConfiguredMailAccounts.CatalogOver(options).ServedAccounts).Id);
    }

    [Fact]
    public void ServedAccounts_NoAccountsConfigured_ServesNothing()
    {
        // Arrange
        var options = new MailSynchronizationOptions();

        // Act, Assert
        Assert.Empty(ConfiguredMailAccounts.CatalogOver(options).ServedAccounts);
    }

    /// <summary>An account whose identifier never bound is not a served account, and reading the set does not fail on it.</summary>
    [Fact]
    public void ServedAccounts_AccountWithNoIdentifier_IsSkipped()
    {
        // Arrange
        var options = new MailSynchronizationOptions { Accounts = [CreateAccount("primary"), CreateAccount("   ")] };

        // Act, Assert
        Assert.Equal(MailAccountId.Create("primary"), Assert.Single(ConfiguredMailAccounts.CatalogOver(options).ServedAccounts).Id);
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
            Accounts =
            [
                new MailSynchronizationAccountOptions { AccountId = "primary", DisplayName = "The primary mailbox" },
            ],
        };

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.Contains(messages, message => message!.Contains("IMAP host is required", StringComparison.Ordinal));
        Assert.Contains(messages, message => message!.Contains("IMAP user name is required", StringComparison.Ordinal));
    }

    /// <summary>The password is no longer a configuration value, so its absence is a resolution failure rather than a binding rule.</summary>
    /// <summary>
    /// The rule reads the account's permitted mechanisms rather than the presence of a block, which is what lets a
    /// token-authenticated account configure no password while an account that will authenticate with one still has
    /// to. Before OAuth existed the absence was left entirely to secret resolution; it is checked here now because a
    /// missing block is no longer distinguishable from a deliberate one without reading the policy.
    /// </summary>
    [Fact]
    public void ValidateForSynchronization_PasswordMechanismWithoutASecretReference_ReportsTheMissingCredential()
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
        Assert.Contains(messages, message => message!.Contains("no password secret reference", StringComparison.Ordinal));
    }

    /// <summary>The block decides when personal data is destroyed, so a typo in it fails startup rather than binding away.</summary>
    [Fact]
    public void ValidateForSynchronization_AccountWhoseAuditTrailBlockIsNotABlock_ReportsIt()
    {
        // Arrange
        var account = CreateAccount("primary");
        account.AuditTrail = null!;
        var options = new MailSynchronizationOptions { Enabled = true, Accounts = [account] };

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.Contains(messages, message => message!.Contains("Account 'primary'", StringComparison.Ordinal)
            && message.Contains("audit trail configuration must be a block", StringComparison.Ordinal));
    }

    /// <summary>
    /// A window shorter than a run would erase entries before the run that would have shown them, and one beyond the
    /// ceiling stops being a retention anybody could justify. Both fail startup rather than being clamped.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    [InlineData(3650 * 24 + 1)]
    public void ValidateForSynchronization_AuditTrailRetentionOutsideTheAcceptedRange_ReportsIt(int retentionHours)
    {
        // Arrange
        var account = CreateAccount("primary");
        account.AuditTrail.Retention = TimeSpan.FromHours(retentionHours);
        var options = new MailSynchronizationOptions { Enabled = true, Accounts = [account] };

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.Contains(messages, message => message!.Contains("Account 'primary'", StringComparison.Ordinal)
            && message.Contains("audit trail retention must be between", StringComparison.Ordinal));
    }

    /// <summary>The control for the two above: the default block an account that configures nothing gets is accepted.</summary>
    [Fact]
    public void ValidateForSynchronization_AccountThatConfiguresNoAuditTrail_ReportsNoAuditTrailError()
    {
        // Arrange
        var options = new MailSynchronizationOptions { Enabled = true, Accounts = [CreateAccount("primary")] };

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.DoesNotContain(messages, message => message!.Contains("audit trail", StringComparison.Ordinal));
    }

    /// <summary>The record of what a question read is a second operator decision, so its window is checked as its own.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    [InlineData(3650 * 24 + 1)]
    public void ValidateForSynchronization_AnsweringAuditTrailRetentionOutsideTheAcceptedRange_ReportsIt(
        int retentionHours)
    {
        // Arrange
        var account = CreateAccount("primary");
        account.AnsweringAuditTrail.Retention = TimeSpan.FromHours(retentionHours);
        var options = new MailSynchronizationOptions { Enabled = true, Accounts = [account] };

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.Contains(messages, message => message!.Contains("Account 'primary'", StringComparison.Ordinal)
            && message.Contains("answering audit trail retention must be between", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateForSynchronization_AccountWithNoAnsweringAuditTrailBlock_ReportsIt()
    {
        // Arrange
        var account = CreateAccount("primary");
        account.AnsweringAuditTrail = null!;
        var options = new MailSynchronizationOptions { Enabled = true, Accounts = [account] };

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.Contains(messages, message => message!.Contains("Account 'primary'", StringComparison.Ordinal)
            && message.Contains("answering audit trail configuration must be a block", StringComparison.Ordinal));
    }

    /// <summary>A missing block would read as an account permitting nothing, which refuses rules while naming the wrong cause.</summary>
    [Fact]
    public void ValidateForSynchronization_AccountWithNoRuleActionBlock_ReportsIt()
    {
        // Arrange
        var account = CreateAccount("primary");
        account.RuleActions = null!;
        var options = new MailSynchronizationOptions { Enabled = true, Accounts = [account] };

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.Contains(messages, message => message!.Contains("Account 'primary'", StringComparison.Ordinal)
            && message.Contains("rule action permissions must be a block", StringComparison.Ordinal));
    }

    /// <summary>Off by default and separate from the mutation trail, which is what the two blocks together have to mean.</summary>
    [Fact]
    public void GetAnsweringAuditSettings_AnAccountThatTurnedOnlyTheMutationTrailOn_KeepsTheAnsweringRecordOff()
    {
        // Arrange
        var account = CreateAccount("primary");
        account.AuditTrail.Enabled = true;
        var options = new MailSynchronizationOptions { Enabled = true, Accounts = [account] };

        // Act
        var settings = options.Readers.AnsweringAuditSettings.GetAnsweringAuditSettings(MailAccountId.Create("primary"));

        // Assert
        Assert.False(settings.IsEnabled);
        Assert.Equal(TimeSpan.FromDays(30), settings.Retention);
    }

    /// <summary>An account this deployment does not configure names no window, and a missing window destroys nothing.</summary>
    [Fact]
    public void GetAnsweringAuditSettings_AnAccountThisDeploymentDoesNotConfigure_IsDisabled()
    {
        // Arrange
        var options = new MailSynchronizationOptions { Enabled = true, Accounts = [CreateAccount("primary")] };

        // Act
        var settings = options.Readers.AnsweringAuditSettings.GetAnsweringAuditSettings(MailAccountId.Create("somebody-elses"));

        // Assert
        Assert.Equal(MailAnsweringAuditSettings.Disabled, settings);
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
        account.UserName = "mailfathom@example.test";
        account.TransportSecurity.ConnectionSecurity = MailConnectionSecurity.None;
        var options = new MailSynchronizationOptions { Enabled = true, Accounts = [account] };

        // Act
        var messages = string.Join(' ', options.ValidateForSynchronization().Select(result => result.ErrorMessage));

        // Assert
        Assert.DoesNotContain("mailfathom@example.test", messages, StringComparison.Ordinal);
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

    /// <summary>A role is how something asks for this account's junk folder, so two folders claiming one would give that question two answers.</summary>
    [Fact]
    public void ValidateForSynchronization_TwoFoldersOfOneAccountNamingOneRole_ReportsBothAliasesAndTheRole()
    {
        // Arrange
        var account = CreateAccount("primary");
        account.Folders =
        [
            new MailFolderMappingOptions { Alias = "spam", RemotePath = "INBOX.Spam", SpecialUse = "Junk" },
            new MailFolderMappingOptions { Alias = "junk", SpecialUse = "junk" },
        ];
        var options = new MailSynchronizationOptions { Accounts = [account] };

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        var collision = Assert.Single(messages, message => message!.Contains("at most one folder per role", StringComparison.Ordinal));
        Assert.Contains("'spam'", collision, StringComparison.Ordinal);
        Assert.Contains("'junk'", collision, StringComparison.Ordinal);
        Assert.Contains("'Junk'", collision, StringComparison.Ordinal);
    }

    /// <summary>The rule is one folder per role, not one role per folder, so an account naming several different roles binds.</summary>
    [Fact]
    public void ValidateForSynchronization_FoldersNamingDifferentRolesOrNoRoleAtAll_ReportsNothing()
    {
        // Arrange
        var account = CreateAccount("primary");
        account.Folders =
        [
            new MailFolderMappingOptions { Alias = "inbox", SpecialUse = "Inbox" },
            new MailFolderMappingOptions { Alias = "spam", RemotePath = "INBOX.Spam", SpecialUse = "Junk" },
            new MailFolderMappingOptions { Alias = "projects", RemotePath = "INBOX.Projects" },
            new MailFolderMappingOptions { Alias = "notes", RemotePath = "INBOX.Notes" },
        ];
        var options = new MailSynchronizationOptions { Accounts = [account] };

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.DoesNotContain(messages, message => message!.Contains("at most one folder per role", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, message => message!.Contains("Folder alias", StringComparison.Ordinal));
    }

    /// <summary>An alias spelled like a role would be a folder nothing could name, because every caller writing it would reach the role.</summary>
    [Theory]
    [InlineData("role:Junk")]
    [InlineData("ROLE:archive")]
    [InlineData("  role:whatever  ")]
    public void ValidateForSynchronization_AnAliasBeginningWithTheRoleScheme_IsRefused(string alias)
    {
        // Arrange
        var account = CreateAccount("primary");
        account.Folders = [new MailFolderMappingOptions { Alias = alias, RemotePath = "INBOX.Spam" }];
        var options = new MailSynchronizationOptions { Accounts = [account] };

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.Contains(messages, message => message!.Contains("role:", StringComparison.Ordinal)
            && message.Contains("by the role it plays", StringComparison.Ordinal));
    }

    /// <summary>A comma-separated list is combined by bitwise OR, so accepting one would bind two roles an operator wrote onto a third.</summary>
    [Theory]
    [InlineData("Archive,Drafts")]
    [InlineData("4")]
    public void ValidateForSynchronization_ARoleSpelledAsSomethingOtherThanItsName_IsRefused(string specialUse)
    {
        // Arrange
        var account = CreateAccount("primary");
        account.Folders = [new MailFolderMappingOptions { Alias = "spam", SpecialUse = specialUse }];
        var options = new MailSynchronizationOptions { Accounts = [account] };

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.Contains(messages, message => message!.Contains("which is not supported", StringComparison.Ordinal));
    }

    /// <summary>Two accounts are two mailboxes, so each one having its own junk folder is the ordinary case.</summary>
    [Fact]
    public void ValidateForSynchronization_TwoAccountsNamingTheSameRole_ReportsNothing()
    {
        // Arrange
        var first = CreateAccount("primary");
        first.Folders = [new MailFolderMappingOptions { Alias = "spam", RemotePath = "INBOX.Spam", SpecialUse = "Junk" }];
        var second = CreateAccount("secondary");
        second.Folders = [new MailFolderMappingOptions { Alias = "junk", SpecialUse = "Junk" }];
        var options = new MailSynchronizationOptions { Accounts = [first, second] };

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.DoesNotContain(messages, message => message!.Contains("at most one folder per role", StringComparison.Ordinal));
    }

    /// <summary>A folder labelled with a role names the path it would be created at, so the objection to creating a role mapping does not reach it.</summary>
    [Fact]
    public void ValidateForSynchronization_CreationAskedBesideAPathAndARole_ReportsNothing()
    {
        // Arrange
        var account = CreateAccount("primary");
        account.Folders =
        [
            new MailFolderMappingOptions
            {
                Alias = "spam",
                RemotePath = "INBOX.Spam",
                SpecialUse = "Junk",
                CreateIfMissing = true,
            },
        ];
        var options = new MailSynchronizationOptions { Accounts = [account] };

        // Act
        var messages = options.ValidateForSynchronization().Select(result => result.ErrorMessage).ToArray();

        // Assert
        Assert.DoesNotContain(messages, message => message!.Contains("Folder alias 'spam'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "NotARole")]
    public void ValidateForSynchronization_FolderNamingNeitherTargetOrAnUnknownRole_ReportsIt(string? remotePath, string? specialUse)
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
        var policy = options.Readers.TransportSecurityPolicies.GetPolicy(MailAccountId.Create("primary"));

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
        var window = options.Readers.SynchronizationWindows.GetWindow(MailAccountId.Create("primary"));

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
        var window = options.Readers.SynchronizationWindows.GetWindow(MailAccountId.Create("primary"));

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
        var dispositions = ConfiguredMailAccounts.CatalogOver(options).ServedAccounts
            .Select(account => options.Readers.RemotelyDeletedEmailDispositions.GetDisposition(account.Id))
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
        var disposition = options.Readers.RemotelyDeletedEmailDispositions.GetDisposition(MailAccountId.Create("primary"));

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
                ["MailSynchronization:Accounts:0:DisplayName"] = "The primary mailbox",
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
                ["MailSynchronization:Accounts:0:DisplayName"] = "The primary mailbox",
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
                ["MailSynchronization:Accounts:0:DisplayName"] = "The primary mailbox",
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

    /// <summary>The two dispositions are independent settings, so one account can follow its server and still keep what it deletes.</summary>
    /// <remarks>
    /// This is the confusion the second setting exists to prevent. An account that erases what its server loses would
    /// otherwise erase what MailFathom itself was told to delete, which is precisely where the owner is most likely to
    /// have meant the opposite: deleting on the server frees quota, and the local archive is the reason to do it.
    /// </remarks>
    [Fact]
    public void GetDisposition_AccountErasingWhatItsServerLoses_KeepsWhatMailFathomDeletesItself()
    {
        // Arrange
        var account = CreateAccount("primary");
        account.RemotelyDeletedEmailDisposition = RemotelyDeletedEmailDisposition.EraseLocalCopy;
        var options = new MailSynchronizationOptions { Accounts = [account] };
        var accountId = MailAccountId.Create("primary");

        // Act
        var remoteDisposition = options.Readers.RemotelyDeletedEmailDispositions.GetDisposition(accountId);
        var authoredDisposition = options.Readers.AuthoredDeleteEmailDispositions.GetAuthoredDeleteDisposition(accountId);

        // Assert
        Assert.Equal(RemotelyDeletedEmailDisposition.EraseLocalCopy, remoteDisposition);
        Assert.Equal(AuthoredDeleteEmailDisposition.RetainLocalCopy, authoredDisposition);
    }

    /// <summary>The two accounts of one deployment answer differently here too, which is why this setting is per account as well.</summary>
    [Fact]
    public void GetAuthoredDeleteDisposition_AccountsConfiguringDifferentDispositions_AnswersPerAccount()
    {
        // Arrange
        var forgetful = CreateAccount("forgetful");
        forgetful.AuthoredDeleteEmailDisposition = AuthoredDeleteEmailDisposition.EraseLocalCopy;
        var options = new MailSynchronizationOptions { Accounts = [forgetful, CreateAccount("archive")] };

        // Act
        var dispositions = ConfiguredMailAccounts.CatalogOver(options).ServedAccounts
            .Select(account => options.Readers.AuthoredDeleteEmailDispositions.GetAuthoredDeleteDisposition(account.Id))
            .ToArray();

        // Assert
        Assert.Equal(
            [AuthoredDeleteEmailDisposition.RetainLocalCopy, AuthoredDeleteEmailDisposition.EraseLocalCopy],
            dispositions);
    }

    [Fact]
    public void Bind_AuthoredDeleteEmailDisposition_ReadsItByName()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MailSynchronization:Accounts:0:AccountId"] = "primary",
                ["MailSynchronization:Accounts:0:DisplayName"] = "The primary mailbox",
                ["MailSynchronization:Accounts:0:AuthoredDeleteEmailDisposition"] = "RetainTombstone",
            })
            .Build();

        // Act
        var options = configuration.GetSection("MailSynchronization").Get<MailSynchronizationOptions>()!;

        // Assert
        Assert.Equal(
            AuthoredDeleteEmailDisposition.RetainTombstone,
            Assert.Single(options.Accounts).AuthoredDeleteEmailDisposition);
    }

    /// <summary>
    /// A bare number binds onto this enum as readily as onto the one above, and the value decides what a delete leaves
    /// behind. An account whose value names nothing would have every delete it authored refused where the record is
    /// built, one deletion at a time, rather than at the startup that accepted the typo.
    /// </summary>
    [Fact]
    public void ValidateForSynchronization_AuthoredDeleteDispositionNumberNoMemberCarries_IsRejected()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MailSynchronization:Accounts:0:AccountId"] = "primary",
                ["MailSynchronization:Accounts:0:DisplayName"] = "The primary mailbox",
                ["MailSynchronization:Accounts:0:AuthoredDeleteEmailDisposition"] = "3",
            })
            .Build();
        var options = configuration.GetSection("MailSynchronization").Get<MailSynchronizationOptions>()!;

        // Act
        var results = options.ValidateForSynchronization().ToArray();

        // Assert
        var result = Assert.Single(results);
        Assert.Contains("RetainLocalCopy", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("EraseLocalCopy", result.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>Push holds a connection open per folder, so an account that says nothing keeps the schedule it already had.</summary>
    [Fact]
    public void Mode_AccountConfiguringNone_Polls()
    {
        // Arrange
        var options = new MailSynchronizationOptions { Accounts = [CreateAccount("primary")] };

        // Act
        var mode = Assert.Single(options.Accounts).Mode;

        // Assert
        Assert.Equal(MailSynchronizationMode.Polling, mode);
    }

    [Fact]
    public void Bind_Mode_ReadsItByName()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MailSynchronization:Accounts:0:AccountId"] = "primary",
                ["MailSynchronization:Accounts:0:DisplayName"] = "The primary mailbox",
                ["MailSynchronization:Accounts:0:Mode"] = "Push",
            })
            .Build();

        // Act
        var options = configuration.GetSection("MailSynchronization").Get<MailSynchronizationOptions>()!;

        // Assert
        Assert.Equal(MailSynchronizationMode.Push, Assert.Single(options.Accounts).Mode);
    }

    /// <summary>
    /// A bare number binds onto an enum whether or not a member carries it. Left unvalidated, an undefined value would
    /// be read as "not Push", so an operator who asked for push and mistyped it would silently get polling with nothing
    /// reporting the difference.
    /// </summary>
    [Fact]
    public void ValidateForSynchronization_ModeNumberNoMemberCarries_IsRejected()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MailSynchronization:Accounts:0:AccountId"] = "primary",
                ["MailSynchronization:Accounts:0:DisplayName"] = "The primary mailbox",
                ["MailSynchronization:Accounts:0:Mode"] = "3",
            })
            .Build();
        var options = configuration.GetSection("MailSynchronization").Get<MailSynchronizationOptions>()!;

        // Act
        var results = options.ValidateForSynchronization().ToArray();

        // Assert
        var result = Assert.Single(results);
        Assert.Contains("Polling", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("Push", result.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// The write connection is the third kind an account can hold, and the setting bounds how long it keeps its slot
    /// after the last change it carried. Zero is refused because a connection closed the instant it is released is the
    /// per-mutation connection the pool exists to avoid, and the ceiling keeps an idle account from holding a slot for
    /// most of an hour.
    /// </summary>
    [Theory]
    [InlineData("00:00:00")]
    [InlineData("00:00:04")]
    [InlineData("00:31:00")]
    public void Bind_WriteConnectionIdlePeriodOutsideItsRange_FailsDataAnnotationValidation(string configuredValue)
    {
        // Arrange
        var options = new MailSynchronizationOptions();
        new ConfigurationBuilder()
            .AddInMemoryCollection([
                new KeyValuePair<string, string?>("WriteConnectionIdlePeriod", configuredValue),
            ])
            .Build()
            .Bind(options);

        // Act
        var valid = Validator.TryValidateObject(options, new ValidationContext(options), null, validateAllProperties: true);

        // Assert
        Assert.False(valid);
    }

    [Fact]
    public void WriteConnectionIdlePeriod_NothingIsConfigured_KeepsTheConnectionLongEnoughToBatchARunOfChanges()
    {
        // Arrange
        var options = new MailSynchronizationOptions();

        // Act, Assert
        Assert.Equal(TimeSpan.FromMinutes(2), options.WriteConnectionIdlePeriod);
    }

    /// <summary>RFC 2177 requires IDLE to be re-issued at least every 29 minutes, so the defaults have to sit under it.</summary>
    [Fact]
    public void PushDefaults_NothingIsConfigured_RenewWellInsideTheProtocolMandate()
    {
        // Arrange
        var options = new MailSynchronizationOptions();

        // Act, Assert
        Assert.True(options.PushRenewalInterval < TimeSpan.FromMinutes(29));
        Assert.Equal(3, options.MaxConsecutivePushFailures);
        Assert.Equal(TimeSpan.FromMinutes(15), options.PushDegradationPeriod);

        // A subscription names folders explicitly and a server may refuse an oversized one as a whole, so the default
        // has to be a list an ordinary server accepts rather than however many folders an account happens to configure.
        Assert.Equal(20, options.MaxSubscribedFolders);
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
        account.UserName = "mailfathom@example.test";
        var options = new MailSynchronizationOptions { Accounts = [account] };

        // Act
        using var settings = (await options.ResolveSettingsAsync(
            "primary",
            new PlaintextOnlySecretReferenceResolver(),
            CreateTrustAnchorLoader(),
            CancellationToken.None)).Material;

        // Assert
        Assert.Equal("dev-password", settings.Password!.RevealAsString());
    }

    [Fact]
    public async Task ResolveSettingsAsync_ConfiguredAccount_CarriesTheEndpointSettingsUnchanged()
    {
        // Arrange
        var account = CreateAccount("primary", secretReference: "plaintext:dev-password");
        account.Host = " imap.example.test ";
        account.Port = 1993;
        account.UserName = "mailfathom@example.test";
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
            Assert.Equal("mailfathom@example.test", settings.UserName);
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
                ["MailSynchronization:Accounts:0:DisplayName"] = "The primary mailbox",
                ["MailSynchronization:Accounts:0:Host"] = "imap.example.test",
                ["MailSynchronization:Accounts:0:UserName"] = "mailfathom@example.test",
                ["MailSynchronization:Accounts:0:Secrets:Password:SecretReference"] = "systemd-credential:imap-primary-password",
            })
            .Build();

        // Act
        var options = configuration.GetSection("MailSynchronization").Get<MailSynchronizationOptions>()!;

        // Assert
        var account = Assert.Single(options.Accounts);
        Assert.Equal("systemd-credential:imap-primary-password", account.Secrets.Password!.SecretReference);
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
                ["MailSynchronization:Accounts:0:DisplayName"] = "The primary mailbox",
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
                ["MailSynchronization:Accounts:0:DisplayName"] = "The primary mailbox",
                ["MailSynchronization:Accounts:0:EarliestEmailReceivedDate"] = "last January",
            })
            .Build();
        var options = new MailSynchronizationOptions();

        // Act, Assert
        Assert.Throws<InvalidOperationException>(() => configuration
            .GetSection("MailSynchronization")
            .Bind(options, binderOptions => binderOptions.ErrorOnUnknownConfiguration = true));
    }

    /// <summary>Every key a convergence pass is bounded by reaches the bound the application takes.</summary>
    [Fact]
    public void ToConvergenceOptions_ConfiguredSection_CarriesEveryKeyThePassIsBoundedBy()
    {
        // Arrange
        var options = new MailSynchronizationOptions
        {
            MaxMutationsPerConvergencePass = 17,
            UnknownMutationOutcomeGrace = TimeSpan.FromHours(3),
        };

        // Act
        var bounds = options.ToConvergenceOptions();

        // Assert
        Assert.Equal(17, bounds.MaxMutationsPerPass);
        Assert.Equal(TimeSpan.FromHours(3), bounds.UnknownOutcomeGrace);
    }

    /// <summary>Every key one run stops at reaches the bound the application takes, and none is read off a neighbour.</summary>
    [Fact]
    public void ToSynchronizationOptions_ConfiguredSection_CarriesEveryKeyTheRunStopsAt()
    {
        // Arrange
        var options = new MailSynchronizationOptions
        {
            MaxMetadataBatchSize = 11,
            MaxRawMimeBytes = 12L,
            MaxMetadataBatchesPerRun = 13,
            MaxReconciledEmailsPerRun = 14,
            MaxContentBytesPerRun = 15L,
        };

        // Act
        var bounds = options.ToSynchronizationOptions();

        // Assert
        Assert.Equal(11, bounds.MaxMetadataBatchSize);
        Assert.Equal(12L, bounds.MaxRawMimeBytes);
        Assert.Equal(13, bounds.MaxMetadataBatchesPerRun);
        Assert.Equal(14, bounds.MaxReconciledEmailsPerRun);
        Assert.Equal(15L, bounds.MaxContentBytesPerRun);
    }

    /// <summary>Every limit a MIME walk is performed under reaches the parse, including whether it verifies for itself.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToMimeExtractionOptions_ConfiguredSection_CarriesEveryLimitTheParseIsPerformedUnder(bool verifyDkimLocally)
    {
        // Arrange
        var options = new MailSynchronizationOptions
        {
            MaxMimePartCount = 21,
            MaxMimeNestingDepth = 22,
            MaxExtractedTextCharacters = 23,
            VerifyDkimLocally = verifyDkimLocally,
        };

        // Act
        var limits = options.ToMimeExtractionOptions();

        // Assert
        Assert.Equal(21, limits.MaxPartCount);
        Assert.Equal(22, limits.MaxNestingDepth);
        Assert.Equal(23, limits.MaxExtractedTextCharacters);
        Assert.Equal(verifyDkimLocally, limits.VerifyDkimLocally);
    }

    private static TrustAnchorLoader CreateTrustAnchorLoader() =>
        new(new PlaintextOnlySecretReferenceResolver());

    private static MailSynchronizationAccountOptions CreateAccount(
        string accountId,
        string secretReference = "systemd-credential:imap-primary-password") => new()
        {
            AccountId = accountId,
            DisplayName = $"The {accountId.Trim()} mailbox",
            Host = "imap.example.test",
            UserName = "mailfathom@example.test",
            Secrets = new MailAccountSecretOptions
            {
                Password = new ConfiguredSecret { SecretReference = secretReference },
            },
        };
}
