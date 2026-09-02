// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.SensitiveContent;
using MailFathom.Host.Configuration.SensitiveContent;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.SensitiveContent;

/// <summary>Covers that the section binds from configuration the way composition reads it.</summary>
/// <remarks>
/// <para>
/// Every other test around this section builds the options object in C#, which proves what the rules do and nothing
/// about whether an operator's file ever reaches them. Both switches are getter-only complex properties and the category
/// and suppression lists beneath them are getter-only collections, so a switch that bound as <see langword="false" /> or
/// a category list that stayed empty would leave the declaration rules, the catalog validator, and the plan mapper all
/// passing while the deployment ran with the scanner off, or with the scanner's defaults in place of the named list.
/// That is the same quiet failure the startup validation exists to prevent, one level lower down.
/// </para>
/// <para>
/// The section is bound strictly, exactly as composition binds it, so these tests also state what a misspelling does.
/// The composition root additionally reads the section a second way — through <c>Get</c>, before a container exists, to
/// decide whether anything is registered at all — and that read is covered here too, because a switch that reached the
/// options graph and not that decision would leave a configured scanner with nothing constructed behind it.
/// </para>
/// </remarks>
public sealed class SensitiveContentOptionsBindingTests
{
    [Fact]
    public void Bind_AConfiguredSection_ReadsBothSwitchesTheirCategoriesAndTheirSuppressions()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["SensitiveContent:Secrets:Enabled"] = "true",
            ["SensitiveContent:Secrets:Categories:0"] = "CloudKey",
            ["SensitiveContent:Secrets:Categories:1"] = "PrivateKey",
            ["SensitiveContent:Secrets:Suppressions:0:Category"] = "CloudKey",
            ["SensitiveContent:Secrets:Suppressions:0:Rule"] = "gcp-api-key",
            ["SensitiveContent:Pii:Enabled"] = "true",
            ["SensitiveContent:Pii:Categories:0"] = "PersonName",
            ["SensitiveContent:MaximumAnalyzedCharacters"] = "4096",
            ["SensitiveContent:ScanTimeout"] = "00:00:30",
            ["SensitiveContent:MaximumConcurrentScans"] = "2",
        });

        // Act
        var settings = Bind(configuration);

        // Assert
        Assert.True(settings.Secrets.Enabled);
        Assert.True(settings.Pii.Enabled);
        Assert.Equal(["CloudKey", "PrivateKey"], settings.Secrets.Categories);
        Assert.Equal(["PersonName"], settings.Pii.Categories);
        var suppression = Assert.Single(settings.Secrets.Suppressions);
        Assert.Equal("CloudKey", suppression.Category);
        Assert.Equal("gcp-api-key", suppression.Rule);
        Assert.Equal(4096, settings.MaximumAnalyzedCharacters);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.ScanTimeout);
        Assert.Equal(2, settings.MaximumConcurrentScans);
    }

    /// <summary>
    /// Switching a scanner on protects what is derived from now on and reaches nothing already indexed, so the key that
    /// spends a re-derivation over a whole mailbox is separate and defaults to off. A binder that read it as anything
    /// else would either start that spend without being asked or refuse to start it when it was.
    /// </summary>
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Bind_TheRebuildSwitch_ReadsWhatTheOperatorAskedFor(string configured, bool expected)
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["SensitiveContent:Secrets:Enabled"] = "true",
            ["SensitiveContent:RebuildStaleDerivedData"] = configured,
        });

        // Act
        var settings = Bind(configuration);

        // Assert
        Assert.Equal(expected, settings.RebuildStaleDerivedData);
    }

    /// <summary>Each switch carries its own lists, and one bound into the other would scan for something nobody named.</summary>
    [Fact]
    public void Bind_OneSwitchConfigured_LeavesTheOtherOffAndCarryingNothing()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["SensitiveContent:Secrets:Enabled"] = "true",
            ["SensitiveContent:Secrets:Categories:0"] = "CloudKey",
        });

        // Act
        var settings = Bind(configuration);

        // Assert
        Assert.True(settings.Secrets.Enabled);
        Assert.False(settings.Pii.Enabled);
        Assert.Equal(["CloudKey"], settings.Secrets.Categories);
        Assert.Empty(settings.Pii.Categories);
        Assert.Empty(settings.Secrets.Suppressions);
    }

    /// <summary>
    /// The analyzer block is where the personal-data scanner is told what to reach. A getter-only complex property like the
    /// two switches, so a block that bound as its defaults would leave a deployment probing nothing and refusing to start
    /// against an address the operator did state.
    /// </summary>
    [Fact]
    public void Bind_AConfiguredAnalyzer_ReadsItsAddressLanguagesAndConfidenceFloor()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["SensitiveContent:Pii:Enabled"] = "true",
            ["SensitiveContent:PersonalDataAnalyzer:Endpoint"] = "http://presidio-analyzer:3000",
            ["SensitiveContent:PersonalDataAnalyzer:Languages:0"] = "de",
            ["SensitiveContent:PersonalDataAnalyzer:Languages:1"] = "pl",
            ["SensitiveContent:PersonalDataAnalyzer:MinimumConfidence"] = "0.6",
        });

        // Act
        var settings = Bind(configuration);

        // Assert
        Assert.True(settings.Pii.Enabled);
        Assert.Equal("http://presidio-analyzer:3000", settings.PersonalDataAnalyzer.Endpoint);
        Assert.Equal(["de", "pl"], settings.PersonalDataAnalyzer.Languages);
        Assert.Equal(0.6, settings.PersonalDataAnalyzer.MinimumConfidence);
    }

    /// <summary>An operator stating only an address must keep the floor the product chose rather than fall to the type's zero.</summary>
    [Fact]
    public void Bind_AnAnalyzerAddressAlone_LeavesTheConfidenceFloorAtItsDefault()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["SensitiveContent:Pii:Enabled"] = "true",
            ["SensitiveContent:PersonalDataAnalyzer:Endpoint"] = "http://presidio-analyzer:3000",
        });

        // Act
        var settings = Bind(configuration);

        // Assert
        Assert.Equal(0.4, settings.PersonalDataAnalyzer.MinimumConfidence);
    }

    /// <summary>An operator narrowing one bound must not reset the other two, which a partially bound section otherwise would.</summary>
    [Fact]
    public void Bind_OneConfiguredBound_LeavesTheRemainingBoundsAtTheirDefaults()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["SensitiveContent:MaximumConcurrentScans"] = "1",
        });

        // Act
        var settings = Bind(configuration);

        // Assert
        var defaults = new SensitiveContentOptions();
        Assert.Equal(1, settings.MaximumConcurrentScans);
        Assert.Equal(defaults.MaximumAnalyzedCharacters, settings.MaximumAnalyzedCharacters);
        Assert.Equal(defaults.ScanTimeout, settings.ScanTimeout);
    }

    /// <summary>An absent section is the ordinary deployment, which scans nothing and has to bind rather than fail.</summary>
    [Fact]
    public void Bind_AnUnconfiguredDeployment_LeavesBothScannersOff()
    {
        // Act
        var settings = Bind(ConfigurationFrom([]));

        // Assert
        Assert.False(settings.Secrets.Enabled);
        Assert.False(settings.Pii.Enabled);
        Assert.Equal(new SensitiveContentOptions().MaximumAnalyzedCharacters, settings.MaximumAnalyzedCharacters);
    }

    /// <summary>
    /// A misspelling that bound quietly is the failure this whole section is written against: the scanner would run
    /// with its defaults, or not at all, while an operator read their own file as proof of the protection they named.
    /// </summary>
    [Theory]
    [InlineData("SensitiveContent:Secrets:Enabeld", "true")]
    [InlineData("SensitiveContent:Secret:Enabled", "true")]
    [InlineData("SensitiveContent:Pii:Enabeld", "true")]
    [InlineData("SensitiveContent:Secrets:Category:0", "CloudKey")]
    [InlineData("SensitiveContent:Secrets:Suppression:0:Rule", "gcp-api-key")]
    [InlineData("SensitiveContent:Secrets:Suppressions:0:Rules", "gcp-api-key")]
    [InlineData("SensitiveContent:MaximumAnalyzedCharacter", "4096")]
    [InlineData("SensitiveContent:ScanTimeouts", "00:00:30")]
    [InlineData("SensitiveContent:PersonalDataAnalyzer:Endpont", "http://presidio-analyzer:3000")]
    [InlineData("SensitiveContent:PersonalDataAnalyser:Endpoint", "http://presidio-analyzer:3000")]
    [InlineData("SensitiveContent:PersonalDataAnalyzer:MinimumConfidance", "0.6")]
    [InlineData("SensitiveContent:ScreenOutgoingMailsFor:0", "Secrets")]
    public void Bind_AnUnrecognizedKey_FailsRatherThanBeingIgnored(string key, string value)
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["SensitiveContent:Secrets:Enabled"] = "true",
            [key] = value,
        });

        // Act, Assert
        Assert.ThrowsAny<InvalidOperationException>(() => Bind(configuration));
    }

    /// <summary>
    /// The composition root reads the section a second time, before a container exists, to decide which detectors are
    /// registered at all. An analyzer address that reached the options graph and not this read would validate at
    /// startup and then leave an owner switching that scanner on with nothing behind it.
    /// </summary>
    [Fact]
    public void Get_AConfiguredAnalyzer_ReachesTheDecisionCompositionRegistersOn()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["SensitiveContent:PersonalDataAnalyzer:Endpoint"] = "http://presidio-analyzer:3000",
        });

        // Act
        var settings = configuration.GetSection(SensitiveContentOptions.SectionName).Get<SensitiveContentOptions>();

        // Assert
        Assert.Equal(
            [SensitiveContentScannerKind.Secrets, SensitiveContentScannerKind.Pii],
            settings!.ProvidedScanners);
    }

    /// <summary>A deployment that deployed no analyzer still provides the scanner that runs inside this process.</summary>
    [Fact]
    public void Get_AnUnconfiguredDeployment_ProvidesTheSecretsScannerAlone()
    {
        // Act
        var settings = ConfigurationFrom([])
            .GetSection(SensitiveContentOptions.SectionName)
            .Get<SensitiveContentOptions>();

        // Assert
        Assert.Equal(
            [SensitiveContentScannerKind.Secrets],
            (settings ?? new SensitiveContentOptions()).ProvidedScanners);
    }

    /// <summary>The screened scanners are read from configuration in the order the operator wrote them.</summary>
    [Fact]
    public void Bind_ScreenOutgoingMailForNamingScanners_ReadsEveryEntry()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["SensitiveContent:Secrets:Enabled"] = "true",
            ["SensitiveContent:ScreenOutgoingMailFor:0"] = "Secrets",
            ["SensitiveContent:ScreenOutgoingMailFor:1"] = "Pii",
        });

        // Act
        var settings = Bind(configuration);

        // Assert
        Assert.NotNull(settings.ScreenOutgoingMailFor);
        Assert.Equal(["Secrets", "Pii"], settings.ScreenOutgoingMailFor);
    }

    /// <summary>
    /// An operator switching outgoing-mail screening off writes an empty array, and the key never written is what the
    /// default is read from. Only an array-typed property tells the two apart — a list would bind both to empty — so the
    /// empty array is asserted through a source that can express one.
    /// </summary>
    [Fact]
    public void Bind_ScreenOutgoingMailForWrittenAsAnEmptyArray_ReadsItAsNamingNothing()
    {
        // Arrange
        var configuration = JsonConfigurationFrom(
            """{ "SensitiveContent": { "Secrets": { "Enabled": true }, "ScreenOutgoingMailFor": [] } }""");

        // Act
        var settings = Bind(configuration);

        // Assert
        Assert.NotNull(settings.ScreenOutgoingMailFor);
        Assert.Empty(settings.ScreenOutgoingMailFor);
    }

    /// <summary>A deployment that never named the key takes the product's own default rather than screening nothing.</summary>
    [Fact]
    public void Bind_ADeploymentThatNeverNamedTheKey_LeavesItAbsent()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["SensitiveContent:Secrets:Enabled"] = "true",
        });

        // Act
        var settings = Bind(configuration);

        // Assert
        Assert.Null(settings.ScreenOutgoingMailFor);
    }

    /// <summary>Bound exactly as the composition root binds it, so a misspelling refused there is refused here.</summary>
    private static SensitiveContentOptions Bind(IConfiguration configuration)
    {
        var settings = new SensitiveContentOptions();

        configuration
            .GetSection(SensitiveContentOptions.SectionName)
            .Bind(settings, binderOptions => binderOptions.ErrorOnUnknownConfiguration = true);

        return settings;
    }

    private static IConfiguration ConfigurationFrom(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    /// <summary>Reads a section from JSON, which is the one source that can state a written empty array.</summary>
    private static IConfiguration JsonConfigurationFrom(string json)
    {
        using var document = new MemoryStream(Encoding.UTF8.GetBytes(json));

        return new ConfigurationBuilder()
            .AddJsonStream(document)
            .Build();
    }
}
