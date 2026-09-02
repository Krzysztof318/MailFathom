// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Rules;
using MailFathom.Host.Configuration.Spam;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Spam;

/// <summary>Covers what an owner's own classification block refuses at the write, and what an absent block means.</summary>
public sealed class OwnerSpamClassificationOptionsTests
{
    [Fact]
    public void FindRefusals_ABlockSettingNothing_ClassifiesNothingAndIsRefusedNothing()
    {
        // Arrange
        var options = new OwnerSpamClassificationOptions();

        // Act
        var refusals = options.FindRefusals(AccountsOf("work", "INBOX")).ToArray();

        // Assert
        Assert.Empty(refusals);
        Assert.False(options.Enabled);
        Assert.False(options.UseScanner);
        Assert.Null(options.ScannedFolders);
    }

    /// <summary>An owner who wrote nothing but their accounts has classification off, which no deployment setting overrides.</summary>
    [Fact]
    public void FindRefusals_ClassificationSwitchedOffWithNothingElseAsked_IsAccepted()
    {
        // Arrange
        var options = new OwnerSpamClassificationOptions { Enabled = false };

        // Act
        var refusals = options.FindRefusals(AccountsOf("work", "INBOX")).ToArray();

        // Assert
        Assert.Empty(refusals);
    }

    /// <summary>A scanner is only ever consulted where classification runs, so asking for one without it is a contradiction.</summary>
    [Fact]
    public void FindRefusals_AScannerAskedForWhileClassificationIsOff_IsRefused()
    {
        // Arrange
        var options = new OwnerSpamClassificationOptions { Enabled = false, UseScanner = true };

        // Act
        var refusal = Assert.Single(options.FindRefusals(AccountsOf("work", "INBOX")));

        // Assert
        Assert.Equal([nameof(OwnerSpamClassificationOptions.UseScanner)], refusal.MemberNames);
    }

    [Fact]
    public void FindRefusals_AScannedFolderThatIsNotAUsableAlias_IsRefused()
    {
        // Arrange
        var options = new OwnerSpamClassificationOptions { Enabled = true, ScannedFolders = ["  "] };

        // Act
        var refusal = Assert.Single(options.FindRefusals(AccountsOf("work", "INBOX")));

        // Assert
        Assert.Equal([nameof(OwnerSpamClassificationOptions.ScannedFolders)], refusal.MemberNames);
    }

