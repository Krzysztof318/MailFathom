// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval;
using Xunit;

namespace MailFathom.Application.UnitTests.Retrieval;

/// <summary>Covers what a question has to be before it can leave the process.</summary>
public sealed class MailQuestionTextTests
{
    [Fact]
    public void Create_OrdinaryText_KeepsItAsWritten()
    {
        // Act
        var question = MailQuestionText.Create("was the invoice attached");

        // Assert
        Assert.Equal("was the invoice attached", question.Value);
    }

    /// <summary>Surrounding whitespace is the caller's formatting rather than part of what they asked.</summary>
    [Fact]
    public void Create_TextWithSurroundingWhitespace_TrimsIt()
    {
        // Act
        var question = MailQuestionText.Create("  was the invoice attached\t");

        // Assert
        Assert.Equal("was the invoice attached", question.Value);
    }

    /// <summary>A run composed around no question would spend a provider call to be asked nothing.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_TextThatAsksNothing_IsRefused(string? text)
    {
        // Act
        var failure = Assert.Throws<MailboxQueryFilterInvalidException>(() => MailQuestionText.Create(text));

        // Assert
        Assert.Equal("question", failure.FilterName);
    }

    [Fact]
    public void Create_TextLongerThanOneQuestionCarries_IsRefused()
    {
        // Arrange
        var text = new string('a', MailQuestionText.MaximumLength + 1);

        // Act
        var failure = Assert.Throws<MailboxQueryFilterInvalidException>(() => MailQuestionText.Create(text));

        // Assert
        Assert.Equal("question", failure.FilterName);
    }

    [Fact]
    public void Create_TextOfExactlyTheGreatestLength_IsAccepted()
    {
        // Arrange
        var text = new string('a', MailQuestionText.MaximumLength);

        // Act
        var question = MailQuestionText.Create(text);

        // Assert
        Assert.Equal(MailQuestionText.MaximumLength, question.Value.Length);
    }

    [Fact]
    public void Create_TextCarryingAControlCharacter_IsRefused()
    {
        // Act
        var failure = Assert.Throws<MailboxQueryFilterInvalidException>(
            () => MailQuestionText.Create("was the invoice\0attached"));

        // Assert
        Assert.Equal("question", failure.FilterName);
    }

    /// <summary>A question is what somebody wants to know about their own mail, so nothing that renders it may repeat it.</summary>
    [Fact]
    public void ToString_AQuestion_ReportsItsLengthRatherThanItsText()
    {
        // Arrange
        var question = MailQuestionText.Create("what did the insurer agree to pay");

        // Act
        var rendered = question.ToString();

        // Assert
        Assert.DoesNotContain("insurer", rendered, StringComparison.Ordinal);
        Assert.Contains("33", rendered, StringComparison.Ordinal);
    }

    /// <summary>The refusal reaches a client, so it names the filter and never the text that was refused.</summary>
    [Fact]
    public void Create_RefusedText_IsNotRepeatedInTheFailureMessage()
    {
        // Act
        var failure = Assert.Throws<MailboxQueryFilterInvalidException>(
            () => MailQuestionText.Create(new string('a', MailQuestionText.MaximumLength + 1)));

        // Assert
        Assert.DoesNotContain("aaaa", failure.Message, StringComparison.Ordinal);
    }
}
