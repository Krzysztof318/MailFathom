// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Access;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Access;

/// <summary>Covers which arrangements of an authentication entry the endpoint accepts.</summary>
/// <remarks>
/// A method is selected by carrying its own block, so the shapes worth stating are the ones an operator could write
/// believing they had turned something on. An entry stating nothing is the one that has to fail, because it is what a
/// misspelled block name binds to and it would otherwise be an endpoint quietly accepting one credential fewer.
/// </remarks>
public sealed class TransportAuthenticationOptionsTests
{
    private const string SettingPath = "McpEndpoint:Authentication:0";

    [Fact]
    public void StatesAMethod_AnEntryCarryingAPublicKey_SelectsThatMethod()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { PublicKey = APublicKey() };

        // Act, Assert
        Assert.True(entry.StatesAMethod);
    }

    [Fact]
    public void FindConfigurationErrors_AnEntryCarryingAPublicKey_ReportsNothing()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions { PublicKey = APublicKey() };

        // Act, Assert
        Assert.Empty(entry.FindConfigurationErrors(SettingPath));
    }

    /// <summary>Nothing conflicts between the methods, so an operator who groups a key and a public key into one entry gets both rather than a refusal.</summary>
    [Fact]
    public void FindConfigurationErrors_AnEntryCarryingSeveralBlocks_ReportsNothing()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions
        {
            ApiKey = new ConfiguredSecret { Name = "workstation", SecretReference = "plaintext:a-key" },
            PublicKey = APublicKey(),
        };

        // Act, Assert
        Assert.Empty(entry.FindConfigurationErrors(SettingPath));
    }

    /// <summary>The refusal has to name every block an operator could have meant, or the one they misspelled goes unmentioned.</summary>
    [Fact]
    public void FindConfigurationErrors_AnEntryStatingNoMethod_NamesEveryBlockItCouldHaveCarried()
    {
        // Arrange
        var entry = new TransportAuthenticationOptions();

        // Act
        var errors = entry.FindConfigurationErrors(SettingPath);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains(SettingPath, reported, StringComparison.Ordinal);
        Assert.Contains("ApiKey", reported, StringComparison.Ordinal);
        Assert.Contains("PublicKey", reported, StringComparison.Ordinal);
        Assert.Contains("OAuth", reported, StringComparison.Ordinal);
    }

    /// <summary>Every configured key is offered to the surface in configuration order, because rotation is a second entry rather than a nested list.</summary>
    [Fact]
    public void PublicKeysIn_SeveralEntries_ReportsEveryConfiguredKeyInOrder()
    {
        // Arrange
        TransportAuthenticationOptions[] entries =
        [
            new() { PublicKey = APublicKey("nightly") },
            new() { ApiKey = new ConfiguredSecret { Name = "workstation", SecretReference = "plaintext:a-key" } },
            new() { PublicKey = APublicKey("nightly-next") },
        ];

        // Act
        var publicKeys = TransportAuthenticationConfiguration.PublicKeysIn(entries);

        // Assert
        Assert.Equal(["nightly", "nightly-next"], publicKeys.Select(key => key.Name));
    }

    private static ConfiguredSecret APublicKey(string name = "nightly") =>
        new() { Name = name, SecretReference = "file:/etc/mailfathom/nightly.pub" };
}
