// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Rendering;
using Xunit;

namespace MailFathom.Application.UnitTests.EmailContent.Rendering;

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
        var representation = EmailBodyRepresentation.Bounded(body, AllowanceOf(100));

        // Assert
        Assert.Equal(body, representation.Text);
        Assert.Equal(body.Length, representation.OriginalCharacterCount);
        Assert.Equal(EmailBodyTruncation.None, representation.Truncation);
        Assert.False(representation.WasTruncated);
    }

    /// <summary>A body of exactly the bound is complete, which is the boundary the truncation state turns on after.</summary>
    [Fact]
    public void Bounded_BodyOfExactlyTheBound_KeepsItAndReportsNoTruncation()
    {
        // Arrange
        var body = new string('a', 64);

        // Act
        var representation = EmailBodyRepresentation.Bounded(body, AllowanceOf(64));

        // Assert
        Assert.Equal(body, representation.Text);
        Assert.Equal(EmailBodyTruncation.None, representation.Truncation);
    }

    /// <summary>One character past the bound is truncation, and the original length is what the caller needs to know.</summary>
    [Fact]
    public void Bounded_BodyOneCharacterBeyondTheBound_CutsItAndReportsTheOriginalLength()
    {
        // Arrange
        var body = new string('a', 65);

        // Act
        var representation = EmailBodyRepresentation.Bounded(body, AllowanceOf(64));

        // Assert
        Assert.Equal(64, representation.Text.Length);
        Assert.Equal(65, representation.OriginalCharacterCount);
        Assert.True(representation.WasTruncated);
    }

    /// <summary>Which limit cut a body is the caller's next decision, so the allowance's own answer is what is reported.</summary>
    [Theory]
    [InlineData(EmailBodyTruncation.BodyCharacterLimit)]
    [InlineData(EmailBodyTruncation.ReadCharacterBudget)]
    public void Bounded_BodyBeyondTheBound_NamesTheBoundTheAllowanceCarried(EmailBodyTruncation truncationWhenCut)
    {
        // Arrange
        var body = new string('a', 65);

        // Act
        var representation = EmailBodyRepresentation.Bounded(
            body,
            new EmailBodyCharacterAllowance(64, truncationWhenCut));

        // Assert
        Assert.Equal(truncationWhenCut, representation.Truncation);
    }

    /// <summary>An email reached after the read's budget ran out returns nothing and says which limit emptied it.</summary>
    [Fact]
    public void Bounded_AllowanceOfNoCharacters_ReturnsAnEmptyTextThatStatesTheBoundThatEmptiedIt()
    {
        // Arrange
        const string body = "The whole message.";

        // Act
        var representation = EmailBodyRepresentation.Bounded(
            body,
            new EmailBodyCharacterAllowance(0, EmailBodyTruncation.ReadCharacterBudget));

        // Assert
        Assert.Equal(string.Empty, representation.Text);
        Assert.Equal(body.Length, representation.OriginalCharacterCount);
        Assert.Equal(EmailBodyTruncation.ReadCharacterBudget, representation.Truncation);
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
        var representation = EmailBodyRepresentation.Bounded(body, AllowanceOf(body.Length - 1));

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
        Assert.Equal(EmailBodyTruncation.None, representation.Truncation);
    }

    private static EmailBodyCharacterAllowance AllowanceOf(int maxCharacters) =>
        new(maxCharacters, EmailBodyTruncation.BodyCharacterLimit);
}
