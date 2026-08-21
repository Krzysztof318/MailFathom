// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>Covers what configuration says one account collects contacts under, and what an unusable entry costs.</summary>
public sealed class ContactCollectionConfigurationTests
{
    /// <summary>Collection derives personal data about people who never dealt with MailFathom, so nobody gets it unasked.</summary>
    [Fact]
    public void SettingsFor_AnAccountThatConfiguredNothing_CollectsNobody()
    {
        // Act
        var settings = OptionsFor(AccountAt("work", "owner@work.example")).Readers.ContactCollection.GetContactCollectionSettings(MailAccountId.Create("work"));

        // Assert
        Assert.False(settings.IsEnabled);
    }

    /// <summary>The two numbers bound who is written down and how fast, so both reach collection as configured.</summary>
    [Fact]
    public void SettingsFor_AnAccountThatSwitchedCollectionOn_CarriesTheBoundsItStated()
    {
        // Arrange
        var account = AccountAt("work", "owner@work.example");
        account.ContactCollection = new ContactCollectionOptions
        {
            Enabled = true,
            MinimumMessagesFromSender = 4,
            MaxContactsPerRun = 25,
        };

        // Act
        var settings = OptionsFor(account).Readers.ContactCollection.GetContactCollectionSettings(MailAccountId.Create("work"));

        // Assert
        Assert.True(settings.IsEnabled);
        Assert.Equal(4, settings.MinimumMessagesFromSender);
        Assert.Equal(25, settings.MaxContactsPerRun);
    }

    /// <summary>A deployment reading a work mailbox and a personal one decides separately for each.</summary>
    [Fact]
    public void SettingsFor_CollectionSwitchedOnForOneAccount_LeavesTheOtherCollectingNobody()
    {
        // Arrange
        var work = AccountAt("work", "owner@work.example");
        work.ContactCollection = new ContactCollectionOptions { Enabled = true };
        var options = OptionsFor(work, AccountAt("personal", "owner@personal.example"));

        // Act
        var onWork = options.Readers.ContactCollection.GetContactCollectionSettings(MailAccountId.Create("work"));
        var onPersonal = options.Readers.ContactCollection.GetContactCollectionSettings(MailAccountId.Create("personal"));

        // Assert
        Assert.True(onWork.IsEnabled);
        Assert.False(onPersonal.IsEnabled);
    }

    /// <summary>A run may outlive a reload that removed its account, and collecting nobody is the honest answer.</summary>
    [Fact]
    public void SettingsFor_AnAccountThisSnapshotNoLongerNames_CollectsNobody()
    {
        // Arrange
        var work = AccountAt("work", "owner@work.example");
        work.ContactCollection = new ContactCollectionOptions { Enabled = true };

        // Act
        var settings = OptionsFor(work).Readers.ContactCollection.GetContactCollectionSettings(MailAccountId.Create("removed"));

        // Assert
        Assert.False(settings.IsEnabled);
        Assert.Equal(0, settings.MaxContactsPerRun);
    }

    /// <summary>An owner writing from one of their mailboxes to another is not a correspondent of themselves.</summary>
    [Fact]
    public void SettingsFor_AnAddressAnotherConfiguredAccountReads_IsNotCollectable()
    {
        // Arrange
        var work = AccountAt("work", "owner@work.example");
        work.ContactCollection = new ContactCollectionOptions { Enabled = true };
        var options = OptionsFor(work, AccountAt("personal", "owner@personal.example"));

        // Act
        var policy = options.Readers.ContactCollection.GetContactCollectionSettings(MailAccountId.Create("work")).Policy;

        // Assert
        Assert.False(policy.Admits(AddressOf("owner@personal.example")));
        Assert.False(policy.Admits(AddressOf("owner@work.example")));
        Assert.True(policy.Admits(AddressOf("anna@partner.example")));
    }

