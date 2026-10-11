// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using System.Text.Json;
using MailFathom.Host.Configuration.Policies;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Policies;

/// <summary>Covers the published names a read of a record answers with, and what refuses a value that is not one of them.</summary>
/// <remarks>
/// The name travels — it is what both record reads carry as <c>source</c> and what <c>mfctl user show --effective</c>
/// prints beside a setting — so the assertions here are about the identity rather than about the member. A rename that
/// changed a published name would pass every test written against members.
/// </remarks>
public sealed class SettingsValueSourceTests
{
    /// <summary>Two values sharing a name would be indistinguishable in every read that reports one.</summary>
    [Fact]
    public void All_NamesAreUnique()
    {
        // Act
        var distinct = SettingsValueSource.All.Select(value => value.Name).Distinct(StringComparer.Ordinal).Count();

        // Assert
        Assert.Equal(SettingsValueSource.All.Count, distinct);
    }

    /// <summary>
    /// A declared value left out of the registry is invisible to every other assertion here, and unresolvable:
    /// <see cref="SettingsValueSource.TryParse" /> reads the registry alone.
    /// </summary>
    [Fact]
    public void All_ListsEveryDeclaredValue()
    {
        // Arrange
        // Both accessibilities, so the guard keeps inspecting something when a member is later declared public.
        var declared = typeof(SettingsValueSource)
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(SettingsValueSource))
            .Select(property => (SettingsValueSource)property.GetValue(null)!)
            .ToArray();

        // Act
        var unregistered = declared.Where(value => !SettingsValueSource.All.Contains(value)).ToArray();

        // Assert
        Assert.NotEmpty(declared);
        Assert.Empty(unregistered);
    }

    /// <summary>
    /// The names are what a client matches on and what a script greps for, so they are asserted as the set rather
    /// than one at a time: a renamed member reads as one name replaced by another rather than as a member missing.
    /// </summary>
    [Fact]
    public void All_PublishesTheNamesAReadAnswersWith()
    {
        // Act
        var published = SettingsValueSource.All.Select(value => value.Name).ToArray();

        // Assert
        Assert.Equal(["DeploymentForced", "OrganizationForced", "Record", "OrganizationDefault", "DeploymentDefault", "BuiltIn"], published);
    }

    /// <summary>A forcing step knows whose policy forces, and every other step forces nothing.</summary>
    [Fact]
    public void ForcedBy_EachStep_NamesTheScopeForcingItAndNothingForTheRest()
    {
        // Act
        var forcing = SettingsValueSource.All
            .Where(source => source.ForcedBy is not null)
            .Select(source => (source.Name, Scope: source.ForcedBy!.Value.Name))
            .ToArray();

        // Assert
        Assert.Equal([("DeploymentForced", "Deployment"), ("OrganizationForced", "Organization")], forcing);
    }

    /// <summary>A name this build publishes resolves to the value that publishes it.</summary>
    [Fact]
    public void TryParse_ANameThisBuildPublishes_ResolvesTheValue()
    {
        // Act
        var resolved = SettingsValueSource.TryParse("OrganizationDefault", out var value);

        // Assert
        Assert.True(resolved);
        Assert.Equal(SettingsValueSource.OrganizationDefault, value);
    }

    /// <summary>A name nothing publishes is unknown rather than new, so nothing reconstructs a value from it.</summary>
    [Theory]
    [InlineData("organizationDefault")]
    [InlineData("Team")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_ANameNothingPublishes_ResolvesNothing(string? name)
    {
        // Act
        var resolved = SettingsValueSource.TryParse(name, out var value);

        // Assert
        Assert.False(resolved);
        Assert.False(value.IsSpecified);
    }

    /// <summary>The struct default is reachable in any C#, so it reports itself rather than pretending to be a value.</summary>
    [Fact]
    public void IsSpecified_TheStructDefault_ReportsItselfUnspecifiedAndRefusesItsName()
    {
        // Arrange
        SettingsValueSource unspecified = default;

        // Act & Assert
        Assert.False(unspecified.IsSpecified);
        Assert.Equal("(unspecified)", unspecified.ToString());
        Assert.Throws<InvalidOperationException>(() => unspecified.Name);
    }

    /// <summary>What reaches a log is the published name, which is the same string a read answers with.</summary>
    [Fact]
    public void ToString_AValue_ReadsAsItsPublishedName()
    {
        // Act & Assert
        Assert.Equal("OrganizationDefault", SettingsValueSource.OrganizationDefault.ToString());
    }

    /// <summary>
    /// The converter is what stops a serializer that ever met the value writing the struct as an object, so the round
    /// trip is asserted as a value and as a property name — the two places the framework asks a converter a different
    /// question.
    /// </summary>
    [Fact]
    public void JsonConverter_EveryValue_RoundTripsAsItsPublishedNameBothWays()
    {
        // Act
        var asValues = JsonSerializer.Deserialize<SettingsValueSource[]>(JsonSerializer.Serialize(SettingsValueSource.All));
        var asKeys = JsonSerializer.Deserialize<Dictionary<SettingsValueSource, int>>(
            JsonSerializer.Serialize(SettingsValueSource.All.Index().ToDictionary(
                value => value.Item,
                value => value.Index)));

        // Assert
        Assert.Equal(SettingsValueSource.All, asValues);
        Assert.Equal(SettingsValueSource.All, asKeys!.Keys);
    }

    /// <summary>A value is written as its name rather than as the struct's members, which is what the attribute buys.</summary>
    [Fact]
    public void JsonConverter_AValue_WritesThePublishedName()
    {
        // Act & Assert
        Assert.Equal("\"OrganizationDefault\"", JsonSerializer.Serialize(SettingsValueSource.OrganizationDefault));
    }

    /// <summary>A token that is not a string, and a string naming nothing, are both refused rather than defaulted.</summary>
    [Theory]
    [InlineData("7")]
    [InlineData("null")]
    [InlineData("\"Team\"")]
    public void JsonConverter_AValueNamingNothing_IsRefused(string json)
    {
        // Act & Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SettingsValueSource>(json));
    }

    /// <summary>The unspecified default names nothing, so writing one would publish a value nothing could read back.</summary>
    [Fact]
    public void JsonConverter_TheStructDefault_IsRefusedRatherThanWritten()
    {
        // Act & Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize<SettingsValueSource>(default));
    }
}
