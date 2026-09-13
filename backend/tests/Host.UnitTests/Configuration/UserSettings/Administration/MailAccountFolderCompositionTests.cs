// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Nodes;
using MailFathom.Host.Configuration.UserSettings.Administration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings.Administration;

/// <summary>
/// Covers the candidate account settings a folder change composes. Nothing here judges what it produced — the binder does
/// that — so what these assert is that the act the caller named is the act the settings received, and that everything
/// the caller did not name travels through untouched.
/// </summary>
public sealed class MailAccountFolderCompositionTests
{
    [Fact]
    public void WithFolderAdded_AnAccountDeclaringNoFolder_LeavesItAtTheFirstPosition()
    {
        // Arrange
        const string account = """{"Host":"mail.example.test"}""";

        // Act
        var candidate = MailAccountFolderComposition.WithFolderAdded(account, """{"Alias":"INBOX/PROJECTS"}""");

        // Assert
        Assert.Equal("INBOX/PROJECTS", ReadFolderAlias(candidate, "0"));
    }

    [Fact]
    public void WithFolderAdded_AnAccountDeclaringItsFoldersAsAnArray_WritesThemBackKeyedByPosition()
    {
        // Arrange
        const string account = """{"Folders":[{"Alias":"INBOX"}]}""";

        // Act
        var candidate = MailAccountFolderComposition.WithFolderAdded(account, """{"Alias":"ARCHIVE"}""");

        // Assert
        Assert.IsType<JsonObject>(JsonNode.Parse(candidate)!["Folders"]);
        Assert.Equal("INBOX", ReadFolderAlias(candidate, "0"));
        Assert.Equal("ARCHIVE", ReadFolderAlias(candidate, "1"));
    }

    /// <summary>A folder change is about folders, so every other setting travels through it untouched.</summary>
    [Fact]
    public void WithFolderAdded_AnAccountCarryingOtherSettings_LeavesThemUntouched()
    {
        // Arrange
        const string account = """{"Host":"mail.example.test","UserName":"alex"}""";

        // Act
        var candidate = MailAccountFolderComposition.WithFolderAdded(account, """{"Alias":"INBOX"}""");

        // Assert
        var written = JsonNode.Parse(candidate)!.AsObject();

        Assert.Equal("mail.example.test", written["Host"]!.GetValue<string>());
        Assert.Equal("alex", written["UserName"]!.GetValue<string>());
    }

    /// <summary>An alias somebody already declared is a collision the naming rules refuse by name, not settings to overwrite unread.</summary>
    [Fact]
    public void WithFolderAdded_AnAliasTheAccountAlreadyDeclares_AppendsItRatherThanReplacingTheExistingEntry()
    {
        // Arrange
        const string account = """{"Folders":{"0":{"Alias":"INBOX","RemotePath":"INBOX"}}}""";

        // Act
        var candidate = MailAccountFolderComposition.WithFolderAdded(account, """{"Alias":"INBOX","RemotePath":"Other"}""");

        // Assert
        Assert.Equal("INBOX", ReadFolderAlias(candidate, "0"));
        Assert.Equal("INBOX", ReadFolderAlias(candidate, "1"));
    }

    [Fact]
    public void WithFolderAdded_ADeclarationThatIsNotAJsonObject_IsRefused()
    {
        // Act and assert
        Assert.Throws<FormatException>(() => MailAccountFolderComposition.WithFolderAdded("{}", "[]"));
    }

    [Fact]
    public void WithFolderAdded_AnAccountThatIsNotAJsonObject_IsRefused()
    {
        // Act and assert
        Assert.Throws<FormatException>(() => MailAccountFolderComposition.WithFolderAdded("[]", """{"Alias":"INBOX"}"""));
    }

    [Fact]
    public void WithFolderAdded_ADeclarationThatIsNotJsonAtAll_IsRefused()
    {
        // Act and assert
        Assert.ThrowsAny<JsonException>(() => MailAccountFolderComposition.WithFolderAdded("{}", "not json"));
    }

