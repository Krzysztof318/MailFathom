// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.OwnerSettings;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.OwnerSettings;

/// <summary>Covers every rule a start judges the declared owners by, before a container or a database row exists.</summary>
/// <remarks>
/// The same rules judge a configuration write, so a declaration accepted at startup and refused by the next write —
/// or the reverse — would be a deployment that cannot be changed through the surface that changes it. Asserting them
/// here is what keeps the two readings one.
/// </remarks>
public sealed class DeclaredOwnersTests
{
    private const string Alex = "1a7f6b1c-2d3e-4f50-8a91-b2c3d4e5f601";
    private const string Morgan = "2b8f7c2d-3e4f-4a61-9b02-c3d4e5f6a712";
    private static readonly DateOnly Today = new(2026, 8, 27);

    [Fact]
    public void ReadFrom_ADeclaredOwner_ReadsTheirEnvelopeAndTheirMailboxes()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Accounts:0:Id"] = Alex,
            ["Accounts:0:DisplayName"] = "alex",
            ["Accounts:0:MailAccounts:0:AccountId"] = "alex-work",
        });

        // Act
        var owners = DeclaredOwners.ReadFrom(configuration);

        // Assert
        var owner = Assert.Single(owners);
        Assert.Equal(Alex, owner.Id);
        Assert.Equal("alex", owner.DisplayName);
        Assert.Equal(["alex-work"], owner.MailAccounts.Select(account => account.AccountId));
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
        Assert.Throws<InvalidOperationException>(() => DeclaredOwners.ReadFrom(configuration));
    }

    [Fact]
    public void FindConfigurationErrors_ADeploymentDeclaringNoOwner_AcceptsIt()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["MailSynchronization:Enabled"] = "true",
            ["MailSynchronization:Accounts:0:AccountId"] = "primary",
        });

        // Act
        var errors = DeclaredOwners.FindConfigurationErrors(configuration, Today);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Each owner names their own mailboxes, and every rule a mail account is declared under is run over them.</summary>
    [Fact]
    public void FindConfigurationErrors_OwnersDeclaringTheirOwnMailboxes_AcceptsThem()
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
        var errors = DeclaredOwners.FindConfigurationErrors(configuration, Today);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>An owner declared before their first mailbox is an ordinary state rather than an unfinished one.</summary>
    [Fact]
    public void FindConfigurationErrors_AnOwnerDeclaringNoMailbox_AcceptsThem()
    {
        // Arrange
        var configuration = Configuration(
        [
            new("Accounts:0:Id", Alex),
            new("Accounts:0:DisplayName", "alex"),
        ]);

        // Act
        var errors = DeclaredOwners.FindConfigurationErrors(configuration, Today);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A mail account of an owner's own is judged by every rule the deployment's own section is judged by.</summary>
    [Fact]
    public void FindConfigurationErrors_AnOwnersMailboxNamingNoHost_IsRefusedUnderTheirLabel()
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
        var errors = DeclaredOwners.FindConfigurationErrors(configuration, Today);

        // Assert
        Assert.Contains(errors, error => error.StartsWith("Accounts:0:MailAccounts — alex:", StringComparison.Ordinal));
    }

    /// <summary>An owner is declared with the label an administrator tells them apart by, and with the identity their mail hangs on.</summary>
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
        var errors = DeclaredOwners.FindConfigurationErrors(configuration, Today);

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
        var errors = DeclaredOwners.FindConfigurationErrors(configuration, Today);

        // Assert
        Assert.Contains(errors, error => error.Contains("129 characters", StringComparison.Ordinal));
    }

    /// <summary>An identifier names one person, so everything either of them owns would be recorded against one row.</summary>
    [Fact]
    public void FindConfigurationErrors_TwoOwnersDeclaredUnderOneIdentifier_IsRefused()
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
        var errors = DeclaredOwners.FindConfigurationErrors(configuration, Today);

        // Assert
        Assert.Contains(errors, error => error.Contains("more than one owner under each of the identifiers", StringComparison.Ordinal));
    }

    /// <summary>A label is what an administrator selects an owner by, so two owners carrying one leaves nothing to select on.</summary>
    [Fact]
    public void FindConfigurationErrors_TwoOwnersDeclaredUnderOneLabel_IsRefused()
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
        var errors = DeclaredOwners.FindConfigurationErrors(configuration, Today);

        // Assert
        Assert.Contains(errors, error => error.Contains("more than one owner under each of the labels", StringComparison.Ordinal));
    }

    /// <summary>The deployment's own section names no owner, so its mailboxes have nobody to belong to once owners are declared.</summary>
    [Fact]
    public void FindConfigurationErrors_MailboxesInTheDeploymentSectionBesideDeclaredOwners_IsRefused()
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
        var errors = DeclaredOwners.FindConfigurationErrors(configuration, Today);

        // Assert
        Assert.Contains(errors, error => error.StartsWith("MailSynchronization:Accounts declares", StringComparison.Ordinal));
    }

    /// <summary>The per-account settings ports this release resolves are keyed by the identifier alone, so a shared name reaches whichever declaration the lookup met.</summary>
    [Fact]
    public void FindConfigurationErrors_AMailAccountNameTwoOwnersShare_IsRefused()
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
        var errors = DeclaredOwners.FindConfigurationErrors(configuration, Today);

        // Assert
        Assert.Contains(errors, error => error.StartsWith("More than one declared owner names a mail account", StringComparison.Ordinal));
    }

    /// <summary>Switching a worker on with no work is the deployment's own defect, and this is the one place the effective set is visible.</summary>
    [Fact]
    public void FindConfigurationErrors_SynchronizationOnWithNoMailboxAnywhere_IsRefused()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["MailSynchronization:Enabled"] = "true",
            ["Accounts:0:Id"] = Alex,
            ["Accounts:0:DisplayName"] = "alex",
        });

        // Act
        var errors = DeclaredOwners.FindConfigurationErrors(configuration, Today);

        // Assert
        Assert.Contains(errors, error => error.StartsWith("MailSynchronization:Enabled is on", StringComparison.Ordinal));
    }

    /// <summary>A list this long was generated rather than written, which is worth stopping for on its own.</summary>
    [Fact]
    public void FindConfigurationErrors_MoreOwnersThanADeploymentMayServe_IsRefusedOnThatAlone()
    {
        // Arrange
        var declarations = Enumerable.Range(0, DeclaredOwners.MaximumDeclaredOwners + 1)
            .SelectMany(index => new KeyValuePair<string, string?>[]
            {
                new($"Accounts:{index}:Id", new Guid(index, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]).ToString()),
                new($"Accounts:{index}:DisplayName", $"owner-{index}"),
            });

        var configuration = Configuration(declarations);

        // Act
        var errors = DeclaredOwners.FindConfigurationErrors(configuration, Today);

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains($"past the {DeclaredOwners.MaximumDeclaredOwners} one deployment may serve", error, StringComparison.Ordinal);
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
        var identifier = DeclaredOwners.TryReadIdentifier(declaredId);

        // Assert
        Assert.Null(identifier);
    }

    [Fact]
    public void TryReadIdentifier_AUuid_ReportsIt()
    {
        // Act
        var identifier = DeclaredOwners.TryReadIdentifier(Alex);

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

    private static IConfiguration Configuration(IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
