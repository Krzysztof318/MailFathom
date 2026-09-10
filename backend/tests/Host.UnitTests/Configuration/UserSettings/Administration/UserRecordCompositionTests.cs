// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Nodes;
using MailFathom.Host.Configuration.UserSettings.Administration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings.Administration;

/// <summary>
/// Covers the candidate record a targeted change composes. Nothing here judges what it produced — the binder does that
/// — so what these assert is that the act the caller named is the act the document received, and that everything the
/// caller did not name travels through untouched.
/// </summary>
public sealed class UserRecordCompositionTests
{
    [Fact]
    public void WithMailAccountAdded_ARecordDeclaringNothing_LeavesTheAccountAtTheFirstPosition()
    {
        // Arrange
        const string record = """{"DisplayName":"alex"}""";

        // Act
        var candidate = UserRecordComposition.WithMailAccountAdded(record, """{"AccountId":"primary"}""");

        // Assert
        Assert.Equal("primary", ReadAccountId(candidate, "0"));
    }

    /// <summary>Everything the caller did not name stays exactly as it was, because a targeted change is targeted.</summary>
    [Fact]
    public void WithMailAccountAdded_ARecordCarryingOtherSettings_LeavesThemUntouched()
    {
        // Arrange
        const string record = """{"DisplayName":"alex","MailAccounts":{"0":{"AccountId":"primary","Host":"mail.example.test"}}}""";

        // Act
        var candidate = UserRecordComposition.WithMailAccountAdded(record, """{"AccountId":"archive"}""");

        // Assert
        var written = JsonNode.Parse(candidate)!.AsObject();

        Assert.Equal("alex", written["DisplayName"]!.GetValue<string>());
        Assert.Equal("mail.example.test", written["MailAccounts"]!["0"]!["Host"]!.GetValue<string>());
    }

    /// <summary>
    /// A record edited by hand routinely carries the array, and both shapes flatten to the same configuration keys — so
    /// both are records this deployment reads, and neither may lose an entry on the way through.
    /// </summary>
    [Fact]
    public void WithMailAccountAdded_ARecordDeclaringItsAccountsAsAnArray_KeepsEveryOneOfThemBesideTheNewOne()
    {
        // Arrange
        const string record = """{"MailAccounts":[{"AccountId":"primary"},{"AccountId":"archive"}]}""";

        // Act
        var candidate = UserRecordComposition.WithMailAccountAdded(record, """{"AccountId":"work"}""");

        // Assert
        Assert.Equal(["primary", "archive", "work"], UserRecordComposition.MailAccountIdentifiersIn(candidate));
    }

    /// <summary>
    /// The collection is written back keyed by position whichever shape it arrived in, so a later change addressing
    /// <c>MailAccounts:1</c> reaches the entry that was at position one rather than whichever element a renumbering
    /// left there.
    /// </summary>
    [Fact]
    public void WithMailAccountAdded_ARecordDeclaringItsAccountsAsAnArray_WritesTheCollectionBackKeyedByPosition()
    {
        // Arrange
        const string record = """{"MailAccounts":[{"AccountId":"primary"}]}""";

        // Act
        var candidate = UserRecordComposition.WithMailAccountAdded(record, """{"AccountId":"archive"}""");

        // Assert
        Assert.IsType<JsonObject>(JsonNode.Parse(candidate)!["MailAccounts"]);
    }

    /// <summary>
    /// Adding a mailbox somebody already declared is a collision the naming rules refuse by name; merging it over the
    /// existing entry would leave that refusal unreachable and quietly replace their settings instead.
    /// </summary>
    [Fact]
    public void WithMailAccountAdded_AnIdentifierTheRecordAlreadyDeclares_AppendsItRatherThanReplacingTheExistingEntry()
    {
        // Arrange
        const string record = """{"MailAccounts":{"0":{"AccountId":"primary","Host":"first.example.test"}}}""";

        // Act
        var candidate = UserRecordComposition.WithMailAccountAdded(
            record,
            """{"AccountId":"primary","Host":"second.example.test"}""");

        // Assert
        Assert.Equal(["primary", "primary"], UserRecordComposition.MailAccountIdentifiersIn(candidate));
    }

