// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Nodes;
using MailFathom.Host.Configuration.UserSettings.Administration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings.Administration;

/// <summary>
/// Covers the candidate account settings a folder change composes, and the part of a declaration the client surface
/// fixes rather than the person making it. Everything else is the binder's to judge, so what these assert is that the
/// act the caller named is the act the settings received, that the switches and the role rules hold, and that
/// everything the caller did not name travels through untouched.
/// </summary>
public sealed class MailAccountFolderCompositionTests
{
    [Fact]
    public void WithFolderAdded_AnAccountDeclaringNoFolder_LeavesItAtTheFirstPosition()
    {
        // Arrange
        const string account = """{"Host":"mail.example.test"}""";

        // Act
        var candidate = MailAccountFolderComposition.WithFolderAdded(account, """{"Alias":"INBOX/PROJECTS"}""").Candidate;

        // Assert
        Assert.Equal("INBOX/PROJECTS", ReadFolderAlias(candidate, "0"));
    }

    [Fact]
    public void WithFolderAdded_AnAccountDeclaringItsFoldersAsAnArray_WritesThemBackKeyedByPosition()
    {
        // Arrange
        const string account = """{"Folders":[{"Alias":"INBOX"}]}""";

        // Act
        var candidate = MailAccountFolderComposition.WithFolderAdded(account, """{"Alias":"ARCHIVE"}""").Candidate;

        // Assert
        Assert.IsType<JsonObject>(JsonNode.Parse(candidate!)!["Folders"]);
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
        var candidate = MailAccountFolderComposition.WithFolderAdded(account, """{"Alias":"INBOX"}""").Candidate;

        // Assert
        var written = JsonNode.Parse(candidate!)!.AsObject();

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
        var candidate = MailAccountFolderComposition.WithFolderAdded(account, """{"Alias":"INBOX","RemotePath":"Other"}""").Candidate;

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
        var candidate = MailAccountFolderComposition.WithFolderAdded("{}", """{"Alias":"INBOX/PROJECTS/2027"}""").Candidate;

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
            """{"Alias":"INBOX/NEW","RemotePath":"INBOX/New"}""").Candidate;

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
            """{"Alias":"INBOX/NEW"}""").Candidate;

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
            """{"Alias":"ARCHIVE"}""").Candidate;

        // Assert
        Assert.Null(candidate);
    }

    [Fact]
    public void WithFolderRemoved_AnAliasTheAccountDeclares_LeavesTheOthersRenumbered()
    {
        // Arrange
        const string account = """{"Folders":{"0":{"Alias":"INBOX"},"1":{"Alias":"INBOX/OLD"},"2":{"Alias":"SENT"}}}""";

        // Act
        var candidate = MailAccountFolderComposition.WithFolderRemoved(account, "INBOX/OLD").Candidate;

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
        var candidate = MailAccountFolderComposition.WithFolderRemoved(account, "MIDDLE").Candidate;

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
        var candidate = MailAccountFolderComposition.WithFolderRemoved(account, "INBOX/OLD").Candidate;

        // Assert
        Assert.Equal("INBOX/OLD/2026", ReadFolderAlias(candidate!, "0"));
    }

    /// <summary>An account left with no folder carries no collection at all, which is what the next reader has to see.</summary>
    [Fact]
    public void WithFolderRemoved_TheLastFolderOfAnAccount_LeavesNoCollectionBehind()
    {
        // Act
        var candidate = MailAccountFolderComposition.WithFolderRemoved("""{"Folders":{"0":{"Alias":"INBOX"}}}""", "INBOX").Candidate;

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
        var candidate = MailAccountFolderComposition.WithFolderRemoved(account, "SENT").Candidate;

        // Assert
        var written = JsonNode.Parse(candidate!)!.AsObject();

        Assert.Single(written, entry => entry.Key.Equals("Folders", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WithFolderRemoved_AnAliasTheAccountDoesNotDeclare_MatchesNothing()
    {
        // Act
        var candidate = MailAccountFolderComposition.WithFolderRemoved("""{"Folders":{"0":{"Alias":"INBOX"}}}""", "ARCHIVE").Candidate;

        // Assert
        Assert.Null(candidate);
    }

    /// <summary>The two switches are the service's rather than the person's, so a folder naming a path comes back carrying both.</summary>
    [Fact]
    public void WithFolderAdded_AFolderNamingARemotePath_CarriesBothSwitchesAsTrue()
    {
        // Act
        var candidate = MailAccountFolderComposition
            .WithFolderAdded("{}", """{"Alias":"INBOX/PROJECTS","RemotePath":"INBOX/Projects"}""")
            .Candidate;

        // Assert
        Assert.True(ReadFolderSwitch(candidate, "0", "Synchronize"));
        Assert.True(ReadFolderSwitch(candidate, "0", "CreateIfMissing"));
    }

    /// <summary>A switch this surface sets is refused rather than overwritten, so a client is told what it asked for was not taken.</summary>
    [Theory]
    [InlineData("Synchronize", "false")]
    [InlineData("Synchronize", "\"no\"")]
    [InlineData("CreateIfMissing", "false")]
    public void WithFolderAdded_ASwitchStatedAsAnythingButTrue_IsRefusedNamingIt(string property, string stated)
    {
        // Act
        var change = MailAccountFolderComposition.WithFolderAdded(
            "{}",
            $$"""{"Alias":"INBOX","RemotePath":"INBOX","{{property}}":{{stated}}}""");

        // Assert
        Assert.Null(change.Candidate);
        Assert.Contains(property, change.Refusal!, StringComparison.Ordinal);
    }

    /// <summary>Stating the value the service sets is not asking for something else, so it is declared rather than read as a contradiction.</summary>
    [Fact]
    public void WithFolderAdded_ASwitchStatedAsTheValueTheServiceSets_IsDeclared()
    {
        // Act
        var candidate = MailAccountFolderComposition
            .WithFolderAdded("{}", """{"Alias":"INBOX","RemotePath":"INBOX","Synchronize":true,"CreateIfMissing":true}""")
            .Candidate;

        // Assert
        Assert.True(ReadFolderSwitch(candidate, "0", "Synchronize"));
    }

    /// <summary>
    /// A declaration is the object a configuration file would have written, and every provider flattens a file's values
    /// to strings before anything binds them — so the two spellings of the value this surface sets are one answer, and
    /// refusing the quoted one would refuse a request asking for exactly what it sets.
    /// </summary>
    [Theory]
    [InlineData("\"true\"")]
    [InlineData("\"True\"")]
    public void WithFolderAdded_ASwitchStatedAsTheValueTheServiceSetsSpelledAsAString_IsDeclared(string stated)
    {
        // Act
        var candidate = MailAccountFolderComposition
            .WithFolderAdded("{}", $$"""{"Alias":"INBOX","RemotePath":"INBOX","Synchronize":{{stated}}}""")
            .Candidate;

        // Assert
        Assert.True(ReadFolderSwitch(candidate, "0", "Synchronize"));
    }

    /// <summary>A property spelled differently is the same setting to every provider in the pipeline, so a switch must not acquire a second spelling.</summary>
    [Fact]
    public void WithFolderAdded_AFolderSpellingASwitchItsOwnWay_LeavesItStatedOnce()
    {
        // Act
        var candidate = MailAccountFolderComposition
            .WithFolderAdded("{}", """{"Alias":"INBOX","RemotePath":"INBOX","synchronize":true}""")
            .Candidate;

        // Assert
        var folder = JsonNode.Parse(candidate!)!["Folders"]!["0"]!.AsObject();

        Assert.Single(folder, entry => entry.Key.Equals("Synchronize", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A folder found by the role it plays is whichever folder advertises the attribute, and one that does not exist advertises nothing.</summary>
    [Fact]
    public void WithFolderAdded_AFolderFoundByTheRoleItPlays_IsSynchronizedAndCarriesNoCreationSwitch()
    {
        // Act
        var candidate = MailAccountFolderComposition.WithFolderAdded("{}", """{"Alias":"JUNK","SpecialUse":"Junk"}""").Candidate;

        // Assert
        Assert.True(ReadFolderSwitch(candidate, "0", "Synchronize"));
        Assert.False(JsonNode.Parse(candidate!)!["Folders"]!["0"]!.AsObject().ContainsKey("CreateIfMissing"));
    }

    [Fact]
    public void WithFolderAdded_TheCreationSwitchStatedOnAFolderNamingNoPath_IsRefusedNamingTheRemotePath()
    {
        // Act
        var change = MailAccountFolderComposition.WithFolderAdded(
            "{}",
            """{"Alias":"JUNK","SpecialUse":"Junk","CreateIfMissing":true}""");

        // Assert
        Assert.Null(change.Candidate);
        Assert.Contains("RemotePath", change.Refusal!, StringComparison.Ordinal);
    }

    /// <summary>A mailbox with two folders both called the inbox has no inbox, so the folder playing a role is reached by the role's own name.</summary>
    [Fact]
    public void WithFolderAdded_ARoleUnderAnAliasThatIsNotItsName_IsRefusedNamingTheRole()
    {
        // Act
        var change = MailAccountFolderComposition.WithFolderAdded(
            "{}",
            """{"Alias":"SPAM","SpecialUse":"Junk","RemotePath":"Spam"}""");

        // Assert
        Assert.Null(change.Candidate);
        Assert.Contains("Junk", change.Refusal!, StringComparison.Ordinal);
    }

    /// <summary>An alias is upper-cased where it is created, so the role's own name in any casing is the role's own name.</summary>
    [Fact]
    public void WithFolderAdded_ARoleUnderItsOwnNameInAnotherCase_IsDeclared()
    {
        // Act
        var candidate = MailAccountFolderComposition
            .WithFolderAdded("{}", """{"Alias":"junk","SpecialUse":"Junk","RemotePath":"Spam"}""")
            .Candidate;

        // Assert
        Assert.Equal("junk", ReadFolderAlias(candidate, "0"));
    }

    /// <summary>These three stay the person's on every folder, special ones included, which is what lets a folder declared against the wrong place be corrected.</summary>
    [Fact]
    public void WithFolderReplaced_ASpecialFoldersEditableSettings_TravelThrough()
    {
        // Arrange
        const string account = """{"Folders":{"0":{"Alias":"JUNK","SpecialUse":"Junk","RemotePath":"Spam"}}}""";

        // Act
        var candidate = MailAccountFolderComposition.WithFolderReplaced(
                account,
                "JUNK",
                """{"Alias":"JUNK","SpecialUse":"Junk","RemotePath":"[Gmail]/Spam","GenerateEmbeddings":false,"VisibleToTools":false}""")
            .Candidate;

        // Assert
        var folder = JsonNode.Parse(candidate!)!["Folders"]!["0"]!.AsObject();

        Assert.Equal("[Gmail]/Spam", folder["RemotePath"]!.GetValue<string>());
        Assert.False(folder["GenerateEmbeddings"]!.GetValue<bool>());
        Assert.False(folder["VisibleToTools"]!.GetValue<bool>());
    }

    [Fact]
    public void WithFolderReplaced_TheRoleWithdrawnFromASpecialFolder_IsRefusedNamingTheRole()
    {
        // Act
        var change = MailAccountFolderComposition.WithFolderReplaced(
            """{"Folders":{"0":{"Alias":"JUNK","SpecialUse":"Junk","RemotePath":"Spam"}}}""",
            "JUNK",
            """{"Alias":"JUNK","RemotePath":"Spam"}""");

        // Assert
        Assert.Null(change.Candidate);
        Assert.Contains("Junk", change.Refusal!, StringComparison.Ordinal);
    }

    [Fact]
    public void WithFolderReplaced_ARoleSwappedForAnother_IsRefusedNamingTheRoleTheFolderPlays()
    {
        // Act
        var change = MailAccountFolderComposition.WithFolderReplaced(
            """{"Folders":{"0":{"Alias":"JUNK","SpecialUse":"Junk","RemotePath":"Spam"}}}""",
            "JUNK",
            """{"Alias":"TRASH","SpecialUse":"Trash","RemotePath":"Spam"}""");

        // Assert
        Assert.Null(change.Candidate);
        Assert.Contains("Junk", change.Refusal!, StringComparison.Ordinal);
    }

    [Fact]
    public void WithFolderReplaced_ARoleGivenToAFolderDeclaredWithNone_IsRefusedNamingTheProperty()
    {
        // Act
        var change = MailAccountFolderComposition.WithFolderReplaced(
            """{"Folders":{"0":{"Alias":"SPAM","RemotePath":"Spam"}}}""",
            "SPAM",
            """{"Alias":"JUNK","SpecialUse":"Junk","RemotePath":"Spam"}""");

        // Assert
        Assert.Null(change.Candidate);
        Assert.Contains("SpecialUse", change.Refusal!, StringComparison.Ordinal);
    }

    /// <summary>Filing needs its drafts and sent folders, so the folder playing a role is not one a dialog can withdraw.</summary>
    [Fact]
    public void WithFolderRemoved_AFolderPlayingARole_IsRefusedNamingTheRole()
    {
        // Act
        var change = MailAccountFolderComposition.WithFolderRemoved(
            """{"Folders":{"0":{"Alias":"DRAFTS","SpecialUse":"Drafts","RemotePath":"Drafts"}}}""",
            "DRAFTS");

        // Assert
        Assert.Null(change.Candidate);
        Assert.Contains("Drafts", change.Refusal!, StringComparison.Ordinal);
    }

    /// <summary>A declared account carries its folders, so it is a folder route by another name rather than the way around the three.</summary>
    [Fact]
    public void WithFoldersDeclared_AnAccountDeclaringFolders_CarriesTheSwitchesOnEachOfThem()
    {
        // Arrange
        const string account =
            """{"Host":"mail.example.test","Folders":[{"Alias":"INBOX","RemotePath":"INBOX"},{"Alias":"SENT","RemotePath":"Sent"}]}""";

        // Act
        var candidate = MailAccountFolderComposition.WithFoldersDeclared(account).Candidate;

        // Assert
        Assert.True(ReadFolderSwitch(candidate, "0", "CreateIfMissing"));
        Assert.True(ReadFolderSwitch(candidate, "1", "CreateIfMissing"));
    }

    [Fact]
    public void WithFoldersDeclared_AnAccountWhoseFolderStatesSynchronizeFalse_IsRefusedNamingTheSwitch()
    {
        // Act
        var change = MailAccountFolderComposition.WithFoldersDeclared(
            """{"Folders":[{"Alias":"INBOX","RemotePath":"INBOX","Synchronize":false}]}""");

        // Assert
        Assert.Null(change.Candidate);
        Assert.Contains("Synchronize", change.Refusal!, StringComparison.Ordinal);
    }

    [Fact]
    public void WithFoldersDeclared_AnAccountDeclaringNoFolder_LeavesItsSettingsAsTheyWere()
    {
        // Act
        var candidate = MailAccountFolderComposition.WithFoldersDeclared("""{"Host":"mail.example.test"}""").Candidate;

        // Assert
        var written = JsonNode.Parse(candidate!)!.AsObject();

        Assert.Equal("mail.example.test", written["Host"]!.GetValue<string>());
        Assert.False(written.ContainsKey("Folders"));
    }

    private static bool? ReadFolderSwitch(string? json, string position, string property) =>
        JsonNode.Parse(json!)!["Folders"]![position]![property]?.GetValue<bool>();

    private static string? ReadFolderAlias(string? json, string position) =>
        JsonNode.Parse(json!)!["Folders"]![position]!["Alias"]!.GetValue<string>();
}
