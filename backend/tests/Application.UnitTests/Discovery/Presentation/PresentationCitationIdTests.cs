// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Discovery.Presentation.Citations;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Presentation;

/// <summary>Covers the name a block points at a source by.</summary>
public sealed class PresentationCitationIdTests
{
    [Theory]
    [InlineData("c1")]
    [InlineData("source-12")]
    [InlineData("0")]
    public void Create_APermittedSpelling_KeepsIt(string candidate)
    {
        // Act
        var id = PresentationCitationId.Create(candidate);

        // Assert
        Assert.Equal(candidate, id.Value);
        Assert.Equal(candidate, id.ToString());
    }

    /// <summary>A name a renderer prints beside a fact is not a place to smuggle content through.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("C1")]
    [InlineData("c 1")]
    [InlineData("c_1")]
    [InlineData("<b>")]
    public void Create_ASpellingThatIsNotAName_IsRefused(string? candidate)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => PresentationCitationId.Create(candidate));
    }

    [Fact]
    public void Create_ANameLongerThanTheBound_IsRefused()
    {
        // Arrange
        var overlong = new string('c', PresentationCitationId.MaxLength + 1);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => PresentationCitationId.Create(overlong));
    }

    [Fact]
    public void IsSpecified_TheStructDefault_ReportsItselfUnspecified()
    {
        // Arrange
        PresentationCitationId unspecified = default;

        // Act, Assert
        Assert.False(unspecified.IsSpecified);
        Assert.Equal("(unspecified)", unspecified.ToString());
        Assert.Throws<InvalidOperationException>(() => unspecified.Value);
    }

    [Fact]
    public void Serialization_AnIdentifier_RoundTripsAsAPlainString()
    {
        // Arrange
        var id = PresentationCitationId.Create("c1");

        // Act
        var json = JsonSerializer.Serialize(id);
        var read = JsonSerializer.Deserialize<PresentationCitationId>(json);

        // Assert
        Assert.Equal("\"c1\"", json);
        Assert.Equal(id, read);
    }

    [Fact]
    public void Serialization_AnIdentifierPropertyName_RoundTripsAsAPlainString()
    {
        // Arrange
        var written = new Dictionary<PresentationCitationId, int> { [PresentationCitationId.Create("c1")] = 1 };

        // Act
        var json = JsonSerializer.Serialize(written);
        var read = JsonSerializer.Deserialize<Dictionary<PresentationCitationId, int>>(json);

        // Assert
        Assert.Equal("{\"c1\":1}", json);
        Assert.Equal(written, read);
    }

    [Theory]
    [InlineData("\"C1\"")]
    [InlineData("7")]
    public void Deserialization_AValueThatDoesNotSpellAName_IsRefused(string json)
    {
        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PresentationCitationId>(json));
    }

    [Fact]
    public void Serialization_TheStructDefault_IsRefused()
    {
        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize<PresentationCitationId>(default));
    }
}