    /// <summary>The depth ceiling is the account's rather than a screen's, so a caller reaching the route without the dialog meets it too.</summary>
    [Theory]
    [InlineData("INBOX/PROJECTS/2027/Q1")]
    [InlineData("A/B/C/D/E")]
    [InlineData("/INBOX/PROJECTS/2027/Q1/")]
    public void WithFolderAdded_AnAliasNestedPastThreeLevels_IsRefusedNamingTheAliasAndItsDepth(string alias)
    {
        // Act
        var refused = Assert.Throws<FormatException>(() => MailAccountFolderComposition.WithFolderAdded(
            "{}",
            $$"""{"Alias":"{{alias}}"}"""));

        // Assert
        Assert.Contains(alias, refused.Message, StringComparison.Ordinal);
        Assert.Contains("3 levels", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>Three levels is what the design draws to, so the deepest alias a dialog can compose is one the account takes.</summary>
    [Fact]
    public void WithFolderAdded_AnAliasNestedExactlyThreeLevels_IsDeclared()
    {
        // Act
        var candidate = MailAccountFolderComposition.WithFolderAdded("{}", """{"Alias":"INBOX/PROJECTS/2027"}""");

        // Assert
        Assert.Equal("INBOX/PROJECTS/2027", ReadFolderAlias(candidate, "0"));
    }

    [Fact]
    public void WithFolderReplaced_AnAliasNestedPastThreeLevels_IsRefused()
    {
        // Act and assert
        Assert.Throws<FormatException>(() => MailAccountFolderComposition.WithFolderReplaced(
            """{"Folders":{"0":{"Alias":"INBOX/OLD"}}}""",
            "INBOX/OLD",
            """{"Alias":"INBOX/PROJECTS/2027/Q1"}"""));
    }

    /// <summary>Renaming a folder is the change that proves the replacement keeps its position among its siblings.</summary>
    [Fact]
    public void WithFolderReplaced_AFolderBetweenTwoOthers_LeavesItWhereItStood()
    {
        // Arrange
        const string account = """{"Folders":{"0":{"Alias":"INBOX"},"1":{"Alias":"INBOX/OLD"},"2":{"Alias":"SENT"}}}""";

        // Act
        var candidate = MailAccountFolderComposition.WithFolderReplaced(
            account,
            "INBOX/OLD",
            """{"Alias":"INBOX/NEW","RemotePath":"INBOX/New"}""");

        // Assert
        Assert.Equal("INBOX", ReadFolderAlias(candidate!, "0"));
        Assert.Equal("INBOX/NEW", ReadFolderAlias(candidate!, "1"));
        Assert.Equal("SENT", ReadFolderAlias(candidate!, "2"));
    }

    /// <summary>An alias is upper-cased where it is created, so finding one is case-insensitive wherever it is matched.</summary>
    [Fact]
    public void WithFolderReplaced_AnAliasSpelledInAnotherCase_FindsTheFolder()
    {
        // Act
        var candidate = MailAccountFolderComposition.WithFolderReplaced(
            """{"Folders":{"0":{"Alias":"INBOX/OLD"}}}""",
            "inbox/old",
            """{"Alias":"INBOX/NEW"}""");

        // Assert
        Assert.Equal("INBOX/NEW", ReadFolderAlias(candidate!, "0"));
    }

    [Fact]
    public void WithFolderReplaced_AnAliasTheAccountDoesNotDeclare_MatchesNothing()
    {
        // Act
        var candidate = MailAccountFolderComposition.WithFolderReplaced(
            """{"Folders":{"0":{"Alias":"INBOX"}}}""",
            "ARCHIVE",
            """{"Alias":"ARCHIVE"}""");

        // Assert
        Assert.Null(candidate);
    }

    [Fact]
    public void WithFolderRemoved_AnAliasTheAccountDeclares_LeavesTheOthersRenumbered()
    {
        // Arrange
        const string account = """{"Folders":{"0":{"Alias":"INBOX"},"1":{"Alias":"INBOX/OLD"},"2":{"Alias":"SENT"}}}""";

        // Act
        var candidate = MailAccountFolderComposition.WithFolderRemoved(account, "INBOX/OLD");

        // Assert
        Assert.Equal("INBOX", ReadFolderAlias(candidate!, "0"));
        Assert.Equal("SENT", ReadFolderAlias(candidate!, "1"));
    }

    /// <summary>The positions mean what the configuration layer orders them by, so keys out of order still read as the account a start binds.</summary>
    [Fact]
    public void WithFolderRemoved_AnAccountWhoseKeysAreOutOfOrder_KeepsTheOthersInTheOrderTheyBind()
    {
        // Arrange
        const string account = """{"Folders":{"10":{"Alias":"LAST"},"2":{"Alias":"MIDDLE"},"0":{"Alias":"FIRST"}}}""";

        // Act
        var candidate = MailAccountFolderComposition.WithFolderRemoved(account, "MIDDLE");

        // Assert
        Assert.Equal("FIRST", ReadFolderAlias(candidate!, "0"));
        Assert.Equal("LAST", ReadFolderAlias(candidate!, "1"));
    }

    /// <summary>What a slash in an alias means to the tree a screen draws is the caller's reading, so nothing nested goes with it here.</summary>
    [Fact]
    public void WithFolderRemoved_AFolderCarryingNestedOnes_WithdrawsOnlyTheOneNamed()
    {
        // Arrange
        const string account = """{"Folders":{"0":{"Alias":"INBOX/OLD"},"1":{"Alias":"INBOX/OLD/2026"}}}""";

        // Act
        var candidate = MailAccountFolderComposition.WithFolderRemoved(account, "INBOX/OLD");

        // Assert
        Assert.Equal("INBOX/OLD/2026", ReadFolderAlias(candidate!, "0"));
    }

    /// <summary>An account left with no folder carries no collection at all, which is what the next reader has to see.</summary>
    [Fact]
    public void WithFolderRemoved_TheLastFolderOfAnAccount_LeavesNoCollectionBehind()
    {
        // Act
        var candidate = MailAccountFolderComposition.WithFolderRemoved("""{"Folders":{"0":{"Alias":"INBOX"}}}""", "INBOX");

        // Assert
        Assert.False(JsonNode.Parse(candidate!)!.AsObject().ContainsKey("Folders"));
    }

    /// <summary>
    /// A property spelled differently is the same setting to every provider in the pipeline, so settings that spelled the
    /// collection their own way must not come back carrying both spellings.
    /// </summary>
    [Fact]
    public void WithFolderRemoved_AnAccountSpellingTheCollectionDifferently_LeavesItStatedOnce()
    {
        // Arrange
        const string account = """{"folders":{"0":{"alias":"INBOX"},"1":{"alias":"SENT"}}}""";

        // Act
        var candidate = MailAccountFolderComposition.WithFolderRemoved(account, "SENT");

        // Assert
        var written = JsonNode.Parse(candidate!)!.AsObject();

        Assert.Single(written, entry => entry.Key.Equals("Folders", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WithFolderRemoved_AnAliasTheAccountDoesNotDeclare_MatchesNothing()
    {
        // Act
        var candidate = MailAccountFolderComposition.WithFolderRemoved("""{"Folders":{"0":{"Alias":"INBOX"}}}""", "ARCHIVE");

        // Assert
        Assert.Null(candidate);
    }

    private static string? ReadFolderAlias(string json, string position) =>
        JsonNode.Parse(json)!["Folders"]![position]!["Alias"]!.GetValue<string>();
}
