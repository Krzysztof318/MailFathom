// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Configuration;
using Xunit;

namespace MailFathom.Application.UnitTests.Configuration;

/// <summary>
/// Covers which settings the persisted layer refuses to carry. The list is what keeps the layer from supplying the
/// settings it was itself reached through, so what matters is that a key reaches the refusal however it was written.
/// </summary>
public sealed class BootstrapOnlySettingsTests
{
    /// <summary>Every key the operations page lists is refused, which is the whole of the claim that page makes.</summary>
    [Theory]
    [InlineData("ConnectionStrings:mailfathom")]
    [InlineData("Persistence:ConnectionString")]
    [InlineData("Persistence:Password")]
    [InlineData("Persistence:CommandTimeoutSeconds")]
    [InlineData("Secrets:Interpretation")]
    [InlineData("ConfigurationSources:Directory")]
    [InlineData("ConfigurationSources:File")]
    public void FindIn_KeyReadBeforeTheLayerExists_IsRefused(string persistedKey)
    {
        // Arrange
        string[] persistedKeys = ["MailboxSearch:SnippetsPerEmail", persistedKey];

        // Act
        var refused = BootstrapOnlySettings.FindIn(persistedKeys);

        // Assert
        Assert.Equal([persistedKey], refused);
    }

    /// <summary>
    /// A secret block is a section rather than a value, so the half that decides the credential is nested beneath the
    /// key. Refusing only the section's own key would admit exactly the part that matters.
    /// </summary>
    [Theory]
    [InlineData("Persistence:Password:SecretReference")]
    [InlineData("Persistence:Password:Lifetime")]
    public void FindIn_KeyNestedBeneathARefusedSection_IsRefused(string persistedKey)
    {
        // Act
        var refused = BootstrapOnlySettings.FindIn([persistedKey]);

        // Assert
        Assert.Equal(["Persistence:Password"], refused);
    }

    /// <summary>Configuration keys are compared case-insensitively everywhere else in the pipeline, and so are these.</summary>
    [Fact]
    public void FindIn_RefusedKeyInAnotherCasing_IsStillRefused()
    {
        // Act
        var refused = BootstrapOnlySettings.FindIn(["secrets:interpretation"]);

        // Assert
        Assert.Equal(["Secrets:Interpretation"], refused);
    }

    /// <summary>
    /// A key that merely begins with a refused one names a different setting, and refusing it would take a persistable
    /// setting away from an operator for the sake of a string comparison.
    /// </summary>
    [Theory]
    [InlineData("Persistence:ConnectionStringTimeout")]
    [InlineData("Persistence:PasswordRotationInterval")]
    [InlineData("ConfigurationSourcesReport:Directory")]
    public void FindIn_KeyMerelyBeginningWithARefusedOne_IsCarried(string persistedKey)
    {
        // Act
        var refused = BootstrapOnlySettings.FindIn([persistedKey]);

        // Assert
        Assert.Empty(refused);
    }

    /// <summary>A document carrying several is reported whole, so one repair answers all of them.</summary>
    [Fact]
    public void FindIn_SeveralRefusedKeys_ReportsEveryOne()
    {
        // Act
        var refused = BootstrapOnlySettings.FindIn(
            ["Secrets:Interpretation", "Persistence:Password:SecretReference", "MailboxSearch:SnippetsPerEmail"]);

        // Assert
        Assert.Equal(["Persistence:Password", "Secrets:Interpretation"], refused);
    }

    /// <summary>
    /// The prefix test runs one way only. A refused key beginning with a persisted one names a longer setting, so
    /// <c>Persistence:CommandTimeout</c> is not <c>Persistence:CommandTimeoutSeconds</c> and the shorter name stays a
    /// setting an operator may persist rather than being refused by the longer one it happens to be a prefix of.
    /// </summary>
    [Fact]
    public void FindIn_KeyARefusedOneMerelyBeginsWith_IsCarried()
    {
        // Act
        var refused = BootstrapOnlySettings.FindIn(["Persistence:CommandTimeout"]);

        // Assert
        Assert.Empty(refused);
    }

    /// <summary>An ordinary document carries none of them, which is what the refusal costs a correct deployment.</summary>
    [Fact]
    public void FindIn_OrdinarySettings_AreCarried()
    {
        // Act
        var refused = BootstrapOnlySettings.FindIn(
            ["MailboxSearch:SnippetsPerEmail", "Persistence:MaximumConcurrencyCommitAttempts", "Secrets:Files:0:Name"]);

        // Assert
        Assert.Empty(refused);
    }

    /// <summary>
    /// The same list decides what may be written, one path at a time rather than one document at a time, because a
    /// write arrives as the single path an administrator asked to change.
    /// </summary>
    [Theory]
    [InlineData("Persistence:Password", "Persistence:Password")]
    [InlineData("Persistence:Password:SecretReference", "Persistence:Password")]
    [InlineData("connectionstrings:mailfathom", "ConnectionStrings:mailfathom")]
    public void TryFindCovering_PathAWriteMayNotReach_ReportsTheRefusedSetting(string configurationPath, string expected)
    {
        // Act
        var refused = BootstrapOnlySettings.TryFindCovering(configurationPath, out var refusedSetting);

        // Assert
        Assert.True(refused);
        Assert.Equal(expected, refusedSetting);
    }

    /// <summary>An ordinary setting is covered by nothing here, which leaves the catalog free to route it.</summary>
    [Theory]
    [InlineData("MailboxSearch:SnippetsPerEmail")]
    [InlineData("Persistence:PasswordRotationInterval")]
    [InlineData("Persistence:CommandTimeout")]
    public void TryFindCovering_OrdinarySetting_ReportsNoRefusal(string configurationPath)
    {
        // Act
        var refused = BootstrapOnlySettings.TryFindCovering(configurationPath, out var refusedSetting);

        // Assert
        Assert.False(refused);
        Assert.Null(refusedSetting);
    }
}
