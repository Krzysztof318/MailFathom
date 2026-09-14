// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Nodes;
using MailFathom.Host.Configuration.UserSettings.Administration;
using MailFathom.Infrastructure.Persistence.Users;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings.Administration;

/// <summary>
/// Covers the one document an account is declared, read, and saved as: the address and the name beside the settings,
/// and never the identifier this deployment generates.
/// </summary>
public sealed class MailAccountDeclarationTests
{
    [Fact]
    public void Read_ADeclarationStatingAnAddressAndAName_TakesBothOutOfTheSettings()
    {
        // Act
        var declaration = MailAccountDeclaration.Read(
            """{"EmailAddress":"  alex@example.test ","DisplayName":"work","Host":"imap.example.test"}""");

        // Assert
        Assert.Equal("alex@example.test", declaration.EmailAddress);
        Assert.Equal("work", declaration.DisplayName);
        Assert.Equal(["Host"], JsonNode.Parse(declaration.Document)!.AsObject().Select(entry => entry.Key));
    }

    /// <summary>The identifier is the deployment's to generate, so a declaration deciding one is refused whichever way it spells the key.</summary>
    [Theory]
    [InlineData("AccountId")]
    [InlineData("accountid")]
    public void Read_ADeclarationStatingAnIdentifier_IsRefused(string property)
    {
        // Act
        var refused = Assert.Throws<FormatException>(() => MailAccountDeclaration.Read(
            $$"""{"{{property}}":"work","EmailAddress":"alex@example.test","DisplayName":"work"}"""));

        // Assert
        Assert.Contains("AccountId", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>The address is what one account in the whole deployment is told apart by, so a value not shaped like one is refused.</summary>
    [Theory]
    [InlineData("""{"DisplayName":"work"}""")]
    [InlineData("""{"EmailAddress":"","DisplayName":"work"}""")]
    [InlineData("""{"EmailAddress":"alex@mail@example.test","DisplayName":"work"}""")]
    [InlineData("""{"EmailAddress":"alex example@example.test","DisplayName":"work"}""")]
    [InlineData("""{"EmailAddress":"@example.test","DisplayName":"work"}""")]
    [InlineData("""{"EmailAddress":"alex@","DisplayName":"work"}""")]
    [InlineData("""{"EmailAddress":"alex","DisplayName":"work"}""")]
    public void Read_ADeclarationStatingNoUsableAddress_IsRefused(string json)
    {
        // Act and assert
        var refused = Assert.Throws<FormatException>(() => MailAccountDeclaration.Read(json));

        Assert.Contains("EmailAddress", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_AnAddressLongerThanAPathMayCarry_IsRefused()
    {
        // Arrange
        var address = $"{new string('a', MailAccountRecord.MaximumEmailAddressLength - "@example.test".Length + 1)}@example.test";

        // Act and assert
        Assert.Throws<FormatException>(() => MailAccountDeclaration.Read(
            $$"""{"EmailAddress":"{{address}}","DisplayName":"work"}"""));
    }

    [Fact]
    public void Read_AnAddressExactlyAsLongAsAPathMayCarry_IsRead()
    {
        // Arrange
        var address = $"{new string('a', MailAccountRecord.MaximumEmailAddressLength - "@example.test".Length)}@example.test";

        // Act
        var declaration = MailAccountDeclaration.Read($$"""{"EmailAddress":"{{address}}","DisplayName":"work"}""");

        // Assert
        Assert.Equal(address, declaration.EmailAddress);
    }

    [Theory]
    [InlineData("""{"EmailAddress":"alex@example.test"}""")]
    [InlineData("""{"EmailAddress":"alex@example.test","DisplayName":"  "}""")]
    public void Read_ADeclarationStatingNoDisplayName_IsRefused(string json)
    {
        // Act and assert
        var refused = Assert.Throws<FormatException>(() => MailAccountDeclaration.Read(json));

        Assert.Contains("DisplayName", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_ADeclarationThatIsNotAJsonObject_IsRefused()
    {
        // Act and assert
        Assert.Throws<FormatException>(() => MailAccountDeclaration.Read("[]"));
    }

    /// <summary>An administrator reads the columns first, because they are what an account is recognised by in an editor.</summary>
    [Fact]
    public void Of_AnAccountRecord_PutsTheAddressAndTheNameBeforeEverySetting()
    {
        // Arrange
        var account = new MailAccountRecord(
            Guid.Parse("0197a3c0-0000-7000-8000-000000000001"),
            "alex@example.test",
            "work",
            """{"Host":"imap.example.test","UserName":"alex"}""",
            Version: 3);

        // Act
        var declaration = JsonNode.Parse(MailAccountDeclaration.Of(account))!.AsObject();

        // Assert
        Assert.Equal(["EmailAddress", "DisplayName", "Host", "UserName"], declaration.Select(entry => entry.Key));
        Assert.Equal("alex@example.test", declaration["EmailAddress"]!.GetValue<string>());
        Assert.Equal("work", declaration["DisplayName"]!.GetValue<string>());
    }

    /// <summary>An account an upgrade derived no address for still reads back, stating only what it holds.</summary>
    [Fact]
    public void Of_AnAccountHoldingNoAddress_StatesNoAddress()
    {
        // Arrange
        var account = new MailAccountRecord(Guid.NewGuid(), EmailAddress: null, "work", "{}", Version: 1);

        // Act
        var declaration = JsonNode.Parse(MailAccountDeclaration.Of(account))!.AsObject();

        // Assert
        Assert.Equal(["DisplayName"], declaration.Select(entry => entry.Key));
    }
}
