// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Configuration;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.RootSettings;
using MailFathom.Infrastructure.Persistence.Settings;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration;

/// <summary>
/// Covers what a write does to the document itself. The assertions read the keys the candidate flattens to rather than
/// the JSON text, because what the document is for is the configuration it contributes and two spellings of one
/// setting are the same contribution.
/// </summary>
public sealed class SettingsDocumentPatchTests
{
    /// <summary>A setting the document did not carry is added at the path the write named.</summary>
    [Fact]
    public void Apply_ASettingTheDocumentDoesNotCarry_AddsIt()
    {
        // Arrange
        var edits = new[] { ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "3") };

        // Act
        var candidate = SettingsDocumentPatch.Apply("""{ "Deployment": { "PublicBaseAddress": "https://mail.example" } }""", edits);

        // Assert
        Assert.Equal("3", Flatten(candidate)["MailboxSearch:SnippetsPerEmail"]);
        Assert.Equal("https://mail.example", Flatten(candidate)["Deployment:PublicBaseAddress"]);
    }

    /// <summary>Every value is written as a JSON string, because a configuration value is a string to every provider that reads one.</summary>
    [Fact]
    public void Apply_AValueThatLooksLikeANumber_WritesItAsAConfigurationValue()
    {
        // Arrange
        var edits = new[] { ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "3") };

        // Act
        var candidate = SettingsDocumentPatch.Apply("{}", edits);

        // Assert
        Assert.Contains("\"3\"", candidate, StringComparison.Ordinal);
    }

    /// <summary>A setting already carried is replaced rather than duplicated.</summary>
    [Fact]
    public void Apply_ASettingTheDocumentCarries_ReplacesIt()
    {
        // Arrange
        var edits = new[] { ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "5") };

        // Act
        var candidate = SettingsDocumentPatch.Apply("""{ "MailboxSearch": { "SnippetsPerEmail": "3" } }""", edits);

        // Assert
        Assert.Equal("5", Flatten(candidate)["MailboxSearch:SnippetsPerEmail"]);
    }

    /// <summary>
    /// A path is matched the way every provider in the pipeline compares keys, so a write spelled differently reaches
    /// the setting the document already carries instead of adding a second spelling of it — which is a document the
    /// layer refuses outright.
    /// </summary>
    [Fact]
    public void Apply_APathSpelledDifferently_ReachesTheSettingTheDocumentCarries()
    {
        // Arrange
        var edits = new[] { ConfigurationEdit.SetTo("mailboxsearch:snippetsperemail", "5") };

        // Act
        var candidate = SettingsDocumentPatch.Apply("""{ "MailboxSearch": { "SnippetsPerEmail": "3" } }""", edits);

        // Assert
        Assert.Equal("5", Assert.Single(Flatten(candidate)).Value);
    }

    /// <summary>
    /// An indexed element is reached through its own position, and the position is written as a property name because
    /// the parser renumbers a JSON array's elements from zero.
    /// </summary>
    [Fact]
    public void Apply_AnIndexedElement_WritesThePositionAsAPropertyName()
    {
        // Arrange
        var edits = new[] { ConfigurationEdit.SetTo("MailRules:Rules:1:Name", "second") };

        // Act
        var candidate = SettingsDocumentPatch.Apply("{}", edits);

        // Assert
        Assert.Equal("second", Flatten(candidate)["MailRules:Rules:1:Name"]);
        Assert.Contains("\"1\"", candidate, StringComparison.Ordinal);
    }

    /// <summary>
    /// An array the write walks through becomes the object of the same keys, which contributes exactly the
    /// configuration the array did and leaves the elements beside the written one where they were.
    /// </summary>
    [Fact]
    public void Apply_AnArrayOnThePath_KeepsEveryElementAtItsOwnPosition()
    {
        // Arrange
        var edits = new[] { ConfigurationEdit.SetTo("MailRules:Rules:1:Name", "rewritten") };

        // Act
        var candidate = SettingsDocumentPatch.Apply(
            """{ "MailRules": { "Rules": [ { "Name": "first" }, { "Name": "second" } ] } }""",
            edits);

        // Assert
        var keys = Flatten(candidate);
        Assert.Equal("first", keys["MailRules:Rules:0:Name"]);
        Assert.Equal("rewritten", keys["MailRules:Rules:1:Name"]);
    }

    /// <summary>
    /// A removal reaching through an array removes the setting rather than stopping at the array, and leaves the
    /// elements beside it where they were. Without the conversion the walk would end at the array and the write would
    /// still commit, telling an operator a setting had stopped being persisted while it was still there.
    /// </summary>
    [Fact]
    public void Apply_ARemovalBeneathAnArrayElement_RemovesTheSettingAndKeepsItsSiblings()
    {
        // Arrange
        var edits = new[] { ConfigurationEdit.Removing("MailRules:Rules:1:Name") };

        // Act
        var candidate = SettingsDocumentPatch.Apply(
            """{ "MailRules": { "Rules": [ { "Name": "first" }, { "Name": "second", "Enabled": "false" } ] } }""",
            edits);

        // Assert
        var keys = Flatten(candidate);
        Assert.False(keys.ContainsKey("MailRules:Rules:1:Name"));
        Assert.Equal("false", keys["MailRules:Rules:1:Enabled"]);
        Assert.Equal("first", keys["MailRules:Rules:0:Name"]);
    }

    /// <summary>A value met where the path continues is replaced, because JSON holds no value and children under one name.</summary>
    [Fact]
    public void Apply_AValueWhereThePathContinues_ReplacesItWithTheSection()
    {
        // Arrange
        var edits = new[] { ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "3") };

        // Act
        var candidate = SettingsDocumentPatch.Apply("""{ "MailboxSearch": "whatever" }""", edits);

        // Assert
        Assert.Equal("3", Assert.Single(Flatten(candidate)).Value);
    }

    /// <summary>A removal drops the setting, so the source beneath the layer supplies it again.</summary>
    [Fact]
    public void Apply_ARemoval_DropsTheSetting()
    {
        // Arrange
        var edits = new[] { ConfigurationEdit.Removing("MailboxSearch:SnippetsPerEmail") };

        // Act
        var candidate = SettingsDocumentPatch.Apply(
            """{ "MailboxSearch": { "SnippetsPerEmail": "3", "MaximumResults": "20" } }""",
            edits);

        // Assert
        var keys = Flatten(candidate);
        Assert.False(keys.ContainsKey("MailboxSearch:SnippetsPerEmail"));
        Assert.Equal("20", keys["MailboxSearch:MaximumResults"]);
    }

    /// <summary>
    /// A removal that empties a section drops the section too, so the document does not go on describing settings the
    /// deployment no longer persists.
    /// </summary>
    [Fact]
    public void Apply_ARemovalThatEmptiesASection_DropsTheSectionWithIt()
    {
        // Arrange
        var edits = new[] { ConfigurationEdit.Removing("MailboxSearch:SnippetsPerEmail") };

        // Act
        var candidate = SettingsDocumentPatch.Apply("""{ "MailboxSearch": { "SnippetsPerEmail": "3" } }""", edits);

        // Assert
        Assert.Empty(Flatten(candidate));
        Assert.Equal("{}", candidate);
    }

    /// <summary>Removing a setting the document does not carry changes nothing rather than failing.</summary>
    [Fact]
    public void Apply_ARemovalOfASettingTheDocumentDoesNotCarry_ChangesNothing()
    {
        // Arrange
        var edits = new[] { ConfigurationEdit.Removing("MailboxSearch:SnippetsPerEmail") };

        // Act
        var candidate = SettingsDocumentPatch.Apply("""{ "Deployment": { "PublicBaseAddress": "https://mail.example" } }""", edits);

        // Assert
        Assert.Equal("https://mail.example", Assert.Single(Flatten(candidate)).Value);
    }

    /// <summary>Changes are applied in the order they were given, so the last word on one setting is the one that stands.</summary>
    [Fact]
    public void Apply_SeveralChangesToOneSetting_AppliesThemInOrder()
    {
        // Arrange
        var edits = new[]
        {
            ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "3"),
            ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "5"),
        };

        // Act
        var candidate = SettingsDocumentPatch.Apply("{}", edits);

        // Assert
        Assert.Equal("5", Flatten(candidate)["MailboxSearch:SnippetsPerEmail"]);
    }

    /// <summary>A document that is not a JSON object of configuration keys has nothing for a write to change.</summary>
    [Fact]
    public void Apply_ADocumentThatIsNotAConfigurationObject_IsRefused()
    {
        // Arrange
        var edits = new[] { ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "3") };

        // Act & Assert
        Assert.Throws<FormatException>(() => SettingsDocumentPatch.Apply("\"not settings\"", edits));
    }

    /// <summary>A document that is not JSON at all reaches the caller as the parser's own failure.</summary>
    [Fact]
    public void Apply_ADocumentThatIsNotJson_IsRefused()
    {
        // Arrange
        var edits = new[] { ConfigurationEdit.SetTo("MailboxSearch:SnippetsPerEmail", "3") };

        // Act & Assert
        Assert.ThrowsAny<JsonException>(() => SettingsDocumentPatch.Apply("{ not json", edits));
    }

    /// <summary>Reads the configuration keys a candidate document contributes, which is what the layer publishes from it.</summary>
    /// <remarks>
    /// Composed through the layer's own source rather than read out of the JSON, so what the assertions see is what a
    /// deployment would see: the framework's parser decides the keys, and a section that carries no value of its own
    /// is not one of them.
    /// </remarks>
    private static Dictionary<string, string?> Flatten(string json)
    {
        var source = new RootSettingsConfigurationSource(new RootSettingsDocument(json, Version: 1));

        using var composed = (ConfigurationRoot)new ConfigurationBuilder().Add(source).Build();

        return composed
            .AsEnumerable()
            .Where(setting => setting.Value is not null)
            .ToDictionary(setting => setting.Key, setting => setting.Value, StringComparer.OrdinalIgnoreCase);
    }
}
