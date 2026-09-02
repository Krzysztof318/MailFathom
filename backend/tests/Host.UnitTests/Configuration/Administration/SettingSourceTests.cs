// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using System.Text.Json;
using MailFathom.Host.Configuration.Administration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Administration;

/// <summary>Covers the published names a reading answers with, and what refuses a value that is not one of them.</summary>
/// <remarks>
/// The name travels — it is what the administrative reading carries, what <c>mfctl config get</c> prints beside a
/// value, and what a refused write names as the source that beat it — so the assertions here are about the identity
/// rather than about the member. A rename that changed a published name would pass every test written against members.
/// </remarks>
public sealed class SettingSourceTests
{
    /// <summary>Two sources sharing a name would be indistinguishable in every reading that reports one.</summary>
    [Fact]
    public void All_NamesAreUnique()
    {
        // Act
        var distinct = SettingSource.All.Select(source => source.Name).Distinct(StringComparer.Ordinal).Count();

        // Assert
        Assert.Equal(SettingSource.All.Count, distinct);
    }

    /// <summary>
    /// A declared source left out of the registry is invisible to every other assertion here, and unresolvable:
    /// <see cref="SettingSource.TryParse" /> reads the registry alone, so a reading would publish a name nothing
    /// could parse back.
    /// </summary>
    [Fact]
    public void All_ListsEveryDeclaredSource()
    {
        // Arrange
        // Both accessibilities, so the guard keeps inspecting something when a member is later declared public: a
        // query matching only today's accessibility would leave the sequence empty and Assert.Empty passing over it.
        var declared = typeof(SettingSource)
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(SettingSource))
            .Select(property => (SettingSource)property.GetValue(null)!);

        // Act
        var unregistered = declared.Where(source => !SettingSource.All.Contains(source)).ToArray();

        // Assert
        Assert.Empty(unregistered);
    }

    /// <summary>
    /// The names are what an operator matches on and what a script greps for, so they are asserted as the set rather
    /// than one at a time: a renamed member reads as one name replaced by another rather than as a member missing.
    /// </summary>
    [Fact]
    public void All_PublishesTheNamesAReadingAnswersWith()
    {
        // Act
        var published = SettingSource.All.Select(source => source.Name).ToArray();

        // Assert
        Assert.Equal(
            ["command-line", "environment-variable", "user-secrets", "persisted-layer", "file", "unclassified"],
            published);
    }

    /// <summary>A name this build publishes resolves to the source that publishes it.</summary>
    [Fact]
    public void TryParse_ANameThisBuildPublishes_ResolvesTheSource()
    {
        // Act
        var resolved = SettingSource.TryParse("persisted-layer", out var source);

        // Assert
        Assert.True(resolved);
        Assert.Equal(SettingSource.PersistedLayer, source);
    }

    /// <summary>A name nothing publishes is unknown rather than new, so nothing reconstructs a source from it.</summary>
    [Theory]
    [InlineData("Persisted-Layer")]
    [InlineData("database")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_ANameNothingPublishes_ResolvesNothing(string? name)
    {
        // Act
        var resolved = SettingSource.TryParse(name, out var source);

        // Assert
        Assert.False(resolved);
        Assert.False(source.IsSpecified);
    }

    /// <summary>The struct default is reachable in any C#, so it reports itself rather than pretending to be a source.</summary>
    [Fact]
    public void IsSpecified_TheStructDefault_ReportsItselfUnspecifiedAndRefusesItsName()
    {
        // Arrange
        SettingSource unspecified = default;

        // Act & Assert
        Assert.False(unspecified.IsSpecified);
        Assert.Equal("(unspecified)", unspecified.ToString());
        Assert.Throws<InvalidOperationException>(() => unspecified.Name);
    }

    /// <summary>What reaches a log is the published name, which is the same string a reading answers with.</summary>
    [Fact]
    public void ToString_ASource_ReadsAsItsPublishedName()
    {
        // Act & Assert
        Assert.Equal("environment-variable", SettingSource.EnvironmentVariable.ToString());
    }

    /// <summary>
    /// The converter is what stops a serializer that ever met the value writing the struct as an empty object, so the
    /// round trip is asserted as a value and as a property name — the two places the framework asks a converter a
    /// different question.
    /// </summary>
    [Fact]
    public void JsonConverter_EverySource_RoundTripsAsItsPublishedNameBothWays()
    {
        // Act
        var asValues = JsonSerializer.Deserialize<SettingSource[]>(JsonSerializer.Serialize(SettingSource.All));
        var asKeys = JsonSerializer.Deserialize<Dictionary<SettingSource, int>>(
            JsonSerializer.Serialize(SettingSource.All.Index().ToDictionary(
                source => source.Item,
                source => source.Index)));

        // Assert
        Assert.Equal(SettingSource.All, asValues);
        Assert.Equal(SettingSource.All, asKeys!.Keys);
    }

    /// <summary>A source is written as its name rather than as the struct's members, which is what the attribute buys.</summary>
    [Fact]
    public void JsonConverter_ASource_WritesThePublishedName()
    {
        // Act & Assert
        Assert.Equal("\"user-secrets\"", JsonSerializer.Serialize(SettingSource.UserSecrets));
    }

    /// <summary>A token that is not a string, and a string naming nothing, are both refused rather than defaulted.</summary>
    [Theory]
    [InlineData("7")]
    [InlineData("null")]
    [InlineData("\"database\"")]
    public void JsonConverter_AValueNamingNoSource_IsRefused(string json)
    {
        // Act & Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SettingSource>(json));
    }

    /// <summary>The unspecified default names no source, so writing one would publish a value nothing could read back.</summary>
    [Fact]
    public void JsonConverter_TheStructDefault_IsRefusedRatherThanWritten()
    {
        // Act & Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize<SettingSource>(default));
    }
}
