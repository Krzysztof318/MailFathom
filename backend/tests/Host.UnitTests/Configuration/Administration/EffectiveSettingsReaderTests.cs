// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Administration;
using MailFathom.Host.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Administration;

/// <summary>
/// Covers what an operator is told about their own configuration. Every assertion is about the layer a value came from
/// rather than only the value, because that is the part the reading exists for: a write to a setting an override
/// supplies commits and changes nothing, and the source is what says so before the write rather than after it.
/// </summary>
public sealed class EffectiveSettingsReaderTests
{
    /// <summary>A value only the deployment's files supply is reported as coming from a file, named by the file.</summary>
    [Fact]
    public void Read_ASettingOnlyAFileSupplies_NamesTheFile()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: """{ "MailboxSearch": { "SnippetsPerEmail": "2" } }""",
            persisted: "{}");

        // Act
        var reading = deployment.Reader.Read("MailboxSearch");

        // Assert
        var setting = Assert.Single(reading.Settings);
        Assert.Equal("MailboxSearch:SnippetsPerEmail", setting.Path);
        Assert.Equal("2", setting.Value);
        Assert.Equal(SettingSource.File, setting.Source);
        Assert.Equal(ComposedConfigurationDeployment.ProvisionedFileName, setting.Origin);
    }

    /// <summary>The persisted layer beats the files beneath it, which is the precedence the layer was inserted at.</summary>
    [Fact]
    public void Read_ASettingThePersistedLayerAlsoCarries_NamesTheLayer()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: """{ "MailboxSearch": { "SnippetsPerEmail": "2" } }""",
            persisted: """{ "MailboxSearch": { "SnippetsPerEmail": "5" } }""");

        // Act
        var setting = Assert.Single(deployment.Reader.Read("MailboxSearch").Settings);

        // Assert
        Assert.Equal("5", setting.Value);
        Assert.Equal(SettingSource.PersistedLayer, setting.Source);
        Assert.Null(setting.Origin);
    }

    /// <summary>A source above the layer beats it, and is what makes a write to that setting change nothing.</summary>
    [Fact]
    public void Read_ASettingAnOperatorOverrideSupplies_NamesTheOverride()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: "{}",
            persisted: """{ "MailboxSearch": { "SnippetsPerEmail": "5" } }""",
            operatorOverride: """{ "MailboxSearch": { "SnippetsPerEmail": "9" } }""");

        // Act
        var setting = Assert.Single(deployment.Reader.Read("MailboxSearch").Settings);

        // Assert
        Assert.Equal("9", setting.Value);
        Assert.Equal(SettingSource.UserSecrets, setting.Source);
    }

    /// <summary>What outranks the layer is what a write has to be told about, and nothing beneath it is.</summary>
    [Fact]
    public void ShadowOver_ASettingAnOverrideSupplies_ReportsIt()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: """{ "MailboxSearch": { "WordsPerSnippet": "12" } }""",
            persisted: "{}",
            operatorOverride: """{ "MailboxSearch": { "SnippetsPerEmail": "9" } }""");

        // Act
        var shadowed = deployment.Reader.ShadowOver("MailboxSearch:SnippetsPerEmail");
        var unshadowed = deployment.Reader.ShadowOver("MailboxSearch:WordsPerSnippet");

        // Assert
        Assert.NotNull(shadowed);
        Assert.Equal(SettingSource.UserSecrets, shadowed.Source);
        Assert.Null(unshadowed);
    }

    /// <summary>
    /// What the files supply is a different question from what the deployment reads, and it is the one an adoption
    /// answers: the value an override currently beats is still the value the files decide.
    /// </summary>
    [Fact]
    public void ReadBeneathTheLayer_ASettingAnOverrideBeats_ReportsWhatTheFilesSupply()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: """{ "MailboxSearch": { "SnippetsPerEmail": "2" } }""",
            persisted: """{ "MailboxSearch": { "SnippetsPerEmail": "5" } }""",
            operatorOverride: """{ "MailboxSearch": { "SnippetsPerEmail": "9" } }""");

        // Act
        var setting = Assert.Single(deployment.Reader.ReadBeneathTheLayer("MailboxSearch").Settings);

        // Assert
        Assert.Equal("2", setting.Value);
        Assert.Equal(SettingSource.File, setting.Source);
        Assert.Equal("2", deployment.Reader.ValueBeneathTheLayer("MailboxSearch:SnippetsPerEmail"));
    }

    /// <summary>A secret-bearing setting reports the marker whichever reading asked for it.</summary>
    [Fact]
    public void Read_ASecretBearingSetting_ReportsTheMarker()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: """{ "Ai": { "Providers": { "0": { "ApiKey": "file:/run/secrets/model" } } } }""",
            persisted: "{}");

        // Act
        var setting = Assert.Single(deployment.Reader.Read("Ai:Providers:0:ApiKey").Settings);

        // Assert
        Assert.Equal(SettingRedaction.Marker, setting.Value);
        Assert.True(setting.IsRedacted);
    }

    /// <summary>A path is read as a path rather than as a string, so a section reports the settings beneath it.</summary>
    [Fact]
    public void Read_ASection_ReportsEverythingBeneathIt()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: """{ "MailboxSearch": { "SnippetsPerEmail": "2", "WordsPerSnippet": "12" }, "Deployment": { "PublicBaseAddress": "https://mail.example/" } }""",
            persisted: "{}");

        // Act
        var reading = deployment.Reader.Read("MailboxSearch");

        // Assert
        Assert.Equal(
            ["MailboxSearch:SnippetsPerEmail", "MailboxSearch:WordsPerSnippet"],
            reading.Settings.Select(setting => setting.Path));
    }

    /// <summary>The version reported beside a reading is the one this process composed itself over.</summary>
    [Fact]
    public void ComposedVersion_ReportsTheVersionTheLayerWasBuiltFrom()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(provisioned: "{}", persisted: "{}", version: 7);

        // Act & Assert
        Assert.Equal(7, deployment.Reader.ComposedVersion);
    }

    /// <summary>Removing a setting the layer does not carry changes nothing, which is what the layer is asked before a write.</summary>
    [Fact]
    public void LayerCarries_ASettingOnlyAFileSupplies_ReportsFalse()
    {
        // Arrange
        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: """{ "MailboxSearch": { "SnippetsPerEmail": "2" } }""",
            persisted: """{ "Deployment": { "PublicBaseAddress": "https://mail.example/" } }""");

        // Act & Assert
        Assert.False(deployment.Reader.LayerCarries("MailboxSearch:SnippetsPerEmail"));
        Assert.True(deployment.Reader.LayerCarries("Deployment"));
    }

    /// <summary>
    /// A reading refuses a prefix past its own bound rather than answering with the deployment's whole configuration.
    /// Nothing else in the change reaches the bound, so an inverted comparison here would let one request serialize
    /// every setting a deployment composed and no test would report it.
    /// </summary>
    [Fact]
    public void Read_APrefixMatchingMoreSettingsThanTheBound_RefusesAndReportsHowManyItMatched()
    {
        // Arrange
        var beyondTheBound = EffectiveSettingsReader.MaximumSettings + 1;

        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: SectionOf("Wide", beyondTheBound),
            persisted: "{}");

        // Act
        var reading = deployment.Reader.Read("Wide");

        // Assert
        Assert.True(reading.IsTooBroad);
        Assert.Equal(beyondTheBound, reading.MatchedCount);
        Assert.Empty(reading.Settings);
    }

    /// <summary>
    /// The adoption bound is measured against what the reading answers with rather than against the paths it started
    /// from, because a source above the layer supplies settings an adoption would never copy — and the refusal would
    /// then report that number back to the operator as what their files supply.
    /// </summary>
    [Fact]
    public void ReadBeneathTheLayer_APrefixTheLayerDecidesMostOf_MeasuresTheBoundAgainstWhatTheFilesSupply()
    {
        // Arrange
        var beyondTheBound = EffectiveSettingsReader.MaximumSettings + 1;

        using var deployment = ComposedConfigurationDeployment.Composed(
            provisioned: """{ "Wide": { "0": "supplied" } }""",
            persisted: SectionOf("Wide", beyondTheBound));

        // Act
        var reading = deployment.Reader.ReadBeneathTheLayer("Wide");

        // Assert
        Assert.False(reading.IsTooBroad);
        Assert.Equal("Wide:0", Assert.Single(reading.Settings).Path);
    }

    /// <summary>Composes one section holding the stated number of settings, each a value of its own.</summary>
    private static string SectionOf(string section, int settings)
    {
        var written = string.Join(
            ", ",
            Enumerable.Range(0, settings).Select(position => $"\"{position}\": \"value\""));

        return $$"""{ "{{section}}": { {{written}} } }""";
    }
}
