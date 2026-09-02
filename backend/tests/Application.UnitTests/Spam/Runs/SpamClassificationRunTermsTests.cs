// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Spam.Actions;
using MailFathom.Application.Spam.Runs;
using MailFathom.Domain.Folders;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam.Runs;

public sealed class SpamClassificationRunTermsTests
{
    [Fact]
    public void Create_FoldersNamedTwiceAndOutOfOrder_KeepsOneOfEachInANormalizedOrder()
    {
        // Arrange
        MailFolderAlias[] named =
        [
            MailFolderAlias.Create("INBOX"),
            MailFolderAlias.Create("ARCHIVE"),
            MailFolderAlias.Create("INBOX"),
        ];

        // Act
        var terms = SpamClassificationRunTerms.Create(named, SpamActionPosture.DryRun, rescores: false);

        // Assert
        Assert.Equal(["ARCHIVE", "INBOX"], terms.FolderAliases.Select(alias => alias.Value));
    }

    [Fact]
    public void Create_APostureOutsideTheDeclaredSet_IsRefused()
    {
        // Arrange, Act, Assert
        var failure = Assert.Throws<ArgumentOutOfRangeException>(
            () => SpamClassificationRunTerms.Create([], (SpamActionPosture)7, rescores: false));

        Assert.Equal("posture", failure.ParamName);
    }

    /// <summary>A scope narrowed to nothing is a scope of nothing, which is what the walk over it then reads.</summary>
    [Fact]
    public void Create_NoFolderNamed_NamesNoMailRatherThanAllOfIt()
    {
        // Arrange, Act
        var terms = SpamClassificationRunTerms.Create([], SpamActionPosture.Acting, rescores: true);

        // Assert
        Assert.Empty(terms.FolderAliases);
        Assert.Equal(SpamActionPosture.Acting, terms.Posture);
        Assert.True(terms.Rescores);
    }
}