    /// <summary>The bounds are the deployment's, and an owner writing outside them is told the range rather than the value.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    [InlineData(double.NaN)]
    public void FindRefusals_AThresholdOutsideTheDeploymentsRange_IsRefusedNamingTheRange(double threshold)
    {
        // Arrange
        var options = new OwnerSpamClassificationOptions { Enabled = true, ScannerThreshold = threshold };

        // Act
        var refusal = Assert.Single(options.FindRefusals(AccountsOf("work", "INBOX")));

        // Assert
        Assert.Equal([nameof(OwnerSpamClassificationOptions.ScannerThreshold)], refusal.MemberNames);
        Assert.Contains(
            SpamClassificationOptions.SmallestThreshold.ToString(CultureInfo.InvariantCulture),
            refusal.ErrorMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            SpamClassificationOptions.LargestThreshold.ToString(CultureInfo.InvariantCulture),
            refusal.ErrorMessage,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(5.5)]
    [InlineData(1000)]
    public void FindRefusals_AThresholdInsideTheDeploymentsRange_IsAccepted(double threshold)
    {
        // Arrange
        var options = new OwnerSpamClassificationOptions { Enabled = true, ScannerThreshold = threshold };

        // Act
        var refusals = options.FindRefusals(AccountsOf("work", "INBOX")).ToArray();

        // Assert
        Assert.Empty(refusals);
    }

    /// <summary>A destination is judged against this owner's own accounts, so a folder only somebody else maps is refused.</summary>
    [Fact]
    public void FindRefusals_AJunkFolderNoAccountOfTheirsMaps_IsRefused()
    {
        // Arrange
        var options = new OwnerSpamClassificationOptions
        {
            Enabled = true,
            Actions = new OwnerSpamActionOptions { MoveToJunkFolder = true, JunkFolder = "quarantine" },
        };

        // Act
        var refusal = Assert.Single(options.FindRefusals(AccountsOf("work", "INBOX")));

        // Assert
        Assert.Equal([nameof(OwnerSpamActionOptions.JunkFolder)], refusal.MemberNames);
        Assert.Contains("work", refusal.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void FindRefusals_AJunkFolderTheirOwnAccountMaps_IsAccepted()
    {
        // Arrange
        var options = new OwnerSpamClassificationOptions
        {
            Enabled = true,
            Actions = new OwnerSpamActionOptions { MoveToJunkFolder = true, JunkFolder = "quarantine" },
        };

        // Act
        var refusals = options.FindRefusals(AccountsOf("work", "INBOX", "quarantine")).ToArray();

        // Assert
        Assert.Empty(refusals);
    }

    /// <summary>A destination beside switches that are off is an owner staging a change, which refusing would make impossible.</summary>
    [Fact]
    public void FindRefusals_AJunkFolderNamedWhileFilingIsOff_IsNotJudgedAgainstTheirAccounts()
    {
        // Arrange
        var options = new OwnerSpamClassificationOptions
        {
            Enabled = true,
            Actions = new OwnerSpamActionOptions { JunkFolder = "quarantine" },
        };

        // Act
        var refusals = options.FindRefusals(AccountsOf("work", "INBOX")).ToArray();

        // Assert
        Assert.Empty(refusals);
    }

    /// <summary>Acting on a verdict nothing produces is refused here exactly as it is in the deployment's own section.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void FindRefusals_AnActionAskedForWhileClassificationIsOff_IsRefused(bool moveToJunkFolder, bool markAsRead)
    {
        // Arrange
        var options = new OwnerSpamClassificationOptions
        {
            Enabled = false,
            Actions = new OwnerSpamActionOptions { MoveToJunkFolder = moveToJunkFolder, MarkAsRead = markAsRead },
        };

        // Act
        var refusal = Assert.Single(options.FindRefusals(AccountsOf("work", "INBOX")));

        // Assert
        Assert.Equal(
            [nameof(OwnerSpamActionOptions.MoveToJunkFolder), nameof(OwnerSpamActionOptions.MarkAsRead)],
            refusal.MemberNames);
    }

    [Fact]
    public void FindRefusals_AnActionThresholdOutsideTheDeploymentsRange_IsRefusedNamingTheRange()
    {
        // Arrange
        var options = new OwnerSpamClassificationOptions
        {
            Enabled = true,
            Actions = new OwnerSpamActionOptions { Threshold = 1001 },
        };

        // Act
        var refusal = Assert.Single(options.FindRefusals(AccountsOf("work", "INBOX")));

        // Assert
        Assert.Equal([nameof(OwnerSpamActionOptions.Threshold)], refusal.MemberNames);
        Assert.Contains(
            SpamClassificationOptions.LargestThreshold.ToString(CultureInfo.InvariantCulture),
            refusal.ErrorMessage,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FindRefusals_NoAccounts_Throws()
    {
        // Arrange
        var options = new OwnerSpamClassificationOptions();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => options.FindRefusals(null!).ToArray());
    }

    private static IReadOnlyCollection<DeclaredMailAccount> AccountsOf(string accountId, params string[] aliases) =>
        DeclaredMailAccounts.ReadFrom(
        [
            new MailSynchronizationAccountOptions
            {
                AccountId = accountId,
                DisplayName = "The mailbox",
                Host = "imap.example.test",
                UserName = "mailfathom@example.test",
                Secrets = new MailAccountSecretOptions
                {
                    Password = new ConfiguredSecret
                    {
                        SecretReference = "systemd-credential:imap-password",
                    },
                },
                Folders = [.. aliases.Select(alias => new MailFolderMappingOptions { Alias = alias })],
            },
        ]);
}