    /// <summary>A record whose root is not an object states no settings at all, so there is nothing for an account to join.</summary>
    [Fact]
    public void WithMailAccountAdded_ARecordThatIsNotAJsonObject_IsRefused()
    {
        // Act & Assert
        Assert.Throws<FormatException>(
            () => UserRecordComposition.WithMailAccountAdded("[]", """{"AccountId":"primary"}"""));
    }

    /// <summary>A declaration is that account's settings, so anything else is a caller sending the wrong thing rather than a record to compose.</summary>
    [Fact]
    public void WithMailAccountAdded_ADeclarationThatIsNotAJsonObject_IsRefused()
    {
        // Act & Assert
        Assert.Throws<FormatException>(() => UserRecordComposition.WithMailAccountAdded("{}", "\"primary\""));
    }

    [Fact]
    public void WithMailAccountAdded_ADeclarationThatIsNotJsonAtAll_IsRefused()
    {
        // Act & Assert
        Assert.ThrowsAny<JsonException>(() => UserRecordComposition.WithMailAccountAdded("{}", "not json"));
    }

    /// <summary>An operator holds the identifier rather than the position, and the naming rules make it unique within the user.</summary>
    [Fact]
    public void WithMailAccountRemoved_AnIdentifierTheRecordDeclares_LeavesEveryOtherAccountInPlace()
    {
        // Arrange
        const string record = """{"MailAccounts":{"0":{"AccountId":"primary"},"1":{"AccountId":"archive"},"2":{"AccountId":"work"}}}""";

        // Act
        var candidate = UserRecordComposition.WithMailAccountRemoved(record, "archive");

        // Assert
        Assert.Equal(["primary", "work"], UserRecordComposition.MailAccountIdentifiersIn(candidate!));
    }

    /// <summary>The identifier is matched the way configuration matches a key, so a case an operator typed differently is still their account.</summary>
    [Theory]
    [InlineData("PRIMARY")]
    [InlineData("  primary  ")]
    public void WithMailAccountRemoved_AnIdentifierWrittenDifferentlyFromTheRecords_RemovesTheAccountItNames(string named)
    {
        // Arrange
        const string record = """{"MailAccounts":{"0":{"AccountId":"primary"}}}""";

        // Act
        var candidate = UserRecordComposition.WithMailAccountRemoved(record, named);

        // Assert
        Assert.Empty(UserRecordComposition.MailAccountIdentifiersIn(candidate!));
    }

    /// <summary>
    /// A record describing a collection nobody declares is one the next reader takes for an unfinished edit, and an
    /// empty object contributes no configuration key either way.
    /// </summary>
    [Fact]
    public void WithMailAccountRemoved_TheLastAccountARecordDeclares_LeavesNoCollectionAtAll()
    {
        // Arrange
        const string record = """{"DisplayName":"alex","MailAccounts":{"0":{"AccountId":"primary"}}}""";

        // Act
        var candidate = UserRecordComposition.WithMailAccountRemoved(record, "primary");

        // Assert
        Assert.False(JsonNode.Parse(candidate!)!.AsObject().ContainsKey("MailAccounts"));
    }

    /// <summary>
    /// Answering with the record unchanged would leave the caller believing a mailbox had stopped being synchronized,
    /// which is the one wrong answer this can give.
    /// </summary>
    [Fact]
    public void WithMailAccountRemoved_AnIdentifierTheRecordDeclaresNothingUnder_ReportsNothingRatherThanTheRecordUnchanged()
    {
        // Arrange
        const string record = """{"MailAccounts":{"0":{"AccountId":"primary"}}}""";

        // Act
        var candidate = UserRecordComposition.WithMailAccountRemoved(record, "archive");

        // Assert
        Assert.Null(candidate);
    }

