// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.EmailContent;
using Xunit;

namespace MailMcp.Application.UnitTests;

/// <summary>Covers the bound one body representation is returned under, and what it reports about the cut.</summary>
public sealed class EmailBodyRepresentationTests
{
    /// <summary>A body that fits is returned whole and must not be reported as anything less.</summary>
    [Fact]
    public void Bounded_BodyShorterThanTheBound_KeepsItAndReportsNoTruncation()
    {
        // Arrange
        const string body = "The whole message.";

        // Act
        var representation = EmailBodyRepresentation.Bounded(body, maxCharacters: 100);

        // Assert
        Assert.Equal(body, representation.Text);
        Assert.Equal(body.Length, representation.OriginalCharacterCount);
        Assert.False(representation.WasTruncated);
    }

    /// <summary>A body of exactly the bound is complete, which is the boundary the truncation flag turns on after.</summary>
    [Fact]
    public void Bounded_BodyOfExactlyTheBound_KeepsItAndReportsNoTruncation()
    {
        // Arrange
        var body = new string('a', 64);

        // Act
        var representation = EmailBodyRepresentation.Bounded(body, maxCharacters: 64);

        // Assert
        Assert.Equal(body, representation.Text);
        Assert.False(representation.WasTruncated);
    }

    /// <summary>One character past the bound is truncation, and the original length is what the caller needs to know.</summary>
    [Fact]
    public void Bounded_BodyOneCharacterBeyondTheBound_CutsItAndReportsTheOriginalLength()
    {
        // Arrange
        var body = new string('a', 65);

        // Act
        var representation = EmailBodyRepresentation.Bounded(body, maxCharacters: 64);

        // Assert
        Assert.Equal(64, representation.Text.Length);
        Assert.Equal(65, representation.OriginalCharacterCount);
        Assert.True(representation.WasTruncated);
    }

    /// <summary>The cut falls between characters a reader sees, never through the middle of one.</summary>
    [Fact]
    public void Bounded_BodyEndingInASurrogatePairAtTheBound_CutsBeforeItRatherThanThroughIt()
    {
        // Arrange
        // A family emoji is several scalars joined by zero-width joiners, so any cut inside it leaves an unpaired
        // surrogate that a JSON writer replaces and PostgreSQL refuses.
        var body = "Report " + "👨‍👩‍👧‍👦";

        // Act
        var representation = EmailBodyRepresentation.Bounded(body, maxCharacters: body.Length - 1);

        // Assert
        Assert.Equal("Report ", representation.Text);
        Assert.True(representation.WasTruncated);
        Assert.DoesNotContain(representation.Text, character => char.IsSurrogate(character));
    }

    /// <summary>A body that displayed nothing is an empty representation rather than an absent one.</summary>
    [Fact]
    public void Empty_ABodyThatDisplayedNothing_IsCompleteRatherThanTruncated()
    {
        // Act
        var representation = EmailBodyRepresentation.Empty;

        // Assert
        Assert.Equal(string.Empty, representation.Text);
        Assert.Equal(0, representation.OriginalCharacterCount);
        Assert.False(representation.WasTruncated);
    }
}
