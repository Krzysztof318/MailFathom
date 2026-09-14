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

/// <summary>Covers what an account's own classification block refuses at the write, and what an absent block means.</summary>
public sealed class MailAccountSpamClassificationOptionsTests
{
    [Fact]
    public void FindRefusals_ABlockSettingNothing_ClassifiesNothingAndIsRefusedNothing()
    {
        // Arrange
        var options = new MailAccountSpamClassificationOptions();

        // Act
        var refusals = options.FindRefusals(AccountOf("work", "INBOX")).ToArray();

        // Assert
        Assert.Empty(refusals);
        Assert.False(options.Enabled);
        Assert.False(options.UseScanner);
        Assert.Null(options.ScannedFolders);
    }

    /// <summary>An account whose record states nothing else has classification off, which no deployment setting overrides.</summary>
    [Fact]
    public void FindRefusals_ClassificationSwitchedOffWithNothingElseAsked_IsAccepted()
    {
        // Arrange
        var options = new MailAccountSpamClassificationOptions { Enabled = false };

        // Act
        var refusals = options.FindRefusals(AccountOf("work", "INBOX")).ToArray();

        // Assert
        Assert.Empty(refusals);
    }

    /// <summary>A scanner is only ever consulted where classification runs, so asking for one without it is a contradiction.</summary>
    [Fact]
    public void FindRefusals_AScannerAskedForWhileClassificationIsOff_IsRefused()
    {
        // Arrange
        var options = new MailAccountSpamClassificationOptions { Enabled = false, UseScanner = true };

        // Act
        var refusal = Assert.Single(options.FindRefusals(AccountOf("work", "INBOX")));

        // Assert
        Assert.Equal([nameof(MailAccountSpamClassificationOptions.UseScanner)], refusal.MemberNames);
    }

    [Fact]
    public void FindRefusals_AScannedFolderThatIsNotAUsableAlias_IsRefused()
    {
        // Arrange
        var options = new MailAccountSpamClassificationOptions { Enabled = true, ScannedFolders = ["  "] };

        // Act
        var refusal = Assert.Single(options.FindRefusals(AccountOf("work", "INBOX")));

        // Assert
        Assert.Equal([nameof(MailAccountSpamClassificationOptions.ScannedFolders)], refusal.MemberNames);
    }

    /// <summary>The bounds are the deployment's, and a record writing outside them is told the range rather than the value.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    [InlineData(double.NaN)]
    public void FindRefusals_AThresholdOutsideTheDeploymentsRange_IsRefusedNamingTheRange(double threshold)
    {
        // Arrange
        var options = new MailAccountSpamClassificationOptions { Enabled = true, ScannerThreshold = threshold };

        // Act
        var refusal = Assert.Single(options.FindRefusals(AccountOf("work", "INBOX")));

        // Assert
        Assert.Equal([nameof(MailAccountSpamClassificationOptions.ScannerThreshold)], refusal.MemberNames);
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
        var options = new MailAccountSpamClassificationOptions { Enabled = true, ScannerThreshold = threshold };

        // Act
        var refusals = options.FindRefusals(AccountOf("work", "INBOX")).ToArray();

        // Assert
        Assert.Empty(refusals);
    }

    /// <summary>A destination is judged against this account's own folders, so one only another mailbox maps is refused.</summary>
    [Fact]
    public void FindRefusals_AJunkFolderItsOwnAccountDoesNotMap_IsRefused()
    {
        // Arrange
        var options = new MailAccountSpamClassificationOptions
        {
            Enabled = true,
            Actions = new MailAccountSpamActionOptions { MoveToJunkFolder = true, JunkFolder = "quarantine" },
        };

        // Act
        var refusal = Assert.Single(options.FindRefusals(AccountOf("work", "INBOX")));

        // Assert
        Assert.Equal([nameof(MailAccountSpamActionOptions.JunkFolder)], refusal.MemberNames);
        Assert.Contains("work", refusal.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void FindRefusals_AJunkFolderItsOwnAccountMaps_IsAccepted()
    {
        // Arrange
        var options = new MailAccountSpamClassificationOptions
        {
            Enabled = true,
            Actions = new MailAccountSpamActionOptions { MoveToJunkFolder = true, JunkFolder = "quarantine" },
        };

        // Act
        var refusals = options.FindRefusals(AccountOf("work", "INBOX", "quarantine")).ToArray();

        // Assert
        Assert.Empty(refusals);
    }

    /// <summary>A destination beside switches that are off is somebody staging a change, which refusing would make impossible.</summary>
    [Fact]
    public void FindRefusals_AJunkFolderNamedWhileFilingIsOff_IsNotJudgedAgainstItsFolders()
    {
        // Arrange
        var options = new MailAccountSpamClassificationOptions
        {
            Enabled = true,
            Actions = new MailAccountSpamActionOptions { JunkFolder = "quarantine" },
        };

        // Act
        var refusals = options.FindRefusals(AccountOf("work", "INBOX")).ToArray();

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
        var options = new MailAccountSpamClassificationOptions
        {
            Enabled = false,
            Actions = new MailAccountSpamActionOptions { MoveToJunkFolder = moveToJunkFolder, MarkAsRead = markAsRead },
        };

        // Act
        var refusal = Assert.Single(options.FindRefusals(AccountOf("work", "INBOX")));

        // Assert
        Assert.Equal(
            [nameof(MailAccountSpamActionOptions.MoveToJunkFolder), nameof(MailAccountSpamActionOptions.MarkAsRead)],
            refusal.MemberNames);
    }

    [Fact]
    public void FindRefusals_AnActionThresholdOutsideTheDeploymentsRange_IsRefusedNamingTheRange()
    {
        // Arrange
        var options = new MailAccountSpamClassificationOptions
        {
            Enabled = true,
            Actions = new MailAccountSpamActionOptions { Threshold = 1001 },
        };

        // Act
        var refusal = Assert.Single(options.FindRefusals(AccountOf("work", "INBOX")));

        // Assert
        Assert.Equal([nameof(MailAccountSpamActionOptions.Threshold)], refusal.MemberNames);
        Assert.Contains(
            SpamClassificationOptions.LargestThreshold.ToString(CultureInfo.InvariantCulture),
            refusal.ErrorMessage,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FindRefusals_NoAccount_Throws()
    {
        // Arrange
        var options = new MailAccountSpamClassificationOptions();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => options.FindRefusals(null!).ToArray());
    }

    private static DeclaredMailAccount AccountOf(string accountId, params string[] aliases) =>
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
        ]).Single();
}
