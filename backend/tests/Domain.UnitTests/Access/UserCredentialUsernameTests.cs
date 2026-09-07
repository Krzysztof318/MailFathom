// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using Xunit;

namespace MailFathom.Domain.UnitTests.Access;

/// <summary>Covers which written names become a username, and the single form the ones that do are folded into.</summary>
public sealed class UserCredentialUsernameTests
{
    [Fact]
    public void TryCreate_ANameTypedWithCapitalsAndSpace_FoldsToTheFormThatIsStoredAndCompared()
    {
        // Act
        var read = UserCredentialUsername.TryCreate("  Ada.Lovelace@Example.Org  ", out var username);

        // Assert
        Assert.True(read);
        Assert.Equal("ada.lovelace@example.org", username.Value);
    }

    /// <summary>Two people typing one name differently must reach one credential, which is the whole reason for folding.</summary>
    [Fact]
    public void TryCreate_TwoSpellingsOfOneName_ProduceEqualUsernames()
    {
        // Act
        UserCredentialUsername.TryCreate("User", out var typed);
        UserCredentialUsername.TryCreate("user", out var stored);

        // Assert
        Assert.Equal(stored, typed);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("user.name")]
    [InlineData("user-name")]
    [InlineData("user_name")]
    [InlineData("user+tag")]
    [InlineData("user@example.org")]
    [InlineData("0wner1")]
    public void TryCreate_EveryAcceptedCharacterClass_ReadsAsAUsername(string written)
    {
        // Act
        var read = UserCredentialUsername.TryCreate(written, out var username);

        // Assert
        Assert.True(read);
        Assert.Equal(written, username.Value);
    }

    /// <summary>
    /// RFC 7617 splits a Basic credential at the first colon, so a username carrying one could never be presented whole
    /// and would authenticate as the shorter name in front of it. It is refused here rather than at the header.
    /// </summary>
    [Fact]
    public void TryCreate_ANameCarryingAColon_IsRefusedBecauseTheTransportWouldSplitIt()
    {
        // Act
        var read = UserCredentialUsername.TryCreate("user:name", out var username);

        // Assert
        Assert.False(read);
        Assert.False(username.IsSpecified);
    }

    [Theory]
    [InlineData("user name")]
    [InlineData("user/name")]
    [InlineData("user\\name")]
    [InlineData("user\"name")]
    [InlineData("user\tname")]
    [InlineData("właściciel")]
    public void TryCreate_ANameOutsideTheAcceptedSet_IsRefused(string written)
    {
        // Act
        var read = UserCredentialUsername.TryCreate(written, out _);

        // Assert
        Assert.False(read);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryCreate_NothingWritten_IsRefusedRatherThanBecomingAnEmptyUsername(string? written)
    {
        // Act
        var read = UserCredentialUsername.TryCreate(written, out var username);

        // Assert
        Assert.False(read);
        Assert.False(username.IsSpecified);
    }

    [Fact]
    public void TryCreate_ANameOfTheGreatestAcceptedLength_ReadsAsAUsername()
    {
        // Arrange
        var written = new string('a', UserCredentialUsername.MaximumLength);

        // Act
        var read = UserCredentialUsername.TryCreate(written, out var username);

        // Assert
        Assert.True(read);
        Assert.Equal(UserCredentialUsername.MaximumLength, username.Value.Length);
    }

    [Fact]
    public void TryCreate_ANameOneCharacterPastTheBound_IsRefused()
    {
        // Arrange
        var written = new string('a', UserCredentialUsername.MaximumLength + 1);

        // Act
        var read = UserCredentialUsername.TryCreate(written, out _);

        // Assert
        Assert.False(read);
    }

    /// <summary>The bound is on the canonical form, so space a person typed around a name of the full length is not what pushes it over.</summary>
    [Fact]
    public void TryCreate_ANameOfTheGreatestLengthTypedWithSurroundingSpace_ReadsAsAUsername()
    {
        // Arrange
        var written = $"  {new string('a', UserCredentialUsername.MaximumLength)}  ";

        // Act
        var read = UserCredentialUsername.TryCreate(written, out _);

        // Assert
        Assert.True(read);
    }

    [Fact]
    public void Create_ANameThisDeploymentDoesNotAccept_ThrowsNamingTheAcceptedForm()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => UserCredentialUsername.Create("user:name"));

        // Assert
        Assert.Contains(UserCredentialUsername.MaximumLength.ToString(null as IFormatProvider), refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Value_TheStructDefault_ThrowsRatherThanAnsweringForAValueItDoesNotHold()
    {
        // Arrange
        UserCredentialUsername unspecified = default;

        // Act & Assert
        Assert.False(unspecified.IsSpecified);
        Assert.Throws<InvalidOperationException>(() => unspecified.Value);
    }

    /// <summary>A username reaches refusals and log lines, so the unspecified default has to render as something rather than throwing there.</summary>
    [Fact]
    public void ToString_TheStructDefault_SaysItNamesNothing()
    {
        // Arrange
        UserCredentialUsername unspecified = default;

        // Act
        var rendered = unspecified.ToString();

        // Assert
        Assert.Equal("(unspecified)", rendered);
    }

    [Fact]
    public void DescribeAcceptedForm_NamesTheBoundAndTheFoldingSoARefusalTellsAnOperatorWhatToWrite()
    {
        // Act
        var described = UserCredentialUsername.DescribeAcceptedForm();

        // Assert
        Assert.Contains(UserCredentialUsername.MaximumLength.ToString(null as IFormatProvider), described, StringComparison.Ordinal);
        Assert.Contains("lowercased", described, StringComparison.Ordinal);
    }
}
