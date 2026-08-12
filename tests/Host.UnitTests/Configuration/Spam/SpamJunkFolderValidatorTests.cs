// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Spam;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Spam;

/// <summary>Covers the seam between the classification section and the accounts declared in another one.</summary>
/// <remarks>
/// The rule itself is covered by <see cref="SpamJunkFolderRulesTests" />. What is worth proving here is the reading: an
/// account list found under the wrong key would report no account at all, which passes every configuration silently.
/// </remarks>
public sealed class SpamJunkFolderValidatorTests
{
    [Fact]
    public void Validate_AnAccountMappingNoJunkFolder_FailsNamingTheAccount()
    {
        // Arrange
        var validator = new SpamJunkFolderValidator(Configuration(new Dictionary<string, string?>
        {
            ["MailSynchronization:Accounts:0:AccountId"] = "primary",
            ["MailSynchronization:Accounts:0:Folders:0:Alias"] = "inbox",
            ["MailSynchronization:Accounts:0:Folders:0:SpecialUse"] = "Inbox",
        }));

        // Act
        var result = validator.Validate(name: null, Filing());

        // Assert
        Assert.True(result.Failed);
        Assert.Contains("primary", Assert.Single(result.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AnAccountMappingAnUnmirroredJunkFolder_Succeeds()
    {
        // Arrange
        var validator = new SpamJunkFolderValidator(Configuration(new Dictionary<string, string?>
        {
            ["MailSynchronization:Accounts:0:AccountId"] = "primary",
            ["MailSynchronization:Accounts:0:Folders:0:Alias"] = "spam",
            ["MailSynchronization:Accounts:0:Folders:0:SpecialUse"] = "Junk",
            ["MailSynchronization:Accounts:0:Folders:0:Synchronize"] = "false",
        }));

        // Act
        var result = validator.Validate(name: null, Filing());

        // Assert
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_NoOptions_IsRefused()
    {
        // Arrange
        var validator = new SpamJunkFolderValidator(Configuration([]));

        // Act
        var refusal = () => validator.Validate(name: null, options: null!);

        // Assert
        Assert.Throws<ArgumentNullException>(refusal);
    }

    private static SpamClassificationOptions Filing() => new()
    {
        Enabled = true,
        Actions = new SpamActionOptions { FileInJunkFolder = true },
    };

    private static IConfiguration Configuration(Dictionary<string, string?> keys) =>
        new ConfigurationBuilder().AddInMemoryCollection(keys).Build();
}
