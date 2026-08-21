// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Chunking;

/// <summary>Covers the rule sets that can be stated, and the ones that would leave a cut with nowhere to go.</summary>
public sealed class EmailChunkingRulesTests
{
    /// <summary>What every chunk stored today was cut to, stated once so a change to it is a change to this test.</summary>
    [Fact]
    public void Current_IsTheRuleSetChunksAreStoredUnder()
    {
        // Act
        var rules = EmailChunkingRules.Current;

        // Assert
        Assert.Equal(1, rules.RuleSetVersion);
        Assert.Equal(1000, rules.TargetCharacterCount);
        Assert.Equal(250, rules.MinimumCharacterCount);
        Assert.Equal(200, rules.OverlapCharacterCount);
        Assert.Equal(["\n\n", "\n", " "], rules.BoundarySeparators);
        Assert.Equal(EmailChunkSourceForm.TrimmedText, rules.SourceForm);
    }

    /// <summary>The ladder is read in order, so a caller must not be able to reorder it after the fact.</summary>
    [Fact]
    public void Create_ASeparatorLadder_KeepsAnOrderTheCallerCannotLaterChange()
    {
        // Arrange
        var separators = new List<string> { "\n\n", "\n" };
        var rules = EmailChunkingRules.Create(
            ruleSetVersion: 2,
            targetCharacterCount: 100,
            minimumCharacterCount: 10,
            overlapCharacterCount: 10,
            separators,
            EmailChunkSourceForm.TrimmedText);

        // Act
        separators.Clear();

        // Assert
        Assert.Equal(["\n\n", "\n"], rules.BoundarySeparators);
    }

    /// <summary>An overlap reaching the target would restart every chunk where the last one began, and never advance.</summary>
    [Fact]
    public void Create_AnOverlapThatDoesNotFitInsideTheTarget_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => EmailChunkingRules.Create(
            ruleSetVersion: 2,
            targetCharacterCount: 100,
            minimumCharacterCount: 10,
            overlapCharacterCount: 100,
            ["\n"],
            EmailChunkSourceForm.TrimmedText));
    }

    /// <summary>A minimum at the target would refuse every separator break the window could offer.</summary>
    [Fact]
    public void Create_AMinimumThatDoesNotFitInsideTheTarget_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => EmailChunkingRules.Create(
            ruleSetVersion: 2,
            targetCharacterCount: 100,
            minimumCharacterCount: 100,
            overlapCharacterCount: 10,
            ["\n"],
            EmailChunkSourceForm.TrimmedText));
    }

    /// <summary>A count that is not positive describes no window a cut could walk.</summary>
    [Theory]
    [InlineData(0, 100, 10, 10)]
    [InlineData(1, 0, 10, 10)]
    [InlineData(1, 100, 0, 10)]
    [InlineData(1, 100, 10, -1)]
    public void Create_ACountOutsideItsRange_IsRefused(
        int ruleSetVersion,
        int targetCharacterCount,
        int minimumCharacterCount,
        int overlapCharacterCount)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => EmailChunkingRules.Create(
            ruleSetVersion,
            targetCharacterCount,
            minimumCharacterCount,
            overlapCharacterCount,
            ["\n"],
            EmailChunkSourceForm.TrimmedText));
    }

    /// <summary>An empty separator matches where every chunk begins, so it would end each one at zero characters.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Create_AnEmptySeparator_IsRefused(string? separator)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => EmailChunkingRules.Create(
            ruleSetVersion: 2,
            targetCharacterCount: 100,
            minimumCharacterCount: 10,
            overlapCharacterCount: 10,
            ["\n", separator!],
            EmailChunkSourceForm.TrimmedText));
    }

    /// <summary>No rules can be stated without a ladder to state them against.</summary>
    [Fact]
    public void Create_WithoutASeparatorLadder_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => EmailChunkingRules.Create(
            ruleSetVersion: 2,
            targetCharacterCount: 100,
            minimumCharacterCount: 10,
            overlapCharacterCount: 10,
            null!,
            EmailChunkSourceForm.TrimmedText));
    }
}
