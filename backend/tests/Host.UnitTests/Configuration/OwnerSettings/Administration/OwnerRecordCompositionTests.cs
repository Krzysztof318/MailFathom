// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Nodes;
using MailFathom.Host.Configuration.OwnerSettings.Administration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.OwnerSettings.Administration;

/// <summary>
/// Covers the candidate record a targeted change composes. Nothing here judges what it produced — the binder does that
/// — so what these assert is that the act the caller named is the act the document received, and that everything the
/// caller did not name travels through untouched.
/// </summary>
public sealed class OwnerRecordCompositionTests
{
    [Fact]
    public void WithMailAccountAdded_ARecordDeclaringNothing_LeavesTheAccountAtTheFirstPosition()
    {
        // Arrange
        const string record = """{"DisplayName":"alex"}""";

        // Act
        var candidate = OwnerRecordComposition.WithMailAccountAdded(record, """{"AccountId":"primary"}""");

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
        var candidate = OwnerRecordComposition.WithMailAccountAdded(record, """{"AccountId":"archive"}""");

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
        var candidate = OwnerRecordComposition.WithMailAccountAdded(record, """{"AccountId":"work"}""");

        // Assert
        Assert.Equal(["primary", "archive", "work"], OwnerRecordComposition.MailAccountIdentifiersIn(candidate));
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
        var candidate = OwnerRecordComposition.WithMailAccountAdded(record, """{"AccountId":"archive"}""");

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
        var candidate = OwnerRecordComposition.WithMailAccountAdded(
            record,
            """{"AccountId":"primary","Host":"second.example.test"}""");

        // Assert
        Assert.Equal(["primary", "primary"], OwnerRecordComposition.MailAccountIdentifiersIn(candidate));
    }

    /// <summary>A record whose root is not an object states no settings at all, so there is nothing for an account to join.</summary>
    [Fact]
    public void WithMailAccountAdded_ARecordThatIsNotAJsonObject_IsRefused()
    {
        // Act & Assert
        Assert.Throws<FormatException>(
            () => OwnerRecordComposition.WithMailAccountAdded("[]", """{"AccountId":"primary"}"""));
    }

    /// <summary>A declaration is that account's settings, so anything else is a caller sending the wrong thing rather than a record to compose.</summary>
    [Fact]
    public void WithMailAccountAdded_ADeclarationThatIsNotAJsonObject_IsRefused()
    {
        // Act & Assert
        Assert.Throws<FormatException>(() => OwnerRecordComposition.WithMailAccountAdded("{}", "\"primary\""));
    }

    [Fact]
    public void WithMailAccountAdded_ADeclarationThatIsNotJsonAtAll_IsRefused()
    {
        // Act & Assert
        Assert.ThrowsAny<JsonException>(() => OwnerRecordComposition.WithMailAccountAdded("{}", "not json"));
    }

    /// <summary>An operator holds the identifier rather than the position, and the naming rules make it unique within the owner.</summary>
    [Fact]
    public void WithMailAccountRemoved_AnIdentifierTheRecordDeclares_LeavesEveryOtherAccountInPlace()
    {
        // Arrange
        const string record = """{"MailAccounts":{"0":{"AccountId":"primary"},"1":{"AccountId":"archive"},"2":{"AccountId":"work"}}}""";

        // Act
        var candidate = OwnerRecordComposition.WithMailAccountRemoved(record, "archive");

        // Assert
        Assert.Equal(["primary", "work"], OwnerRecordComposition.MailAccountIdentifiersIn(candidate!));
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
        var candidate = OwnerRecordComposition.WithMailAccountRemoved(record, named);

        // Assert
        Assert.Empty(OwnerRecordComposition.MailAccountIdentifiersIn(candidate!));
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
        var candidate = OwnerRecordComposition.WithMailAccountRemoved(record, "primary");

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
        var candidate = OwnerRecordComposition.WithMailAccountRemoved(record, "archive");

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
        var candidate = OwnerRecordComposition.WithMailAccountRemoved(record, "archive");

        // Assert
        var written = JsonNode.Parse(candidate!)!.AsObject();

        Assert.Single(written, entry => entry.Key.Equals("MailAccounts", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(["primary"], OwnerRecordComposition.MailAccountIdentifiersIn(candidate!));
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
        var identifiers = OwnerRecordComposition.MailAccountIdentifiersIn(record);

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
        var identifiers = OwnerRecordComposition.MailAccountIdentifiersIn(record);

        // Assert
        Assert.Equal(["primary"], identifiers);
    }

    [Fact]
    public void MailAccountIdentifiersIn_ARecordDeclaringNoCollection_ReportsNothing()
    {
        // Act
        var identifiers = OwnerRecordComposition.MailAccountIdentifiersIn("""{"DisplayName":"alex"}""");

        // Assert
        Assert.Empty(identifiers);
    }

    private static string? ReadAccountId(string json, string position) =>
        JsonNode.Parse(json)!["MailAccounts"]![position]!["AccountId"]!.GetValue<string>();
}
