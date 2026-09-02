// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the folder reader every suite arranging a named folder answers through.</summary>
/// <remarks>
/// A fault here reports somebody else's arrangement: a reader that answered for the wrong account would make a
/// cross-account test pass while proving nothing, and one that shared its list between instances would let a folder
/// arranged by one test decide another test's answer.
/// </remarks>
public sealed class StubMailFolderMappingsTests
{
    private static readonly MailAccountId Work = MailAccountId.Create("work");
    private static readonly MailAccountId Private = MailAccountId.Create("private");

    [Fact]
    public void FindFolderPlayingRole_AFolderArrangedForTheAccount_AnswersWithIt()
    {
        // Arrange
        var junk = MailFolderMapping.ToSpecialUse(MailFolderAlias.Create("junk"), MailFolderSpecialUse.Junk);

        // Act
        var found = StubMailFolderMappings.Nothing.With(Work, junk).FindFolderPlayingRole(Work, MailFolderSpecialUse.Junk);

        // Assert
        Assert.Equal(junk, found);
    }

    [Fact]
    public void FindFolderPlayingRole_AFolderArrangedForAnotherAccount_AnswersWithNothing()
    {
        // Arrange
        var junk = MailFolderMapping.ToSpecialUse(MailFolderAlias.Create("junk"), MailFolderSpecialUse.Junk);

        // Act
        var found = StubMailFolderMappings.Nothing.With(Private, junk).FindFolderPlayingRole(Work, MailFolderSpecialUse.Junk);

        // Assert
        Assert.Null(found);
    }

    [Fact]
    public void FindFolderNamed_AFolderArrangedUnderThatAlias_AnswersWithIt()
    {
        // Arrange
        var archive = MailFolderMapping.ToRemotePath(MailFolderAlias.Create("archive"), RemoteFolderPath.Create("INBOX.Archive"));

        // Act
        var reader = StubMailFolderMappings.Nothing.With(Work, archive);

        // Assert
        Assert.Equal(archive, reader.FindFolderNamed(Work, MailFolderAlias.Create("ARCHIVE")));
        Assert.Null(reader.FindFolderNamed(Work, MailFolderAlias.Create("sent")));
    }

    /// <summary>The arrangement is per reader, so a test that took the default answer never sees a folder another one added.</summary>
    [Fact]
    public void Nothing_ReadTwice_AnswersFromTwoSeparateArrangements()
    {
        // Arrange
        var arranged = StubMailFolderMappings.Nothing;
        arranged.With(Work, MailFolderMapping.ToSpecialUse(MailFolderAlias.Create("junk"), MailFolderSpecialUse.Junk));

        // Act
        var untouched = StubMailFolderMappings.Nothing;

        // Assert
        Assert.NotNull(arranged.FindFolderPlayingRole(Work, MailFolderSpecialUse.Junk));
        Assert.Null(untouched.FindFolderPlayingRole(Work, MailFolderSpecialUse.Junk));
    }

    [Fact]
    public void ResolvingNothing_ARoleNothingWasArrangedFor_LeavesTheRefusalToTheResolver()
    {
        // Arrange
        var resolver = StubMailFolderMappings.ResolvingNothing;

        // Act
        var resolved = resolver.TryResolve(Work, MailFolderReference.ToRole(MailFolderSpecialUse.Junk));

        // Assert
        Assert.Null(resolved);
        Assert.Null(resolver.Resolve(Work, MailFolderReference.ToAlias(MailFolderAlias.Create("inbox"))));
    }

    [Fact]
    public void Resolver_TheArrangementItWasBuiltFrom_IsWhatItAnswersFrom()
    {
        // Arrange
        var junk = MailFolderMapping.ToSpecialUse(MailFolderAlias.Create("junk"), MailFolderSpecialUse.Junk);
        var reader = StubMailFolderMappings.Nothing.With(Work, junk);

        // Act
        var resolved = reader.Resolver.Resolve(Work, MailFolderReference.ToRole(MailFolderSpecialUse.Junk));

        // Assert
        Assert.Equal(junk, resolved);
    }
}
