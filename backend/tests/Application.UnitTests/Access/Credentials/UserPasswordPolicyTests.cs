// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using Xunit;

namespace MailFathom.Application.UnitTests.Access.Credentials;

/// <summary>Covers which passwords this deployment stores, and what a refusal is allowed to say about the one it did not.</summary>
public sealed class UserPasswordPolicyTests
{
    [Fact]
    public void FindRefusal_APasswordOfTheShortestAcceptedLength_IsAccepted()
    {
        // Arrange
        var password = new string('p', UserPasswordPolicy.MinimumLength);

        // Act
        var refusal = UserPasswordPolicy.FindRefusal(password);

        // Assert
        Assert.Null(refusal);
    }

    [Fact]
    public void FindRefusal_APasswordOneCharacterShortOfTheFloor_IsRefused()
    {
        // Arrange
        var password = new string('p', UserPasswordPolicy.MinimumLength - 1);

        // Act
        var refusal = UserPasswordPolicy.FindRefusal(password);

        // Assert
        Assert.NotNull(refusal);
    }

    [Fact]
    public void FindRefusal_APasswordOfTheGreatestAcceptedLength_IsAccepted()
    {
        // Arrange
        var password = new string('p', UserPasswordPolicy.MaximumLength);

        // Act
        var refusal = UserPasswordPolicy.FindRefusal(password);

        // Assert
        Assert.Null(refusal);
    }

    /// <summary>The ceiling is what keeps hashing bounded work, so it holds against whoever can reach the administrative surface.</summary>
    [Fact]
    public void FindRefusal_APasswordOneCharacterPastTheCeiling_IsRefused()
    {
        // Arrange
        var password = new string('p', UserPasswordPolicy.MaximumLength + 1);

        // Act
        var refusal = UserPasswordPolicy.FindRefusal(password);

        // Assert
        Assert.NotNull(refusal);
    }

    /// <summary>A password no client could present whole is refused where it is stored rather than where it fails to arrive.</summary>
    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\t")]
    [InlineData("\0")]
    public void FindRefusal_APasswordCarryingAControlCharacter_IsRefused(string control)
    {
        // Arrange
        var password = new string('p', UserPasswordPolicy.MinimumLength) + control;

        // Act
        var refusal = UserPasswordPolicy.FindRefusal(password);

        // Assert
        Assert.NotNull(refusal);
        Assert.Contains("header", refusal, StringComparison.Ordinal);
    }

    /// <summary>Length is the whole strength rule, so nothing about composition refuses a long password of one character class.</summary>
    [Fact]
    public void FindRefusal_ALongPassphraseOfOneCharacterClass_IsAcceptedBecauseCompositionIsNotARule()
    {
        // Act
        var refusal = UserPasswordPolicy.FindRefusal("correcthorsebatterystaple");

        // Assert
        Assert.Null(refusal);
    }

    /// <summary>
    /// A refusal reaches a terminal, a server log, and an HTTP response, so it says what the rule is and never what was
    /// written — not the value, not a fragment of it, and not the length that was refused.
    /// </summary>
    [Fact]
    public void FindRefusal_ARefusedPassword_IsDescribedWithoutRepeatingAnyPartOfIt()
    {
        // Arrange
        const string Password = "hunter2\axyz";

        // Act
        var refusal = UserPasswordPolicy.FindRefusal(Password);

        // Assert
        Assert.NotNull(refusal);
        Assert.DoesNotContain("hunter", refusal, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Password.Length.ToString(null as IFormatProvider), refusal, StringComparison.Ordinal);
    }
}
