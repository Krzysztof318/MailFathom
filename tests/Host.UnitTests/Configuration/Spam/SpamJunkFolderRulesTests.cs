// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Actions;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Rules;
using MailFathom.Host.Configuration.Spam;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Spam;

/// <summary>Covers the one claim the classification section makes about the accounts in another section.</summary>
public sealed class SpamJunkFolderRulesTests
{
    [Fact]
    public void FindDestinationErrors_AnAccountMappingNoJunkFolder_IsRefusedNamingTheAccount()
    {
        // Arrange
        var options = Filing(junkFolder: null);

        // Act
        var error = Assert.Single(SpamJunkFolderRules.FindDestinationErrors(options, [AccountMapping()]));

        // Assert
        Assert.Contains("personal", error.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>An unmirrored junk folder is the recommended destination, so mapping it is enough.</summary>
    [Fact]
    public void FindDestinationErrors_AnAccountMappingAnUnmirroredJunkFolder_IsAccepted()
    {
        // Arrange
        var junk = new DeclaredMailFolder(MailFolderAlias.Create("JUNK"), MailFolderSpecialUse.Junk);
        var account = AccountMapping(mapped: [junk]);

        // Act
        var errors = SpamJunkFolderRules.FindDestinationErrors(Filing(junkFolder: null), [account]);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void FindDestinationErrors_OneOfTwoAccountsWithoutTheFolder_ReportsOnlyThatAccount()
    {
        // Arrange
        var junk = new DeclaredMailFolder(MailFolderAlias.Create("JUNK"), MailFolderSpecialUse.Junk);
        var mapped = AccountMapping("work", [junk]);

        // Act
        var error = Assert.Single(
            SpamJunkFolderRules.FindDestinationErrors(Filing(junkFolder: null), [mapped, AccountMapping()]));

        // Assert
        Assert.Contains("personal", error.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void FindDestinationErrors_AnExplicitAliasNoAccountMaps_IsRefused()
    {
        // Arrange
        var junk = new DeclaredMailFolder(MailFolderAlias.Create("JUNK"), MailFolderSpecialUse.Junk);
        var account = AccountMapping(mapped: [junk]);

        // Act
        var errors = SpamJunkFolderRules.FindDestinationErrors(Filing("quarantine"), [account]);

        // Assert
        Assert.Single(errors);
    }

    /// <summary>A destination written beside switches that are off is an operator staging a change, not a defect.</summary>
    [Fact]
    public void FindDestinationErrors_FilingSwitchedOff_JudgesNothing()
    {
        // Arrange
        var options = new SpamClassificationOptions
        {
            Enabled = true,
            Actions = new SpamActionOptions { MarkAsRead = true, JunkFolder = "quarantine" },
        };

        // Act
        var errors = SpamJunkFolderRules.FindDestinationErrors(options, [AccountMapping()]);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void FindDestinationErrors_NoAccountDeclaredAtAll_JudgesNothing()
    {
        // Act
        var errors = SpamJunkFolderRules.FindDestinationErrors(Filing(junkFolder: null), []);

        // Assert
        Assert.Empty(errors);
    }

    private static SpamClassificationOptions Filing(string? junkFolder) => new()
    {
        Enabled = true,
        Actions = new SpamActionOptions { FileInJunkFolder = true, JunkFolder = junkFolder },
    };

    private static DeclaredMailAccount AccountMapping(
        string accountId = "personal",
        IReadOnlyCollection<DeclaredMailFolder>? mapped = null) => new(
        accountId,
        mapped ?? [],
        MailRuleActionPermissions.Default);
}
