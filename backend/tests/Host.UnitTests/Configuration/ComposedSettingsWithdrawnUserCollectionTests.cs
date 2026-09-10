// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Host.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration;

/// <summary>
/// Proves that a deployment upgrading over the collection it used to declare its users in is stopped and told what to
/// run. Nothing binds the section any more, so a start that read past it would serve one user while its operator read a
/// file describing several — which is the failure this refusal exists to make impossible.
/// </summary>
public sealed class ComposedSettingsWithdrawnUserCollectionTests
{
    [Fact]
    public void FindWithdrawnUserCollectionRefusals_AConfigurationStillDeclaringUsers_IsRefusedNamingTheCommandsToRun()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Accounts:0:Id"] = "3f2b8c14-6d5a-4e9f-8b70-1c2d3e4f5a60",
            ["Accounts:0:DisplayName"] = "alex",
        });

        // Act
        var refusals = ComposedSettings.FindWithdrawnUserCollectionRefusals(configuration);

        // Assert
        var refusal = Assert.Single(refusals);
        Assert.Equal("Accounts", refusal.SectionName);
        var error = Assert.Single(refusal.Errors);
        Assert.Contains("mfctl user add", error, StringComparison.Ordinal);
        Assert.Contains("mfctl user account add", error, StringComparison.Ordinal);
        Assert.Contains("nothing imports what the collection declared", error, StringComparison.Ordinal);
    }

    /// <summary>The deployment's own mail section carries the same word and is a different collection, so it is untouched.</summary>
    [Fact]
    public void FindWithdrawnUserCollectionRefusals_ADeploymentDeclaringOnlyItsOwnMailAccounts_IsNotRefused()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["MailSynchronization:Accounts:0:AccountId"] = "work",
        });

        // Act
        var refusals = ComposedSettings.FindWithdrawnUserCollectionRefusals(configuration);

        // Assert
        Assert.Empty(refusals);
    }

    /// <summary>
    /// A section carrying no user describes nobody, so stopping a start over it would refuse a deployment that has
    /// nothing left to correct. What the refusal is about is a roster an operator still believes is being served.
    /// </summary>
    [Fact]
    public void FindWithdrawnUserCollectionRefusals_ASectionLeftBehindWithNoUserInIt_IsNotRefused()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes("""{ "Accounts": [] }""")))
            .Build();

        // Act
        var refusals = ComposedSettings.FindWithdrawnUserCollectionRefusals(configuration);

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
        var refusals = ComposedSettings.FindRefusals(configuration);

        // Assert
        Assert.Equal("Accounts", refusals[0].SectionName);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> keys) =>
        new ConfigurationBuilder().AddInMemoryCollection(keys).Build();
}
