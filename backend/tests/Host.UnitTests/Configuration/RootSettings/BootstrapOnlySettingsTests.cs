// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.RootSettings;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.RootSettings;

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
    [InlineData("Persistence:Password:Reference")]
    [InlineData("Persistence:Password:Store")]
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
            ["Secrets:Interpretation", "Persistence:Password:Reference", "MailboxSearch:SnippetsPerEmail"]);

        // Assert
        Assert.Equal(["Persistence:Password", "Secrets:Interpretation"], refused);
    }

    /// <summary>An ordinary document carries none of them, which is what the refusal costs a correct deployment.</summary>
    [Fact]
    public void FindIn_OrdinarySettings_AreCarried()
    {
        // Act
        var refused = BootstrapOnlySettings.FindIn(
            ["MailboxSearch:SnippetsPerEmail", "Persistence:CommandTimeout", "Secrets:Files:0:Name"]);

        // Assert
        Assert.Empty(refused);
    }
}
