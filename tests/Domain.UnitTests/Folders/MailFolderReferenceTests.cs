// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;
using Xunit;

namespace MailFathom.Domain.UnitTests.Folders;

/// <summary>Covers the one written form a folder is named in wherever a caller names one.</summary>
public sealed class MailFolderReferenceTests
{
    [Theory]
    [InlineData("inbox", "INBOX")]
    [InlineData("  Archive/2026  ", "ARCHIVE/2026")]
    public void Create_TextWithoutTheRoleScheme_NamesTheAliasItSpells(string written, string expected)
    {
        // Arrange, Act
        var reference = MailFolderReference.Create(written);

        // Assert
        Assert.Equal(MailFolderAlias.Create(expected), reference.Alias);
        Assert.Null(reference.Role);
        Assert.Equal(expected, reference.ToString());
    }

    [Theory]
    [InlineData("role:Junk")]
    [InlineData("ROLE:junk")]
    [InlineData("  role: Junk  ")]
    public void Create_TextBehindTheRoleScheme_NamesTheRoleItSpells(string written)
    {
        // Arrange, Act
        var reference = MailFolderReference.Create(written);

        // Assert
        Assert.Equal(MailFolderSpecialUse.Junk, reference.Role);
        Assert.Null(reference.Alias);
        Assert.Equal("role:Junk", reference.ToString());
    }

    /// <summary>Without the scheme the two would collide, and a deployment renaming its alias would silently start meaning the role.</summary>
    [Fact]
    public void Create_AnAliasSpelledLikeARole_NamesTheAliasRatherThanTheRole()
    {
        // Arrange, Act
        var reference = MailFolderReference.Create("Junk");

        // Assert
        Assert.Equal(MailFolderAlias.Create("JUNK"), reference.Alias);
        Assert.Null(reference.Role);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("in\tbox")]
    [InlineData("role:")]
    [InlineData("role:   ")]
    [InlineData("role:NotARole")]
    [InlineData("role:4")]
    public void Create_TextNamingNoFolder_ThrowsArgumentException(string written)
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>(() => MailFolderReference.Create(written));
    }

    [Fact]
    public void TryCreate_TextNamingNoFolder_ReportsTheUnspecifiedDefault()
    {
        // Arrange, Act
        var read = MailFolderReference.TryCreate("role:NotARole", out var reference);

        // Assert
        Assert.False(read);
        Assert.False(reference.IsSpecified);
    }

    [Fact]
    public void ToRole_RoleThatDoesNotExist_ThrowsArgumentOutOfRangeException()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => MailFolderReference.ToRole((MailFolderSpecialUse)99));
    }

    /// <summary>The struct default is reachable and names nothing, which every consumer refuses rather than reads.</summary>
    [Fact]
    public void IsSpecified_TheStructDefault_ReportsThatItNamesNoFolder()
    {
        // Arrange, Act
        var reference = default(MailFolderReference);

        // Assert
        Assert.False(reference.IsSpecified);
        Assert.Equal("(unspecified)", reference.ToString());
    }

    [Fact]
    public void ToAlias_TheSameAliasTwice_ProducesOneValue()
    {
        // Arrange, Act
        var first = MailFolderReference.ToAlias(MailFolderAlias.Create("inbox"));
        var second = MailFolderReference.Create("INBOX");

        // Assert
        Assert.Equal(first, second);
        Assert.True(first.IsSpecified);
    }
}
