// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using Xunit;

namespace MailFathom.Domain.UnitTests.Accounts;

public sealed class MailAccountDisplayNameTests
{
    /// <summary>The name exists to be read, so the operator's own casing survives and only padding is taken off.</summary>
    [Theory]
    [InlineData("Work mail", "Work mail")]
    [InlineData("  Work mail  ", "Work mail")]
    [InlineData("PRIVATE", "PRIVATE")]
    public void Create_ConfiguredName_KeepsTheCasingAndTrimsThePadding(string configured, string expected)
    {
        // Arrange, Act
        var displayName = MailAccountDisplayName.Create(configured);

        // Assert
        Assert.Equal(expected, displayName.Value);
    }

    /// <summary>Two spellings that differ in case are two values here, because the comparison belongs where a name is matched.</summary>
    [Fact]
    public void Create_NamesDifferingOnlyByCase_AreNotTheSameValue()
    {
        // Arrange, Act
        var written = MailAccountDisplayName.Create("Work mail");
        var recased = MailAccountDisplayName.Create("WORK MAIL");

        // Assert
        Assert.NotEqual(written, recased);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankName_ThrowsArgumentException(string configured)
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>(() => MailAccountDisplayName.Create(configured));
    }

    /// <summary>The value is published in every result naming its account, so a control character in it would write lines into both the contract and the log.</summary>
    [Theory]
    [InlineData("Work\nmail")]
    [InlineData("Work\tmail")]
    [InlineData("Work\u0007mail")]
    public void Create_NameCarryingAControlCharacter_ThrowsArgumentException(string configured)
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>(() => MailAccountDisplayName.Create(configured));
    }

    [Fact]
    public void Create_NameAtTheLengthBound_IsAccepted()
    {
        // Arrange
        var atTheBound = new string('m', MailAccountDisplayName.MaximumLength);

        // Act
        var displayName = MailAccountDisplayName.Create(atTheBound);

        // Assert
        Assert.Equal(MailAccountDisplayName.MaximumLength, displayName.Value.Length);
    }

    /// <summary>The bound is measured after trimming, so padding never costs an operator a name they could otherwise write.</summary>
    [Fact]
    public void Create_NamePaddedPastTheLengthBound_IsAcceptedOnItsTrimmedLength()
    {
        // Arrange
        var padded = $"  {new string('m', MailAccountDisplayName.MaximumLength)}  ";

        // Act
        var displayName = MailAccountDisplayName.Create(padded);

        // Assert
        Assert.Equal(MailAccountDisplayName.MaximumLength, displayName.Value.Length);
    }

    [Fact]
    public void Create_NameBeyondTheLengthBound_ThrowsArgumentException()
    {
        // Arrange
        var beyondTheBound = new string('m', MailAccountDisplayName.MaximumLength + 1);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => MailAccountDisplayName.Create(beyondTheBound));
    }

    [Fact]
    public void ToString_ConfiguredName_IsTheNameItself()
    {
        // Arrange, Act
        var displayName = MailAccountDisplayName.Create("Work mail");

        // Assert
        Assert.Equal("Work mail", displayName.ToString());
    }
}
