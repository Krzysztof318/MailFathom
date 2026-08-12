// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;
using Xunit;

namespace MailFathom.Domain.UnitTests.Folders;

public sealed class MailFolderParticipationTests
{
    /// <summary>A mapping that says nothing about the three switches behaves exactly as a mapping did before they existed.</summary>
    [Fact]
    public void Full_ByItself_TakesPartInEverything()
    {
        // Act
        var participation = MailFolderParticipation.Full;

        // Assert
        Assert.True(participation.IsMapped);
        Assert.True(participation.IsSynchronized);
        Assert.True(participation.GeneratesEmbeddings);
        Assert.True(participation.IsVisibleToTools);
    }

    /// <summary>A folder no mapping names does not exist here, so nothing it once stored takes part in anything.</summary>
    [Fact]
    public void Unmapped_ByItself_TakesPartInNothing()
    {
        // Act
        var participation = MailFolderParticipation.Unmapped;

        // Assert
        Assert.False(participation.IsMapped);
        Assert.False(participation.IsSynchronized);
        Assert.False(participation.GeneratesEmbeddings);
        Assert.False(participation.IsVisibleToTools);
    }

    /// <summary>A folder an operator stopped mirroring and a folder they stopped mapping are two decisions, so one value cannot stand for both.</summary>
    [Fact]
    public void Unmapped_AgainstMappedOnly_IsADifferentValue()
    {
        // Act, Assert
        Assert.NotEqual(MailFolderParticipation.MappedOnly, MailFolderParticipation.Unmapped);
        Assert.True(MailFolderParticipation.MappedOnly.IsMapped);
    }

    /// <summary>An unmirrored folder stores nothing, so neither of the other two answers can be anything but no.</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Create_SynchronizationOff_WithdrawsEmbeddingAndToolVisibilityWhateverTheySaid(
        bool generatesEmbeddings,
        bool isVisibleToTools)
    {
        // Act
        var participation = MailFolderParticipation.Create(
            isSynchronized: false,
            generatesEmbeddings,
            isVisibleToTools);

        // Assert
        Assert.False(participation.IsSynchronized);
        Assert.False(participation.GeneratesEmbeddings);
        Assert.False(participation.IsVisibleToTools);
    }

    /// <summary>The other two are independent of each other, which is what lets a folder be embedded and unreadable, or mirrored and unembedded.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Create_SynchronizationOn_KeepsTheOtherTwoAnswersAsWritten(
        bool generatesEmbeddings,
        bool isVisibleToTools)
    {
        // Act
        var participation = MailFolderParticipation.Create(
            isSynchronized: true,
            generatesEmbeddings,
            isVisibleToTools);

        // Assert
        Assert.True(participation.IsSynchronized);
        Assert.Equal(generatesEmbeddings, participation.GeneratesEmbeddings);
        Assert.Equal(isVisibleToTools, participation.IsVisibleToTools);
    }

    /// <summary>The two named values are the ones every other reading is compared against, so they must be the values the factory produces.</summary>
    [Fact]
    public void Create_TheTwoExtremes_AreTheNamedValues()
    {
        // Act, Assert
        Assert.Equal(
            MailFolderParticipation.Full,
            MailFolderParticipation.Create(isSynchronized: true, generatesEmbeddings: true, isVisibleToTools: true));
        Assert.Equal(
            MailFolderParticipation.MappedOnly,
            MailFolderParticipation.Create(isSynchronized: false, generatesEmbeddings: true, isVisibleToTools: true));
    }

    /// <summary>The three switches are what a mapping says, so every value built from them is a mapped folder's.</summary>
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    public void Create_AnyThreeAnswers_DescribeAMappedFolder(
        bool isSynchronized,
        bool generatesEmbeddings,
        bool isVisibleToTools)
    {
        // Act
        var participation = MailFolderParticipation.Create(
            isSynchronized,
            generatesEmbeddings,
            isVisibleToTools);

        // Assert
        Assert.True(participation.IsMapped);
    }
}
