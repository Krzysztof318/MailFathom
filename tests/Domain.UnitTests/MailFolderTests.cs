// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Folders;
using Xunit;

namespace MailMcp.Domain.UnitTests;

public sealed class MailFolderTests
{
    [Theory]
    [InlineData("inbox", "INBOX")]
    [InlineData("  Inbox  ", "INBOX")]
    [InlineData("Archive/2026", "ARCHIVE/2026")]
    public void Create_AliasDifferingOnlyByCaseOrPadding_NormalizesToOneValue(string configured, string expected)
    {
        // Arrange, Act
        var alias = MailFolderAlias.Create(configured);

        // Assert
        Assert.Equal(expected, alias.Value);
        Assert.Equal(MailFolderAlias.Create(expected), alias);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("in\tbox")]
    public void Create_BlankOrControlCharacterAlias_ThrowsArgumentException(string configured)
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>(() => MailFolderAlias.Create(configured));
    }

    /// <summary>RFC 3501 makes the inbox the one folder name a server may spell in any case.</summary>
    [Theory]
    [InlineData("INBOX")]
    [InlineData("Inbox")]
    [InlineData("inbox")]
    public void Create_InboxInAnyCase_NamesTheSameRemoteFolder(string advertised)
    {
        // Arrange, Act
        var path = RemoteFolderPath.Create(advertised, '.');

        // Assert
        Assert.Equal("INBOX", path.Value);
        Assert.Equal(RemoteFolderPath.Create("INBOX", '.'), path);
    }

    [Fact]
    public void ToHierarchyLevels_DelimitedPath_SplitsOnTheAdvertisedDelimiter()
    {
        // Arrange
        var path = RemoteFolderPath.Create("Archief/2026/Q3", '/');

        // Act
        var levels = path.ToHierarchyLevels();

        // Assert
        Assert.Equal(["Archief", "2026", "Q3"], levels);
    }

    [Fact]
    public void ToHierarchyLevels_ServerReportedNoDelimiter_KeepsThePathAsOneLevel()
    {
        // Arrange
        var path = RemoteFolderPath.Create("Archief/2026");

        // Act
        var levels = path.ToHierarchyLevels();

        // Assert
        Assert.Equal(["Archief/2026"], levels);
    }

    [Theory]
    [InlineData("/Archive", '/')]
    [InlineData("Archive.", '.')]
    [InlineData("Archive", ' ')]
    [InlineData("", '/')]
    public void Create_PathBoundedByOrDelimitedWithAnUnusableCharacter_ThrowsArgumentException(
        string advertised,
        char hierarchyDelimiter)
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>(() => RemoteFolderPath.Create(advertised, hierarchyDelimiter));
    }

    /// <summary>Discovery reads whatever a server lists, so an entry that names no folder must be reportable rather than fatal.</summary>
    [Theory]
    [InlineData("", '/', false)]
    [InlineData("/Archive", '/', false)]
    [InlineData("Archive", '/', true)]
    public void TryCreate_AdvertisedPath_ReportsWhetherItNamesAFolder(
        string advertised,
        char hierarchyDelimiter,
        bool expectedToBeUsable)
    {
        // Arrange, Act
        var isUsable = RemoteFolderPath.TryCreate(advertised, hierarchyDelimiter, out var path);

        // Assert
        Assert.Equal(expectedToBeUsable, isUsable);
        Assert.Equal(expectedToBeUsable ? advertised : null, isUsable ? path.Value : null);
    }

    [Fact]
    public void ToSpecialUse_RoleThatDoesNotExist_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var alias = MailFolderAlias.Create("inbox");

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MailFolderMapping.ToSpecialUse(alias, (MailFolderSpecialUse)99));
    }

    [Fact]
    public void ToRemotePath_ConfiguredPath_NamesExactlyOneTarget()
    {
        // Arrange
        var alias = MailFolderAlias.Create("archive");

        // Act
        var mapping = MailFolderMapping.ToRemotePath(alias, RemoteFolderPath.Create("Archief"));

        // Assert
        Assert.Equal(MailFolderMappingTarget.RemotePath, mapping.Target);
        Assert.Equal("Archief", mapping.RemotePath!.Value.Value);
        Assert.Null(mapping.SpecialUse);
    }

    [Fact]
    public void ToSpecialUse_ConfiguredRole_NamesExactlyOneTarget()
    {
        // Arrange
        var alias = MailFolderAlias.Create("inbox");

        // Act
        var mapping = MailFolderMapping.ToSpecialUse(alias, MailFolderSpecialUse.Inbox);

        // Assert
        Assert.Equal(MailFolderMappingTarget.SpecialUse, mapping.Target);
        Assert.Equal(MailFolderSpecialUse.Inbox, mapping.SpecialUse);
        Assert.Null(mapping.RemotePath);
    }

    [Fact]
    public void RepointedTo_DifferentRemoteFolder_StartsTheNextGenerationUnderTheSameAlias()
    {
        // Arrange
        var binding = MailFolderResolution.FirstBindingOf(
            MailFolderAlias.Create("archive"),
            RemoteFolderPath.Create("Archief", '/'));

        // Act
        var repointed = binding.RepointedTo(RemoteFolderPath.Create("Archive/2026", '/'));

        // Assert
        Assert.Equal(binding.Alias, repointed.Alias);
        Assert.Equal(2, repointed.Generation.Value);
        Assert.Equal("Archive/2026", repointed.RemotePath.Value);
        Assert.NotEqual(binding.Id, repointed.Id);
    }

    /// <summary>A generation nothing changed would split one folder's occurrences across two identities for no reason.</summary>
    [Fact]
    public void RepointedTo_TheRemoteFolderItAlreadyNames_ThrowsArgumentException()
    {
        // Arrange
        var remotePath = RemoteFolderPath.Create("Archief", '/');
        var binding = MailFolderResolution.FirstBindingOf(MailFolderAlias.Create("archive"), remotePath);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => binding.RepointedTo(remotePath));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_NonPositiveGeneration_ThrowsArgumentOutOfRangeException(int value)
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => MailFolderResolutionGeneration.Create(value));
    }
}