    /// <summary>Reaching under a domain is asked for per entry, so an entry that did not ask does not reach.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void SettingsFor_ASubdomainOfAnExcludedDomain_FollowsWhatTheEntryAskedFor(
        bool includeSubdomains,
        bool expectedAdmitted)
    {
        // Arrange
        var account = AccountAt("work", "owner@work.example");
        account.ContactCollection = new ContactCollectionOptions
        {
            Enabled = true,
            Exclusions =
            [
                new ContactCollectionExclusionOptions
                {
                    Domain = "partner.example",
                    IncludeSubdomains = includeSubdomains,
                },
            ],
        };

        // Act
        var policy = OptionsFor(account).Readers.ContactCollection.GetContactCollectionSettings(MailAccountId.Create("work")).Policy;

        // Assert
        Assert.Equal(expectedAdmitted, policy.Admits(AddressOf("anna@mail.partner.example")));
        Assert.False(policy.Admits(AddressOf("anna@partner.example")));
    }

    /// <summary>A pattern is written over the whole address, which is what an owner excluding one family of names needs.</summary>
    [Fact]
    public void SettingsFor_AnExcludedAddressPattern_KeepsOutOnlyTheAddressesItMatches()
    {
        // Arrange
        var account = AccountAt("work", "owner@work.example");
        account.ContactCollection = new ContactCollectionOptions
        {
            Enabled = true,
            Exclusions = [new ContactCollectionExclusionOptions { AddressPattern = "BILLING-*@PARTNER.EXAMPLE" }],
        };

        // Act
        var policy = OptionsFor(account).Readers.ContactCollection.GetContactCollectionSettings(MailAccountId.Create("work")).Policy;

        // Assert
        Assert.False(policy.Admits(AddressOf("billing-eu@partner.example")));
        Assert.True(policy.Admits(AddressOf("anna@partner.example")));
    }

    /// <summary>Startup refuses an unusable entry, so an arriving message must not throw over the same configuration.</summary>
    [Fact]
    public void SettingsFor_AnEntryNobodyCouldRead_IsSkippedRatherThanRaisedOver()
    {
        // Arrange
        var account = AccountAt("work", "owner@work.example");
        account.ContactCollection = new ContactCollectionOptions
        {
            Enabled = true,
            Exclusions =
            [
                new ContactCollectionExclusionOptions { Domain = "partner.example", AddressPattern = "*@partner.example" },
                new ContactCollectionExclusionOptions { Domain = "excluded.example" },
            ],
        };

        // Act
        var policy = OptionsFor(account).Readers.ContactCollection.GetContactCollectionSettings(MailAccountId.Create("work")).Policy;

        // Assert
        Assert.True(policy.Admits(AddressOf("anna@partner.example")));
        Assert.False(policy.Admits(AddressOf("anna@excluded.example")));
    }

    /// <summary>A threshold outside the bounds is a number nobody can act on, so startup says so rather than running under it.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void ValidateForSynchronization_AThresholdOutsideTheBounds_IsRefused(int threshold)
    {
        // Arrange
        var account = AccountAt("work", "owner@work.example");
        account.ContactCollection = new ContactCollectionOptions { MinimumMessagesFromSender = threshold };

        // Act
        var messages = MessagesFrom(account);

        // Assert
        Assert.Contains(messages, message => message.Contains("messages from a sender", StringComparison.Ordinal));
    }

    /// <summary>The run bound is what paces a first synchronization, so a value it could not pace with is refused.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(1001)]
    public void ValidateForSynchronization_ARunBoundOutsideTheBounds_IsRefused(int perRun)
    {
        // Arrange
        var account = AccountAt("work", "owner@work.example");
        account.ContactCollection = new ContactCollectionOptions { MaxContactsPerRun = perRun };

        // Act
        var messages = MessagesFrom(account);

        // Assert
        Assert.Contains(messages, message => message.Contains("contacts per run", StringComparison.Ordinal));
    }

    /// <summary>An exclusion that excludes nobody fails in the unsafe direction, so it is refused where it is written.</summary>
    [Theory]
    [InlineData(null, null, false)]
    [InlineData("partner.example", "*@partner.example", false)]
    [InlineData(null, "*@partner.example", true)]
    [InlineData(null, "*", false)]
    [InlineData("   ", null, false)]
    public void ValidateForSynchronization_AnEntryNobodyCouldRead_IsRefusedWithoutNamingItsValue(
        string? domain,
        string? addressPattern,
        bool includeSubdomains)
    {
        // Arrange
        var account = AccountAt("work", "owner@work.example");
        account.ContactCollection = new ContactCollectionOptions
        {
            Exclusions =
            [
                new ContactCollectionExclusionOptions { Domain = "usable.example" },
                new ContactCollectionExclusionOptions
                {
                    Domain = domain,
                    AddressPattern = addressPattern,
                    IncludeSubdomains = includeSubdomains,
                },
            ],
        };

        // Act
        var refusal = Assert.Single(
            MessagesFrom(account),
            message => message.Contains("contact collection exclusion", StringComparison.Ordinal));

        // Assert
        Assert.Contains("exclusion 1", refusal, StringComparison.Ordinal);
        Assert.Contains("work", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("partner.example", refusal, StringComparison.Ordinal);
    }

    /// <summary>A usable block is not a mistake, so it produces no startup refusal.</summary>
    [Fact]
    public void ValidateForSynchronization_AUsableBlock_IsAccepted()
    {
        // Arrange
        var account = AccountAt("work", "owner@work.example");
        account.ContactCollection = new ContactCollectionOptions
        {
            Enabled = true,
            MinimumMessagesFromSender = 3,
            MaxContactsPerRun = 100,
            Exclusions =
            [
                new ContactCollectionExclusionOptions { Domain = "partner.example", IncludeSubdomains = true },
                new ContactCollectionExclusionOptions { AddressPattern = "billing-*@partner.example" },
            ],
        };

        // Act
        var messages = MessagesFrom(account);

        // Assert
        Assert.DoesNotContain(messages, message => message.Contains("contact collection", StringComparison.Ordinal));
    }

    private static string[] MessagesFrom(MailSynchronizationAccountOptions account) =>
    [
        .. OptionsFor(account)
            .ValidateForSynchronization()
            .Select(result => result.ErrorMessage)
            .OfType<string>(),
    ];

    private static EmailAddress AddressOf(string written)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, written, out var address));

        return address;
    }

    private static MailSynchronizationOptions OptionsFor(params MailSynchronizationAccountOptions[] accounts) => new()
    {
        Accounts = [.. accounts],
    };

    private static MailSynchronizationAccountOptions AccountAt(string accountId, string userName) => new()
    {
        AccountId = accountId,
        DisplayName = $"Account {accountId}",
        Host = "imap.example.test",
        UserName = userName,
        Secrets = new MailAccountSecretOptions
        {
            Password = new ConfiguredSecret { SecretReference = $"systemd-credential:imap-{accountId}-password" },
        },
    };
}
