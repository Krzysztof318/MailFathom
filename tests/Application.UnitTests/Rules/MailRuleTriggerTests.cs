// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using System.Text.Json;
using MailFathom.Application.Rules;
using Xunit;

namespace MailFathom.Application.UnitTests.Rules;

/// <summary>Covers the trigger vocabulary an operator writes and a rule set's identity is derived from.</summary>
public sealed class MailRuleTriggerTests
{
    /// <summary>Two triggers sharing a name would be indistinguishable in a configuration file and in a digest.</summary>
    [Fact]
    public void All_NamesAreUnique()
    {
        // Act
        var distinctNames = MailRuleTrigger.All
            .Select(trigger => trigger.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        // Assert
        Assert.Equal(MailRuleTrigger.All.Count, distinctNames);
    }

    /// <summary>A declared trigger left out of the registry could be written nowhere: parsing resolves through it alone.</summary>
    [Fact]
    public void All_ListsEveryDeclaredTrigger()
    {
        // Arrange
        var declaredTriggers = typeof(MailRuleTrigger)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(MailRuleTrigger))
            .Select(property => (MailRuleTrigger)property.GetValue(null)!);

        // Act
        var unregistered = declaredTriggers.Where(trigger => !MailRuleTrigger.All.Contains(trigger)).ToArray();

        // Assert
        Assert.Empty(unregistered);
    }

    [Theory]
    [InlineData("Arrival")]
    [InlineData("arrival")]
    [InlineData("  Arrival  ")]
    public void TryParseName_ADeclaredName_ProducesTheTrigger(string name)
    {
        // Act
        var parsed = MailRuleTrigger.TryParseName(name, out var trigger);

        // Assert
        Assert.True(parsed);
        Assert.Equal(MailRuleTrigger.Arrival, trigger);
        Assert.Equal("Arrival", trigger.Name);
    }

    /// <summary>A name this set does not hold is unknown rather than new, so nothing reconstructs a trigger from it.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Schedule")]
    [InlineData("OnDemand")]
    public void TryParseName_AnUndeclaredName_ProducesTheUnspecifiedDefault(string? name)
    {
        // Act
        var parsed = MailRuleTrigger.TryParseName(name, out var trigger);

        // Assert
        Assert.False(parsed);
        Assert.False(trigger.IsSpecified);
    }

    [Fact]
    public void Name_UnspecifiedDefault_IsRefusedRatherThanAnswered()
    {
        // Arrange
        var trigger = default(MailRuleTrigger);

        // Act, Assert
        Assert.False(trigger.IsSpecified);
        Assert.Throws<InvalidOperationException>(() => trigger.Name);
        Assert.Equal("(unspecified)", trigger.ToString());
    }

    [Fact]
    public void JsonConverter_ADeclaredTrigger_RoundTripsAsItsName()
    {
        // Act
        var written = JsonSerializer.Serialize(MailRuleTrigger.Arrival);

        // Assert
        Assert.Equal("\"Arrival\"", written);
        Assert.Equal(MailRuleTrigger.Arrival, JsonSerializer.Deserialize<MailRuleTrigger>(written));
    }

    [Fact]
    public void JsonConverter_ADeclaredTrigger_RoundTripsAsAPropertyName()
    {
        // Arrange
        var counts = new Dictionary<MailRuleTrigger, int> { [MailRuleTrigger.Arrival] = 3 };

        // Act
        var written = JsonSerializer.Serialize(counts);

        // Assert
        Assert.Equal("{\"Arrival\":3}", written);
        Assert.Equal(counts, JsonSerializer.Deserialize<Dictionary<MailRuleTrigger, int>>(written));
    }

    [Theory]
    [InlineData("\"Schedule\"")]
    [InlineData("7")]
    public void JsonConverter_AValueThatNamesNoTrigger_IsRefused(string json)
    {
        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MailRuleTrigger>(json));
    }

    [Fact]
    public void JsonConverter_TheUnspecifiedDefault_IsRefusedRatherThanSerialized()
    {
        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(default(MailRuleTrigger)));
        Assert.Throws<JsonException>(
            () => JsonSerializer.Serialize(new Dictionary<MailRuleTrigger, int> { [default] = 1 }));
    }
}
