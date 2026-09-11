// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Host.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration;

/// <summary>
/// Proves that a deployment upgrading over a section it used to declare its users or its mailboxes in is stopped and
/// told what to run. Nothing binds either section any more, so a start that read past one would serve a roster and read
/// mailboxes other than the ones its operator reads in the file — which is the failure this refusal exists to make
/// impossible.
/// </summary>
public sealed class ComposedSettingsWithdrawnUserAndMailboxSectionTests
{
    [Fact]
    public void FindWithdrawnUserAndMailboxSectionRefusals_AConfigurationStillDeclaringUsers_IsRefusedNamingTheCommandsToRun()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Accounts:0:Id"] = "3f2b8c14-6d5a-4e9f-8b70-1c2d3e4f5a60",
            ["Accounts:0:DisplayName"] = "alex",
        });

        // Act
        var refusals = ComposedSettings.FindWithdrawnUserAndMailboxSectionRefusals(configuration);

        // Assert
        var refusal = Assert.Single(refusals);
        Assert.Equal("Accounts", refusal.SectionName);
        var error = Assert.Single(refusal.Errors);
        Assert.Contains("mfctl user add", error, StringComparison.Ordinal);
        Assert.Contains("mfctl user account add", error, StringComparison.Ordinal);
        Assert.Contains("nothing imports what the collection declared", error, StringComparison.Ordinal);
    }

    /// <summary>The deployment's own mail section is withdrawn beside the roster, and says so in its own sentence.</summary>
    [Fact]
    public void FindWithdrawnUserAndMailboxSectionRefusals_ADeploymentStillDeclaringItsOwnMailAccounts_IsRefusedNamingTheCommandsToRun()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["MailSynchronization:Accounts:0:AccountId"] = "work",
        });

        // Act
        var refusals = ComposedSettings.FindWithdrawnUserAndMailboxSectionRefusals(configuration);

        // Assert
        var refusal = Assert.Single(refusals);
        Assert.Equal("MailSynchronization:Accounts", refusal.SectionName);
        var error = Assert.Single(refusal.Errors);
        Assert.Contains("mfctl user add", error, StringComparison.Ordinal);
        Assert.Contains("mfctl user account add", error, StringComparison.Ordinal);
        Assert.Contains("Nothing imports what the section declared", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// An upgraded file whose accounts were emptied rather than deleted declares no mailbox, so it stops no start. It is
    /// read from JSON because an empty array is a shape only a file can state.
    /// </summary>
    [Fact]
    public void FindWithdrawnUserAndMailboxSectionRefusals_AMailSectionCarryingAnEmptyAccountList_IsNotRefused()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(
                """{ "MailSynchronization": { "Enabled": true, "Accounts": [] } }""")))
            .Build();

        // Act
        var refusals = ComposedSettings.FindWithdrawnUserAndMailboxSectionRefusals(configuration);

        // Assert
        Assert.Empty(refusals);
    }

    /// <summary>
    /// A section carrying no user describes nobody, so stopping a start over it would refuse a deployment that has
    /// nothing left to correct. What the refusal is about is a roster an operator still believes is being served.
    /// </summary>
    [Fact]
    public void FindWithdrawnUserAndMailboxSectionRefusals_ASectionLeftBehindWithNoUserInIt_IsNotRefused()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes("""{ "Accounts": [] }""")))
            .Build();

        // Act
        var refusals = ComposedSettings.FindWithdrawnUserAndMailboxSectionRefusals(configuration);

        // Assert
        Assert.Empty(refusals);
    }

    /// <summary>A refusal here is first among the groups, so an operator carrying it meets it rather than a later section's.</summary>
    [Fact]
    public void FindRefusals_AConfigurationStillDeclaringUsersBesideAnotherMistake_ReportsTheWithdrawnCollectionFirst()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Accounts:0:Id"] = "3f2b8c14-6d5a-4e9f-8b70-1c2d3e4f5a60",
            ["Accounts:0:DisplayName"] = "alex",
            ["Mcp:Enabled"] = "true",
            ["Mcp:Authentication:0:ApiKey:Name"] = "workstation",
        });

        // Act
        var refusals = ComposedSettings.FindRefusals(configuration, declaredAccounts: null);

        // Assert
        Assert.Equal("Accounts", refusals[0].SectionName);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> keys) =>
        new ConfigurationBuilder().AddInMemoryCollection(keys).Build();
}
