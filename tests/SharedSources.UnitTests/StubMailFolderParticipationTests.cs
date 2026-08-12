// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the participation reader every suite arranging a withheld folder answers through.</summary>
/// <remarks>
/// A fault here reports somebody else's arrangement. A reader that named a folder in the wrong list would let a test
/// about tool visibility pass on an exclusion a rule walk applies, and one whose derived answer disagreed with its own
/// lists would prove a narrowing that configuration never produces.
/// </remarks>
public sealed class StubMailFolderParticipationTests
{
    private static readonly MailAccountId Work = MailAccountId.Create("work");

    private static readonly MailFolderIdentity Archive =
        new(Work, MailFolderAlias.Create("archive"));

    /// <summary>The supply an existing arrangement takes, which has to keep saying what it said before folder switches existed.</summary>
    [Fact]
    public void Everything_AFolderNobodyArranged_WithholdsNothing()
    {
        // Arrange
        var participation = StubMailFolderParticipation.Everything;

        // Act, Assert
        Assert.Empty(participation.FoldersHiddenFromTools);
        Assert.Empty(participation.FoldersWithoutEmbeddings);
        Assert.Empty(participation.FoldersNotMirrored);
        Assert.Equal(MailFolderParticipation.Full, participation.GetParticipation(Work, Archive.Alias));
    }

    [Fact]
    public void Hiding_TheFolderItNames_WithholdsItFromToolsAndFromNothingElse()
    {
        // Arrange
        var participation = StubMailFolderParticipation.Hiding(Archive);

        // Act
        var arranged = participation.GetParticipation(Work, Archive.Alias);

        // Assert
        Assert.Equal([Archive], participation.FoldersHiddenFromTools);
        Assert.Empty(participation.FoldersWithoutEmbeddings);
        Assert.Empty(participation.FoldersNotMirrored);
        Assert.False(arranged.IsVisibleToTools);
        Assert.True(arranged.IsSynchronized);
        Assert.True(arranged.GeneratesEmbeddings);
    }

    [Fact]
    public void WithoutEmbeddingsIn_TheFolderItNames_LeavesItMirroredAndReadable()
    {
        // Arrange
        var participation = StubMailFolderParticipation.WithoutEmbeddingsIn(Archive);

        // Act
        var arranged = participation.GetParticipation(Work, Archive.Alias);

        // Assert
        Assert.Equal([Archive], participation.FoldersWithoutEmbeddings);
        Assert.Empty(participation.FoldersHiddenFromTools);
        Assert.Empty(participation.FoldersNotMirrored);
        Assert.False(arranged.GeneratesEmbeddings);
        Assert.True(arranged.IsVisibleToTools);
    }

    /// <summary>A folder nothing mirrors is in every list at once, which is what configuration derives for one.</summary>
    [Fact]
    public void Unmirroring_TheFolderItNames_WithdrawsItFromEveryReaderThereIs()
    {
        // Arrange
        var participation = StubMailFolderParticipation.Unmirroring(Archive);

        // Act
        var arranged = participation.GetParticipation(Work, Archive.Alias);

        // Assert
        Assert.Equal([Archive], participation.FoldersNotMirrored);
        Assert.Equal([Archive], participation.FoldersHiddenFromTools);
        Assert.Equal([Archive], participation.FoldersWithoutEmbeddings);
        Assert.Equal(MailFolderParticipation.MappedOnly, arranged);
    }

    /// <summary>One account's arrangement is never another account's, which is what makes the identity a pair.</summary>
    [Fact]
    public void GetParticipation_TheSameAliasInAnotherAccount_IsNotTheFolderThatWasArranged()
    {
        // Arrange
        var participation = StubMailFolderParticipation.Unmirroring(Archive);

        // Act
        var elsewhere = participation.GetParticipation(MailAccountId.Create("private"), Archive.Alias);

        // Assert
        Assert.Equal(MailFolderParticipation.Full, elsewhere);
    }
}
