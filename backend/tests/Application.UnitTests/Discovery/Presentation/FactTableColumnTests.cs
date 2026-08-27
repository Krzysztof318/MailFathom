// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Discovery.Presentation.Blocks;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Presentation;

/// <summary>Covers the closed catalogue a fact table's columns come from.</summary>
public sealed class FactTableColumnTests
{
    [Fact]
    public void All_TheCatalogue_AllocatesEachIdentityOnce()
    {
        // Act
        var identities = FactTableColumn.All.Select(column => column.Identity).ToArray();

        // Assert
        Assert.Equal(identities.Length, identities.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The kind is why the set is a closed enumeration rather than an enum: it travels with the member.</summary>
    [Theory]
    [InlineData("amount", FactTableValueKind.Amount)]
    [InlineData("quantity", FactTableValueKind.Number)]
    [InlineData("date", FactTableValueKind.Date)]
    [InlineData("party", FactTableValueKind.Text)]
    public void ValueKind_ACataloguedColumn_SaysHowItsCellsAreDrawn(string identity, FactTableValueKind expected)
    {
        // Act
        var parsed = FactTableColumn.TryParse(identity, out var column);

        // Assert
        Assert.True(parsed);
        Assert.Equal(expected, column.ValueKind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("margin")]
    [InlineData("Amount")]
    public void TryParse_AnIdentityTheCatalogueDoesNotHold_ReturnsTheUnspecifiedDefault(string? identity)
    {
        // Act
        var parsed = FactTableColumn.TryParse(identity, out var column);

        // Assert
        Assert.False(parsed);
        Assert.False(column.IsSpecified);
    }

    [Fact]
    public void IsSpecified_TheStructDefault_ReportsItselfUnspecified()
    {
        // Arrange
        FactTableColumn unspecified = default;

        // Act, Assert
        Assert.False(unspecified.IsSpecified);
        Assert.Equal("(unspecified)", unspecified.ToString());
        Assert.Throws<InvalidOperationException>(() => unspecified.Identity);
    }

    [Fact]
    public void Serialization_ACataloguedColumn_RoundTripsAsItsIdentity()
    {
        // Act
        var json = JsonSerializer.Serialize(FactTableColumn.Amount);
        var read = JsonSerializer.Deserialize<FactTableColumn>(json);

        // Assert
        Assert.Equal("\"amount\"", json);
        Assert.Equal(FactTableColumn.Amount, read);
    }

    [Fact]
    public void Serialization_ACataloguedColumnAsAPropertyName_RoundTripsAsItsIdentity()
    {
        // Arrange
        var written = new Dictionary<FactTableColumn, int> { [FactTableColumn.Date] = 1 };

        // Act
        var json = JsonSerializer.Serialize(written);
        var read = JsonSerializer.Deserialize<Dictionary<FactTableColumn, int>>(json);

        // Assert
        Assert.Equal("{\"date\":1}", json);
        Assert.Equal(written, read);
    }

    [Theory]
    [InlineData("\"margin\"")]
    [InlineData("7")]
    public void Deserialization_AnIdentityTheCatalogueDoesNotHold_IsRefused(string json)
    {
        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FactTableColumn>(json));
    }

    [Fact]
    public void Serialization_TheStructDefault_IsRefused()
    {
        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize<FactTableColumn>(default));
    }
}
