// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Rules;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Rules;

/// <summary>Covers the two readings of one question: which accounts a rule's scope is allowed to name.</summary>
/// <remarks>
/// Composition reads keys and a reload reads a bound snapshot, so the two have to agree. A rule set startup accepted and
/// the first reload refused would be the failure, and it would arrive on an edit that changed nothing about the rules.
/// </remarks>
public sealed class DeclaredMailAccountsTests
{
    [Fact]
    public void ReadFrom_Configuration_NamesEveryDeclaredAccountInDeclaredOrder()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MailSynchronization:Accounts:0:AccountId"] = "primary",
                ["MailSynchronization:Accounts:1:AccountId"] = "work",
            })
            .Build();

        // Act
        var accounts = DeclaredMailAccounts.ReadFrom(configuration);

        // Assert
        Assert.Equal(["primary", "work"], accounts);
    }

    /// <summary>A blank identifier is the synchronization section's own defect, so it is dropped rather than reported here.</summary>
    [Fact]
    public void ReadFrom_ConfigurationWithABlankIdentifier_LeavesItOut()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MailSynchronization:Accounts:0:AccountId"] = "  primary  ",
                ["MailSynchronization:Accounts:1:AccountId"] = "   ",
            })
            .Build();

        // Act
        var accounts = DeclaredMailAccounts.ReadFrom(configuration);

        // Assert
        Assert.Equal(["primary"], accounts);
    }

    [Fact]
    public void ReadFrom_ConfigurationWithNoAccounts_NamesNothing()
    {
        // Arrange
        var configuration = new ConfigurationBuilder().Build();

        // Act
        var accounts = DeclaredMailAccounts.ReadFrom(configuration);

        // Assert
        Assert.Empty(accounts);
    }

    /// <summary>The bound reading is the one a reload uses, and it has to answer exactly as the key reading does.</summary>
    [Fact]
    public void ReadFrom_BoundSettings_AnswersAsTheConfigurationReadingDoes()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MailSynchronization:Accounts:0:AccountId"] = "  primary  ",
                ["MailSynchronization:Accounts:1:AccountId"] = "work",
            })
            .Build();
        var settings = new MailSynchronizationOptions
        {
            Accounts =
            [
                new MailSynchronizationAccountOptions { AccountId = "  primary  " },
                new MailSynchronizationAccountOptions { AccountId = "work" },
            ],
        };

        // Act
        var fromConfiguration = DeclaredMailAccounts.ReadFrom(configuration);
        var fromSettings = DeclaredMailAccounts.ReadFrom(settings);

        // Assert
        Assert.Equal(fromConfiguration, fromSettings);
        Assert.Equal(["primary", "work"], fromSettings);
    }
}
