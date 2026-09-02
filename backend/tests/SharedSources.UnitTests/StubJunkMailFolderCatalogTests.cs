// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the catalog several suites arrange their junk folders with.</summary>
/// <remarks>
/// Both members have to agree, which is the whole reason this is a hand-written double rather than a substitute: a
/// catalog whose list named a folder its per-folder question denied would let every suite using it pass against a
/// configuration nothing produces.
/// </remarks>
public sealed class StubJunkMailFolderCatalogTests
{
    private static readonly MailAccountId Primary = MailAccountId.Create("primary");

    private static readonly MailFolderIdentity Junk = new(Primary, MailFolderAlias.Create("JUNK"));

    [Fact]
    public void None_ADeploymentMappingNoJunkFolder_AnswersNothingToEitherQuestion()
    {
        // Arrange, Act
        var catalog = StubJunkMailFolderCatalog.None;

        // Assert
        Assert.Empty(catalog.JunkFolders);
        Assert.False(catalog.IsJunkFolder(Primary, MailFolderAlias.Create("JUNK")));
    }

    [Fact]
    public void Naming_AMappedJunkFolder_AnswersItToBothQuestions()
    {
        // Arrange, Act
        var catalog = StubJunkMailFolderCatalog.Naming(Junk);

        // Assert
        Assert.Equal([Junk], catalog.JunkFolders);
        Assert.True(catalog.IsJunkFolder(Primary, MailFolderAlias.Create("JUNK")));
    }

    /// <summary>One account's junk folder is not another's, so the question is answered on both halves of the identity.</summary>
    [Theory]
    [InlineData("primary", "INBOX")]
    [InlineData("secondary", "JUNK")]
    public void IsJunkFolder_AFolderTheCatalogDoesNotName_IsNotJunk(string accountId, string alias)
    {
        // Arrange
        var catalog = StubJunkMailFolderCatalog.Naming(Junk);

        // Act, Assert
        Assert.False(catalog.IsJunkFolder(MailAccountId.Create(accountId), MailFolderAlias.Create(alias)));
    }
}