    /// <summary>
    /// A property spelled differently is the same setting to every provider in the pipeline, so a record that spelled
    /// the collection its own way must not come back carrying both spellings — which would leave the next reader with
    /// one collection stated twice and no way to tell which of them the deployment binds.
    /// </summary>
    [Fact]
    public void WithMailAccountRemoved_ARecordSpellingTheCollectionDifferently_LeavesItStatedOnceRatherThanTwice()
    {
        // Arrange
        const string record = """{"mailaccounts":{"0":{"accountId":"primary"},"1":{"accountId":"archive"}}}""";

        // Act
        var candidate = UserRecordComposition.WithMailAccountRemoved(record, "archive");

        // Assert
        var written = JsonNode.Parse(candidate!)!.AsObject();

        Assert.Single(written, entry => entry.Key.Equals("MailAccounts", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(["primary"], UserRecordComposition.MailAccountIdentifiersIn(candidate!));
    }

    /// <summary>
    /// The positions mean what the configuration layer orders them by rather than what the document happens to list, so
    /// a record whose keys arrive out of order still reads as the record a start binds.
    /// </summary>
    [Fact]
    public void MailAccountIdentifiersIn_ARecordWhoseKeysAreOutOfOrder_ReadsThemInTheOrderTheRecordBindsIn()
    {
        // Arrange
        const string record = """{"MailAccounts":{"10":{"AccountId":"last"},"2":{"AccountId":"middle"},"0":{"AccountId":"first"}}}""";

        // Act
        var identifiers = UserRecordComposition.MailAccountIdentifiersIn(record);

        // Assert
        Assert.Equal(["first", "middle", "last"], identifiers);
    }

    /// <summary>An entry stating no identifier is what the binder refuses by name; a listing is not where that is discovered.</summary>
    [Fact]
    public void MailAccountIdentifiersIn_AnEntryDeclaringNoIdentifier_PassesOverItRatherThanReportingIt()
    {
        // Arrange
        const string record = """{"MailAccounts":{"0":{"Host":"mail.example.test"},"1":{"AccountId":"primary"}}}""";

        // Act
        var identifiers = UserRecordComposition.MailAccountIdentifiersIn(record);

        // Assert
        Assert.Equal(["primary"], identifiers);
    }

    [Fact]
    public void MailAccountIdentifiersIn_ARecordDeclaringNoCollection_ReportsNothing()
    {
        // Act
        var identifiers = UserRecordComposition.MailAccountIdentifiersIn("""{"DisplayName":"alex"}""");

        // Assert
        Assert.Empty(identifiers);
    }

    [Fact]
    public void WithFolderAdded_AnAccountDeclaringNoFolder_LeavesItAtTheFirstPosition()
    {
        // Arrange
        const string record = """{"MailAccounts":{"0":{"AccountId":"primary"}}}""";

        // Act
        var candidate = UserRecordComposition.WithFolderAdded(record, "primary", """{"Alias":"INBOX/PROJECTS"}""");

        // Assert
        Assert.Equal("INBOX/PROJECTS", ReadFolderAlias(candidate!, "0", "0"));
    }

    [Fact]
    public void WithFolderAdded_AnAccountDeclaringItsFoldersAsAnArray_WritesThemBackKeyedByPosition()
    {
        // Arrange
        const string record = """{"MailAccounts":{"0":{"AccountId":"primary","Folders":[{"Alias":"INBOX"}]}}}""";

        // Act
        var candidate = UserRecordComposition.WithFolderAdded(record, "primary", """{"Alias":"ARCHIVE"}""");

        // Assert
        Assert.Equal("INBOX", ReadFolderAlias(candidate!, "0", "0"));
        Assert.Equal("ARCHIVE", ReadFolderAlias(candidate!, "0", "1"));
    }

    /// <summary>A folder belongs to one mailbox, so every other one travels through a change about this one untouched.</summary>
    [Fact]
    public void WithFolderAdded_ARecordDeclaringSeveralAccounts_LeavesTheOnesItDoesNotName()
    {
        // Arrange
        const string record = """
            {"MailAccounts":{"0":{"AccountId":"primary"},"1":{"AccountId":"archive","Folders":{"0":{"Alias":"OLD"}}}}}
            """;

        // Act
        var candidate = UserRecordComposition.WithFolderAdded(record, "primary", """{"Alias":"INBOX"}""");

        // Assert
        Assert.Equal("INBOX", ReadFolderAlias(candidate!, "0", "0"));
        Assert.Equal("OLD", ReadFolderAlias(candidate!, "1", "0"));
    }

    [Fact]
    public void WithFolderAdded_AnAccountTheRecordDoesNotDeclare_MatchesNothing()
    {
        // Act
        var candidate = UserRecordComposition.WithFolderAdded(
            """{"MailAccounts":{"0":{"AccountId":"primary"}}}""",
            "archive",
            """{"Alias":"INBOX"}""");

        // Assert
        Assert.Null(candidate);
    }

    [Fact]
    public void WithFolderAdded_ADeclarationThatIsNotAJsonObject_IsRefused()
    {
        // Act and assert
        Assert.Throws<FormatException>(
            () => UserRecordComposition.WithFolderAdded("""{"MailAccounts":{"0":{"AccountId":"a"}}}""", "a", "[]"));
    }

    /// <summary>The depth ceiling is the record's rather than a screen's, so a caller reaching the route without the dialog meets it too.</summary>
    [Theory]
    [InlineData("INBOX/PROJECTS/2027/Q1")]
    [InlineData("A/B/C/D/E")]
    [InlineData("/INBOX/PROJECTS/2027/Q1/")]
    public void WithFolderAdded_AnAliasNestedPastThreeLevels_IsRefusedNamingTheAliasAndItsDepth(string alias)
    {
        // Act
        var refused = Assert.Throws<FormatException>(() => UserRecordComposition.WithFolderAdded(
            """{"MailAccounts":{"0":{"AccountId":"primary"}}}""",
            "primary",
            $$"""{"Alias":"{{alias}}"}"""));

        // Assert
        Assert.Contains(alias, refused.Message, StringComparison.Ordinal);
        Assert.Contains("3 levels", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>Three levels is what the design draws to, so the deepest alias a dialog can compose is one the record takes.</summary>
    [Fact]
    public void WithFolderAdded_AnAliasNestedExactlyThreeLevels_IsDeclared()
    {
        // Act
        var candidate = UserRecordComposition.WithFolderAdded(
            """{"MailAccounts":{"0":{"AccountId":"primary"}}}""",
            "primary",
            """{"Alias":"INBOX/PROJECTS/2027"}""");

        // Assert
        Assert.Equal("INBOX/PROJECTS/2027", ReadFolderAlias(candidate!, "0", "0"));
    }

    [Fact]
    public void WithFolderReplaced_AnAliasNestedPastThreeLevels_IsRefused()
    {
        // Act and assert
        Assert.Throws<FormatException>(() => UserRecordComposition.WithFolderReplaced(
            """{"MailAccounts":{"0":{"AccountId":"p","Folders":{"0":{"Alias":"INBOX/OLD"}}}}}""",
            "p",
            "INBOX/OLD",
            """{"Alias":"INBOX/PROJECTS/2027/Q1"}"""));
    }

    /// <summary>Renaming a folder is the change that proves the replacement keeps its position among its siblings.</summary>
    [Fact]
    public void WithFolderReplaced_AFolderBetweenTwoOthers_LeavesItWhereItStood()
    {
        // Arrange
        const string record = """
            {"MailAccounts":{"0":{"AccountId":"p","Folders":{"0":{"Alias":"INBOX"},"1":{"Alias":"INBOX/OLD"},"2":{"Alias":"SENT"}}}}}
            """;

        // Act
        var candidate = UserRecordComposition.WithFolderReplaced(
            record,
            "p",
            "INBOX/OLD",
            """{"Alias":"INBOX/NEW","RemotePath":"INBOX/New"}""");

        // Assert
        Assert.Equal("INBOX", ReadFolderAlias(candidate!, "0", "0"));
        Assert.Equal("INBOX/NEW", ReadFolderAlias(candidate!, "0", "1"));
        Assert.Equal("SENT", ReadFolderAlias(candidate!, "0", "2"));
    }

    /// <summary>An alias is upper-cased where it is created, so finding one is case-insensitive wherever it is matched.</summary>
    [Fact]
    public void WithFolderReplaced_AnAliasSpelledInAnotherCase_FindsTheFolder()
    {
        // Act
        var candidate = UserRecordComposition.WithFolderReplaced(
            """{"MailAccounts":{"0":{"AccountId":"p","Folders":{"0":{"Alias":"INBOX/OLD"}}}}}""",
            "p",
            "inbox/old",
            """{"Alias":"INBOX/NEW"}""");

        // Assert
        Assert.Equal("INBOX/NEW", ReadFolderAlias(candidate!, "0", "0"));
    }

    [Fact]
    public void WithFolderReplaced_AnAliasTheAccountDoesNotDeclare_MatchesNothing()
    {
        // Act
        var candidate = UserRecordComposition.WithFolderReplaced(
            """{"MailAccounts":{"0":{"AccountId":"p","Folders":{"0":{"Alias":"INBOX"}}}}}""",
            "p",
            "ARCHIVE",
            """{"Alias":"ARCHIVE"}""");

        // Assert
        Assert.Null(candidate);
    }

    [Fact]
    public void WithFolderRemoved_AnAliasTheAccountDeclares_LeavesTheOthersRenumbered()
    {
        // Arrange
        const string record = """
            {"MailAccounts":{"0":{"AccountId":"p","Folders":{"0":{"Alias":"INBOX"},"1":{"Alias":"INBOX/OLD"},"2":{"Alias":"SENT"}}}}}
            """;

        // Act
        var candidate = UserRecordComposition.WithFolderRemoved(record, "p", "INBOX/OLD");

        // Assert
        Assert.Equal("INBOX", ReadFolderAlias(candidate!, "0", "0"));
        Assert.Equal("SENT", ReadFolderAlias(candidate!, "0", "1"));
    }

    /// <summary>What a slash in an alias means to the tree a screen draws is the caller's reading, so nothing nested goes with it here.</summary>
    [Fact]
    public void WithFolderRemoved_AFolderCarryingNestedOnes_WithdrawsOnlyTheOneNamed()
    {
        // Arrange
        const string record = """
            {"MailAccounts":{"0":{"AccountId":"p","Folders":{"0":{"Alias":"INBOX/OLD"},"1":{"Alias":"INBOX/OLD/2026"}}}}}
            """;

        // Act
        var candidate = UserRecordComposition.WithFolderRemoved(record, "p", "INBOX/OLD");

        // Assert
        Assert.Equal("INBOX/OLD/2026", ReadFolderAlias(candidate!, "0", "0"));
    }

    /// <summary>An account left with no folder carries no collection at all, which is what the next reader has to see.</summary>
    [Fact]
    public void WithFolderRemoved_TheLastFolderOfAnAccount_LeavesNoCollectionBehind()
    {
        // Act
        var candidate = UserRecordComposition.WithFolderRemoved(
            """{"MailAccounts":{"0":{"AccountId":"p","Folders":{"0":{"Alias":"INBOX"}}}}}""",
            "p",
            "INBOX");

        // Assert
        Assert.Null(JsonNode.Parse(candidate!)!["MailAccounts"]!["0"]!.AsObject()["Folders"]);
    }

    [Fact]
    public void WithFolderRemoved_AnAliasTheAccountDoesNotDeclare_MatchesNothing()
    {
        // Act
        var candidate = UserRecordComposition.WithFolderRemoved(
            """{"MailAccounts":{"0":{"AccountId":"p","Folders":{"0":{"Alias":"INBOX"}}}}}""",
            "p",
            "ARCHIVE");

        // Assert
        Assert.Null(candidate);
    }

    [Fact]
    public void FolderAliasesIn_AnAccountDeclaringSeveral_ReportsThemInTheOrderTheyBind()
    {
        // Act
        var aliases = UserRecordComposition.FolderAliasesIn(
            """{"MailAccounts":{"0":{"AccountId":"p","Folders":{"1":{"Alias":"SENT"},"0":{"Alias":"INBOX"}}}}}""",
            "p");

        // Assert
        Assert.Equal(["INBOX", "SENT"], aliases);
    }

    [Fact]
    public void FolderAliasesIn_AnAccountTheRecordDoesNotDeclare_ReportsNothing()
    {
        // Act
        var aliases = UserRecordComposition.FolderAliasesIn("""{"MailAccounts":{"0":{"AccountId":"p"}}}""", "other");

        // Assert
        Assert.Empty(aliases);
    }

    private static string? ReadAccountId(string json, string position) =>
        JsonNode.Parse(json)!["MailAccounts"]![position]!["AccountId"]!.GetValue<string>();

    private static string? ReadFolderAlias(string json, string account, string position) =>
        JsonNode.Parse(json)!["MailAccounts"]![account]!["Folders"]![position]!["Alias"]!.GetValue<string>();
}
