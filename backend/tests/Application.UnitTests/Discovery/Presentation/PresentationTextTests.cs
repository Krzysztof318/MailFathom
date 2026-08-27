// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Discovery.Presentation;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Presentation;

/// <summary>Covers the one kind of free text a plan may carry, and what it refuses to be.</summary>
public sealed class PresentationTextTests
{
    [Fact]
    public void Create_OrdinaryProse_KeepsItAsWritten()
    {
        // Act
        var text = PresentationText.Create("They accepted the revised figure on the second of March.");

        // Assert
        Assert.Equal("They accepted the revised figure on the second of March.", text.Value);
    }

    /// <summary>Surrounding whitespace is how a model formats rather than part of what it wrote.</summary>
    [Fact]
    public void Create_TextWithSurroundingWhitespace_TrimsIt()
    {
        // Act
        var text = PresentationText.Create("  they accepted\n");

        // Assert
        Assert.Equal("they accepted", text.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_TextThatSaysNothing_IsRefused(string? candidate)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => PresentationText.Create(candidate));
    }

    /// <summary>The bound is the plan's own: a block cites a message rather than reproducing one.</summary>
    [Fact]
    public void Create_TextLongerThanTheBound_IsRefused()
    {
        // Arrange
        var overlong = new string('a', PresentationText.MaxLength + 1);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => PresentationText.Create(overlong));
    }

    /// <summary>A plan reaches a renderer, a log, and a screen reader, and none of them agrees what a control character means.</summary>
    [Fact]
    public void Create_TextCarryingAControlCharacter_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => PresentationText.Create("they accepted\u0007"));
    }

    /// <summary>The whole point of the type: a value that is markup is not prose, whatever produced it.</summary>
    [Theory]
    [InlineData("<Grid><TextBlock Text=\"hello\" /></Grid>")]
    [InlineData("<p>they accepted</p>")]
    [InlineData("<svg viewBox=\"0 0 1 1\"></svg>")]
    public void Create_ValueThatIsMarkup_IsRefused(string markup)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => PresentationText.Create(markup));
    }

    /// <summary>Quoted mail legitimately mentions a bracket, and mangling a quotation to defend a renderer that evaluates nothing would be the worse trade.</summary>
    [Theory]
    [InlineData("the ticket is <PROJ-42> and it is closed")]
    [InlineData("he wrote \"a < b\" in the body")]
    public void Create_ProseMentioningABracket_IsKept(string prose)
    {
        // Act
        var text = PresentationText.Create(prose);

        // Assert
        Assert.Equal(prose, text.Value);
    }

    /// <summary>A struct's default is reachable and C# gives no way to forbid it, so it reports itself instead.</summary>
    [Fact]
    public void IsSpecified_TheStructDefault_ReportsItselfUnspecified()
    {
        // Arrange
        PresentationText unspecified = default;

        // Act, Assert
        Assert.False(unspecified.IsSpecified);
        Assert.Equal("(unspecified)", unspecified.ToString());
        Assert.Throws<InvalidOperationException>(() => unspecified.Value);
    }

    [Fact]
    public void Serialization_ATextValue_RoundTripsAsAPlainString()
    {
        // Arrange
        var text = PresentationText.Create("they accepted");

        // Act
        var json = JsonSerializer.Serialize(text);
        var read = JsonSerializer.Deserialize<PresentationText>(json);

        // Assert
        Assert.Equal("\"they accepted\"", json);
        Assert.Equal(text, read);
    }

    [Fact]
    public void Serialization_ATextPropertyName_RoundTripsAsAPlainString()
    {
        // Arrange
        var written = new Dictionary<PresentationText, int> { [PresentationText.Create("they accepted")] = 1 };

        // Act
        var json = JsonSerializer.Serialize(written);
        var read = JsonSerializer.Deserialize<Dictionary<PresentationText, int>>(json);

        // Assert
        Assert.Equal("{\"they accepted\":1}", json);
        Assert.Equal(written, read);
    }

    /// <summary>A plan read off the wire is held to the same rules as one composed in process.</summary>
    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"<p>markup</p>\"")]
    [InlineData("7")]
    public void Deserialization_AValueAPlanMayNotCarry_IsRefused(string json)
    {
        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PresentationText>(json));
    }

    /// <summary>A token no value of this length could reach is refused before anything decodes it into a string.</summary>
    [Fact]
    public void Deserialization_ATokenLongerThanAnyTextCouldBe_IsRefusedBeforeItIsRead()
    {
        // Arrange
        var json = JsonSerializer.Serialize(new string('a', (PresentationText.MaxLength * 6) + 1));

        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PresentationText>(json));
    }

    /// <summary>The ceiling is loose by construction, so a text that is entirely multi-octet still reads.</summary>
    [Fact]
    public void Deserialization_ATextOfMultiOctetCharactersWithinTheBound_IsRead()
    {
        // Arrange
        var json = JsonSerializer.Serialize(new string('ż', PresentationText.MaxLength));

        // Act
        var read = JsonSerializer.Deserialize<PresentationText>(json);

        // Assert
        Assert.Equal(PresentationText.MaxLength, read.Value.Length);
    }

    [Fact]
    public void Serialization_TheStructDefault_IsRefused()
    {
        // Act, Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize<PresentationText>(default));
    }
}
