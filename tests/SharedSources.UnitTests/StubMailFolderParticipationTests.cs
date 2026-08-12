// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the participation reader every suite arranging a folder decision answers through.</summary>
/// <remarks>
/// A fault here reports somebody else's arrangement. A reader that admitted a folder no test mapped would make a
/// mailbox read return mail the deployment does not have while the test read as proof that it does; one whose two lists
/// disagreed with its per-folder answer would let a folder be readable through a query and unreadable through an
/// identifier, which is exactly the divergence the production reader exists to prevent.
/// </remarks>
public sealed class StubMailFolderParticipationTests
{
    private static readonly MailAccountId Work = MailAccountId.Create("work");
    private static readonly MailAccountId Private = MailAccountId.Create("private");

    private static readonly MailFolderIdentity WorkInbox = new(Work, MailFolderAlias.Create("inbox"));
    private static readonly MailFolderIdentity WorkArchive = new(Work, MailFolderAlias.Create("archive"));
    private static readonly MailFolderIdentity PrivateInbox = new(Private, MailFolderAlias.Create("inbox"));

    [Fact]
    public void Mapping_TheFoldersItNames_AdmitsEachOfThemToEverything()
    {
        // Act
        var participation = StubMailFolderParticipation.Mapping(WorkInbox, PrivateInbox);

        // Assert
        Assert.Equal([WorkInbox, PrivateInbox], participation.FoldersSynchronized);
        Assert.Equal([WorkInbox, PrivateInbox], participation.FoldersVisibleToTools);
        Assert.Equal([WorkInbox, PrivateInbox], participation.FoldersGeneratingEmbeddings);
        Assert.Equal(MailFolderParticipation.Full, participation.GetParticipation(Work, WorkInbox.Alias));
    }

    /// <summary>A folder no arrangement mapped is a folder the deployment does not have, which is what the reader has to say.</summary>
    [Fact]
    public void GetParticipation_AFolderNothingMapped_AnswersUnmapped()
    {
        // Arrange
        var participation = StubMailFolderParticipation.Mapping(WorkInbox);

        // Act, Assert
        Assert.Equal(MailFolderParticipation.Unmapped, participation.GetParticipation(Work, WorkArchive.Alias));
        Assert.Equal(MailFolderParticipation.Unmapped, participation.GetParticipation(Private, WorkInbox.Alias));
        Assert.Empty(StubMailFolderParticipation.Nothing.FoldersVisibleToTools);
        Assert.Empty(StubMailFolderParticipation.Nothing.FoldersGeneratingEmbeddings);
    }

    [Fact]
    public void Hiding_AMappedFolder_LeavesItOutOfTheToolsListAndKeepsItEmbedded()
    {
        // Act
        var participation = StubMailFolderParticipation.Mapping(WorkInbox, WorkArchive).Hiding(WorkArchive);

        // Assert
        Assert.Equal([WorkInbox], participation.FoldersVisibleToTools);
        Assert.Equal([WorkInbox, WorkArchive], participation.FoldersGeneratingEmbeddings);
        Assert.False(participation.GetParticipation(Work, WorkArchive.Alias).IsVisibleToTools);
        Assert.True(participation.GetParticipation(Work, WorkArchive.Alias).GeneratesEmbeddings);
    }

    [Fact]
    public void WithoutEmbeddingsIn_AMappedFolder_LeavesItOutOfTheEmbeddingListAndKeepsItReadable()
    {
        // Act
        var participation = StubMailFolderParticipation
            .Mapping(WorkInbox, WorkArchive)
            .WithoutEmbeddingsIn(WorkArchive);

        // Assert
        Assert.Equal([WorkInbox, WorkArchive], participation.FoldersVisibleToTools);
        Assert.Equal([WorkInbox], participation.FoldersGeneratingEmbeddings);
    }

    /// <summary>A folder nothing mirrors takes part in nothing, so it is admitted to no list while staying mapped.</summary>
    [Fact]
    public void Unmirroring_AMappedFolder_AdmitsItToNoListAndKeepsItMapped()
    {
        // Act
        var participation = StubMailFolderParticipation.Mapping(WorkInbox, WorkArchive).Unmirroring(WorkArchive);

        // Assert
        Assert.Equal([WorkInbox], participation.FoldersSynchronized);
        Assert.Equal([WorkInbox], participation.FoldersVisibleToTools);
        Assert.Equal([WorkInbox], participation.FoldersGeneratingEmbeddings);
        Assert.Equal(MailFolderParticipation.MappedOnly, participation.GetParticipation(Work, WorkArchive.Alias));
    }

    /// <summary>An arrangement states one decision per folder, so the last thing a test said about one is what it answers.</summary>
    [Fact]
    public void With_AFolderArrangedTwice_KeepsTheDecisionStatedLast()
    {
        // Act
        var participation = StubMailFolderParticipation.Mapping(WorkInbox).Hiding(WorkInbox);

        // Assert
        Assert.Empty(participation.FoldersVisibleToTools);
        Assert.Equal([WorkInbox], participation.FoldersGeneratingEmbeddings);
    }

    /// <summary>The arrangement is per reader, so a test that took the default answer never sees a folder another one mapped.</summary>
    [Fact]
    public void Nothing_ReadTwice_AnswersFromTwoSeparateArrangements()
    {
        // Arrange
        var arranged = StubMailFolderParticipation.Nothing;
        arranged.With(WorkInbox, MailFolderParticipation.Full);

        // Act
        var untouched = StubMailFolderParticipation.Nothing;

        // Assert
        Assert.NotEmpty(arranged.FoldersVisibleToTools);
        Assert.Empty(untouched.FoldersVisibleToTools);
    }
}
