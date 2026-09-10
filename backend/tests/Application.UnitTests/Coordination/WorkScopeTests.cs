// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Coordination;
using Xunit;

namespace MailFathom.Application.UnitTests.Coordination;

/// <summary>
/// A scope is what an operator reads back when they ask who holds what, and it is the primary key the exclusion rests
/// on. Both are why it is bounded and free of anything unprintable before it reaches the database.
/// </summary>
public sealed class WorkScopeTests
{
    [Fact]
    public void Create_AComposedName_KeepsTheTextTheCallerComposed()
    {
        // Act
        var scope = WorkScope.Create("mail-account:personal");

        // Assert
        Assert.Equal("mail-account:personal", scope.Value);
    }

    [Fact]
    public void Create_SurroundingWhitespace_TrimsItSoTwoWritingsOfOneScopeAreOneKey()
    {
        // Act
        var scope = WorkScope.Create("  stored-email-embedding  ");

        // Assert
        Assert.Equal("stored-email-embedding", scope.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ABlankName_IsRefusedBecauseNothingIsNamedByIt(string value)
    {
        // Act and assert
        Assert.Throws<ArgumentException>(() => WorkScope.Create(value));
    }

    /// <summary>The column is bounded, so a scope past that bound is refused here rather than by the provider.</summary>
    [Fact]
    public void Create_ANameLongerThanTheColumnHolds_IsRefused()
    {
        // Arrange
        var tooLong = new string('a', WorkScope.MaximumLength + 1);

        // Act and assert
        Assert.Throws<ArgumentException>(() => WorkScope.Create(tooLong));
    }

    [Fact]
    public void Create_ANameOfExactlyTheGreatestLength_IsAccepted()
    {
        // Arrange
        var longest = new string('a', WorkScope.MaximumLength);

        // Act
        var scope = WorkScope.Create(longest);

        // Assert
        Assert.Equal(WorkScope.MaximumLength, scope.Value.Length);
    }

    /// <summary>A control character would make the scope unreadable in the query an operator asks who holds what with.</summary>
    [Fact]
    public void Create_ANameCarryingAControlCharacter_IsRefused()
    {
        // Act and assert
        Assert.Throws<ArgumentException>(() => WorkScope.Create("stored-email\u0007embedding"));
    }

    /// <summary>Two writings of one scope are one key, which is what makes the exclusion a comparison rather than a lookup.</summary>
    [Fact]
    public void Equals_TwoScopesOverOneName_AreTheSameScope()
    {
        // Act
        var scope = WorkScope.Create("content-move");
        var sameScope = WorkScope.Create("content-move");

        // Assert
        Assert.Equal(scope, sameScope);
    }
}
