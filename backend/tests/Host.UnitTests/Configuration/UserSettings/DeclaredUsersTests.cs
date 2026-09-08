// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Host.Configuration.UserSettings;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings;

/// <summary>Covers every rule a start judges the declared users by, before a container or a database row exists.</summary>
/// <remarks>
/// The same rules judge a configuration write, so a declaration accepted at startup and refused by the next write —
/// or the reverse — would be a deployment that cannot be changed through the surface that changes it. Asserting them
/// here is what keeps the two readings one.
/// </remarks>
public sealed class DeclaredUsersTests
{
    private const string Alex = "1a7f6b1c-2d3e-4f50-8a91-b2c3d4e5f601";
    private const string Morgan = "2b8f7c2d-3e4f-4a61-9b02-c3d4e5f6a712";
    private static readonly DateOnly Today = new(2026, 8, 27);

    [Fact]
    public void ReadFrom_ADeclaredUser_ReadsTheirEnvelopeAndTheirMailboxes()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Accounts:0:Id"] = Alex,
            ["Accounts:0:DisplayName"] = "alex",
            ["Accounts:0:MailAccounts:0:AccountId"] = "alex-work",
        });

        // Act
        var users = DeclaredUsers.ReadFrom(configuration);

        // Assert
        var user = Assert.Single(users);
        Assert.Equal(Alex, user.Id);
        Assert.Equal("alex", user.DisplayName);
        Assert.Equal(["alex-work"], user.MailAccounts.Select(account => account.AccountId));
    }

    /// <summary>A property nothing binds leaves the host running on defaults while the operator believes their file is in force.</summary>
    [Fact]
    public void ReadFrom_ADeclarationCarryingAPropertyNothingBinds_IsRefused()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Accounts:0:Id"] = Alex,
            ["Accounts:0:DisplayNames"] = "alex",
        });

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => DeclaredUsers.ReadFrom(configuration));
    }

    [Fact]
    public void FindConfigurationErrors_ADeploymentDeclaringNoUser_AcceptsIt()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["MailSynchronization:Enabled"] = "true",
            ["MailSynchronization:Accounts:0:AccountId"] = "primary",
        });

        // Act
        var errors = DeclaredUsers.FindConfigurationErrors(configuration, Today);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Each user names their own mailboxes, and every rule a mail account is declared under is run over them.</summary>
    [Fact]
    public void FindConfigurationErrors_UsersDeclaringTheirOwnMailboxes_AcceptsThem()
    {
        // Arrange
        var configuration = Configuration(
        [
            new("MailSynchronization:Enabled", "true"),
            new("Accounts:0:Id", Alex),
            new("Accounts:0:DisplayName", "alex"),
            .. Mailbox("Accounts:0:MailAccounts:0", "alex-work", "Alex at work"),
            new("Accounts:1:Id", Morgan),
            new("Accounts:1:DisplayName", "morgan"),
            .. Mailbox("Accounts:1:MailAccounts:0", "morgan-work", "Morgan at work"),
        ]);

        // Act
        var errors = DeclaredUsers.FindConfigurationErrors(configuration, Today);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A user declared before their first mailbox is an ordinary state rather than an unfinished one.</summary>
    [Fact]
    public void FindConfigurationErrors_AUserDeclaringNoMailbox_AcceptsThem()
    {
        // Arrange
        var configuration = Configuration(
        [
            new("Accounts:0:Id", Alex),
            new("Accounts:0:DisplayName", "alex"),
        ]);

        // Act
        var errors = DeclaredUsers.FindConfigurationErrors(configuration, Today);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A mail account of a user's own is judged by every rule the deployment's own section is judged by.</summary>
    [Fact]
    public void FindConfigurationErrors_AUsersMailboxNamingNoHost_IsRefusedUnderTheirLabel()
    {
        // Arrange
        var configuration = Configuration(
        [
            new("Accounts:0:Id", Alex),
            new("Accounts:0:DisplayName", "alex"),
            new("Accounts:0:MailAccounts:0:AccountId", "alex-work"),
            new("Accounts:0:MailAccounts:0:DisplayName", "Alex at work"),
        ]);

        // Act
        var errors = DeclaredUsers.FindConfigurationErrors(configuration, Today);

        // Assert
        Assert.Contains(errors, error => error.StartsWith("Accounts:0:MailAccounts — alex:", StringComparison.Ordinal));
    }

    /// <summary>
    /// A window opening after today excludes every email the mailbox holds, which a running deployment reports as a
    /// mailbox that synchronizes nothing rather than as a setting nobody could have meant.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_AUsersMailboxWhoseWindowOpensAfterToday_IsRefusedUnderTheirLabel()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Accounts:0:Id"] = Alex,
            ["Accounts:0:DisplayName"] = "alex",
            ["Accounts:0:MailAccounts:0:AccountId"] = "alex-work",
            ["Accounts:0:MailAccounts:0:DisplayName"] = "Alex at work",
            ["Accounts:0:MailAccounts:0:Host"] = "imap.example.test",
            ["Accounts:0:MailAccounts:0:UserName"] = "alex@example.test",
            ["Accounts:0:MailAccounts:0:EarliestEmailReceivedDate"] = Today.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        });

        // Act
        var errors = DeclaredUsers.FindConfigurationErrors(configuration, Today);

        // Assert
        Assert.Contains(
            errors,
            error => error.StartsWith("Accounts:0:MailAccounts — alex:", StringComparison.Ordinal)
                && error.Contains("earliest email received date", StringComparison.Ordinal));
    }

    /// <summary>A user is declared with the label an administrator tells them apart by, and with the identity their mail hangs on.</summary>
    [Theory]
    [InlineData(null, Alex, "DisplayName")]
    [InlineData("   ", Alex, "DisplayName")]
    [InlineData("alex", null, "Id")]
    [InlineData("alex", "not-a-uuid", "Id")]
    [InlineData("alex", "00000000-0000-0000-0000-000000000000", "Id")]
    public void FindConfigurationErrors_ADeclarationWithNoUsableEnvelope_NamesThePropertyThatIsMissing(
        string? displayName,
        string? id,
        string expectedProperty)
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Accounts:0:Id"] = id,
            ["Accounts:0:DisplayName"] = displayName,
        });

        // Act
        var errors = DeclaredUsers.FindConfigurationErrors(configuration, Today);

        // Assert
        Assert.Contains(errors, error => error.StartsWith($"Accounts:0:{expectedProperty}", StringComparison.Ordinal));
    }

    [Fact]
    public void FindConfigurationErrors_ALabelPastWhatTheColumnStores_IsRefused()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Accounts:0:Id"] = Alex,
            ["Accounts:0:DisplayName"] = new string('a', 129),
        });

        // Act
        var errors = DeclaredUsers.FindConfigurationErrors(configuration, Today);

        // Assert
        Assert.Contains(errors, error => error.Contains("129 characters", StringComparison.Ordinal));
    }

    /// <summary>An identifier names one person, so everything either of them owns would be recorded against one row.</summary>
    [Fact]
    public void FindConfigurationErrors_TwoUsersDeclaredUnderOneIdentifier_IsRefused()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Accounts:0:Id"] = Alex,
            ["Accounts:0:DisplayName"] = "alex",
            ["Accounts:1:Id"] = Alex,
            ["Accounts:1:DisplayName"] = "morgan",
        });

        // Act
        var errors = DeclaredUsers.FindConfigurationErrors(configuration, Today);

        // Assert
        Assert.Contains(errors, error => error.Contains("more than one user under each of the identifiers", StringComparison.Ordinal));
    }

    /// <summary>A label is what an administrator selects a user by, so two users carrying one leaves nothing to select on.</summary>
    [Fact]
    public void FindConfigurationErrors_TwoUsersDeclaredUnderOneLabel_IsRefused()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Accounts:0:Id"] = Alex,
            ["Accounts:0:DisplayName"] = "alex",
            ["Accounts:1:Id"] = Morgan,
            ["Accounts:1:DisplayName"] = "alex",
        });

        // Act
        var errors = DeclaredUsers.FindConfigurationErrors(configuration, Today);

        // Assert
        Assert.Contains(errors, error => error.Contains("more than one user under each of the labels", StringComparison.Ordinal));
    }

    /// <summary>The deployment's own section names no user, so its mailboxes have nobody to belong to once users are declared.</summary>
    [Fact]
    public void FindConfigurationErrors_MailboxesInTheDeploymentSectionBesideDeclaredUsers_IsRefused()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["MailSynchronization:Accounts:0:AccountId"] = "primary",
            ["MailSynchronization:Accounts:0:DisplayName"] = "The primary mailbox",
            ["Accounts:0:Id"] = Alex,
            ["Accounts:0:DisplayName"] = "alex",
            ["Accounts:0:MailAccounts:0:AccountId"] = "alex-work",
            ["Accounts:0:MailAccounts:0:DisplayName"] = "Alex at work",
        });

        // Act
        var errors = DeclaredUsers.FindConfigurationErrors(configuration, Today);

        // Assert
        Assert.Contains(errors, error => error.StartsWith("MailSynchronization:Accounts declares", StringComparison.Ordinal));
    }

    /// <summary>The per-account settings ports this release resolves are keyed by the identifier alone, so a shared name reaches whichever declaration the lookup met.</summary>
    [Fact]
    public void FindConfigurationErrors_AMailAccountNameTwoUsersShare_IsRefused()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Accounts:0:Id"] = Alex,
            ["Accounts:0:DisplayName"] = "alex",
            ["Accounts:0:MailAccounts:0:AccountId"] = "work",
            ["Accounts:0:MailAccounts:0:DisplayName"] = "Alex at work",
            ["Accounts:1:Id"] = Morgan,
            ["Accounts:1:DisplayName"] = "morgan",
            ["Accounts:1:MailAccounts:0:AccountId"] = "work",
            ["Accounts:1:MailAccounts:0:DisplayName"] = "Morgan at work",
        });

        // Act
        var errors = DeclaredUsers.FindConfigurationErrors(configuration, Today);

        // Assert
        Assert.Contains(errors, error => error.StartsWith("More than one declared user names a mail account", StringComparison.Ordinal));
    }

    /// <summary>
    /// Switching a worker on with no work is the deployment's own defect, and the files no longer decide whether that
    /// is what happened: a user's mailboxes are a record as well as a section. So this reads the switch and the
    /// startup gate judges the roster, which is why a declaration naming no mailbox is not an error here.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_SynchronizationOnWithNoMailboxInAnyFile_IsLeftToTheStartupGate()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["MailSynchronization:Enabled"] = "true",
            ["Accounts:0:Id"] = Alex,
            ["Accounts:0:DisplayName"] = "alex",
        });

        // Act
        var errors = DeclaredUsers.FindConfigurationErrors(configuration, Today);

        // Assert
        Assert.Empty(errors);
        Assert.True(DeclaredUsers.SynchronizationIsOn(configuration));
    }

    /// <summary>A deployment that asked for nothing to be refreshed is what the switch reads as off, including where no section names it at all.</summary>
    [Fact]
    public void SynchronizationIsOn_ADeploymentThatNamedNoSwitch_ReadsAsOff()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Accounts:0:Id"] = Alex,
            ["Accounts:0:DisplayName"] = "alex",
        });

        // Act
        var enabled = DeclaredUsers.SynchronizationIsOn(configuration);

        // Assert
        Assert.False(enabled);
    }

    /// <summary>A list this long was generated rather than written, which is worth stopping for on its own.</summary>
    [Fact]
    public void FindConfigurationErrors_MoreUsersThanADeploymentMayServe_IsRefusedOnThatAlone()
    {
        // Arrange
        var declarations = Enumerable.Range(0, DeclaredUsers.MaximumDeclaredUsers + 1)
            .SelectMany(index => new KeyValuePair<string, string?>[]
            {
                new($"Accounts:{index}:Id", new Guid(index, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]).ToString()),
                new($"Accounts:{index}:DisplayName", $"user-{index}"),
            });

        var configuration = Configuration(declarations);

        // Act
        var errors = DeclaredUsers.FindConfigurationErrors(configuration, Today);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains($"past the {DeclaredUsers.MaximumDeclaredUsers} one deployment may serve", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void TryReadIdentifier_AValueNamingNobody_ReportsNothing(string? declaredId)
    {
        // Act
        var identifier = DeclaredUsers.TryReadIdentifier(declaredId);

        // Assert
        Assert.Null(identifier);
    }

    [Fact]
    public void TryReadIdentifier_AUuid_ReportsIt()
    {
        // Act
        var identifier = DeclaredUsers.TryReadIdentifier(Alex);

        // Assert
        Assert.Equal(new Guid(Alex), identifier);
    }

    /// <summary>States a mail account complete enough that every rule a declaration is judged by accepts it.</summary>
    /// <param name="path">The configuration path the account is declared at.</param>
    /// <param name="accountId">The identifier the account is named by.</param>
    /// <param name="displayName">The name the account is published under.</param>
    /// <returns>The keys that declaration is written as.</returns>
    private static IEnumerable<KeyValuePair<string, string?>> Mailbox(
        string path,
        string accountId,
        string displayName) =>
    [
        new($"{path}:AccountId", accountId),
        new($"{path}:DisplayName", displayName),
        new($"{path}:Host", $"imap.{accountId}.example.test"),
        new($"{path}:UserName", $"{accountId}@example.test"),
        new($"{path}:Secrets:Password:SecretReference", $"systemd-credential:imap-{accountId}-password"),
    ];

    /// <summary>
    /// The same rule a record write is refused under holds over a declaration in the deployment's own file, because the
    /// two are one block arriving by two routes and a rule that ran on only one of them is a rule an operator can step
    /// around by editing the file instead.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_ADeclaredUserSwitchingOffAScannerTheDeploymentRequires_IsRefused()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["MailSynchronization:Enabled"] = "true",
            ["SensitiveContent:Secrets:Enabled"] = "true",
            ["Accounts:0:Id"] = Alex,
            ["Accounts:0:DisplayName"] = "alex",
            ["Accounts:0:SensitiveContent:Secrets:Enabled"] = "false",
        });

        // Act
        var errors = DeclaredUsers.FindConfigurationErrors(configuration, Today);

        // Assert
        var error = Assert.Single(errors);

        Assert.Contains("Accounts:0:SensitiveContent:Secrets:Enabled", error, StringComparison.Ordinal);
    }

    /// <summary>The declaration a deployment writes may tighten exactly as a user's own record may.</summary>
    [Fact]
    public void FindConfigurationErrors_ADeclaredUserSwitchingOnAScannerTheDeploymentLeftOff_AcceptsIt()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["MailSynchronization:Enabled"] = "true",
            ["Accounts:0:Id"] = Alex,
            ["Accounts:0:DisplayName"] = "alex",
            ["Accounts:0:SensitiveContent:Secrets:Enabled"] = "true",
        });

        // Act
        var errors = DeclaredUsers.FindConfigurationErrors(configuration, Today);

        // Assert
        Assert.Empty(errors);
    }

    private static IConfiguration Configuration(IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
