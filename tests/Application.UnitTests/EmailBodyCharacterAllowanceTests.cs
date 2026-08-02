// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent;
using Xunit;

namespace MailFathom.Application.UnitTests;

/// <summary>Covers which of the two bounds on a body representation applies, and which one is then reported.</summary>
public sealed class EmailBodyCharacterAllowanceTests
{
    /// <summary>A read with budget to spare is bounded by the email's own limit, which is the ordinary case.</summary>
    [Fact]
    public void Of_BudgetWiderThanThePerRepresentationBound_AppliesThePerRepresentationBound()
    {
        // Act
        var allowance = EmailBodyCharacterAllowance.Of(
            maxCharactersPerRepresentation: 1_000,
            remainingCharactersForRead: 5_000);

        // Assert
        Assert.Equal(1_000, allowance.MaxCharacters);
        Assert.Equal(EmailBodyTruncation.BodyCharacterLimit, allowance.TruncationWhenCut);
    }

    /// <summary>Once earlier emails have spent most of the budget, what is left is the bound and is named as such.</summary>
    [Fact]
    public void Of_BudgetNarrowerThanThePerRepresentationBound_AppliesTheBudgetAndNamesIt()
    {
        // Act
        var allowance = EmailBodyCharacterAllowance.Of(
            maxCharactersPerRepresentation: 1_000,
            remainingCharactersForRead: 250);

        // Assert
        Assert.Equal(250, allowance.MaxCharacters);
        Assert.Equal(EmailBodyTruncation.ReadCharacterBudget, allowance.TruncationWhenCut);
    }

    /// <summary>Equal bounds are attributed to the email's own limit, because that one holds however much budget is left.</summary>
    [Fact]
    public void Of_BudgetEqualToThePerRepresentationBound_AppliesThePerRepresentationBound()
    {
        // Act
        var allowance = EmailBodyCharacterAllowance.Of(
            maxCharactersPerRepresentation: 1_000,
            remainingCharactersForRead: 1_000);

        // Assert
        Assert.Equal(1_000, allowance.MaxCharacters);
        Assert.Equal(EmailBodyTruncation.BodyCharacterLimit, allowance.TruncationWhenCut);
    }

    /// <summary>
    /// A budget spent to nothing, or past it once a representation returned everything that was left, yields no
    /// characters rather than a negative allowance the truncation would fail on.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-4_096)]
    public void Of_BudgetAlreadySpent_AllowsNoCharactersRatherThanANegativeCount(int remainingCharactersForRead)
    {
        // Act
        var allowance = EmailBodyCharacterAllowance.Of(
            maxCharactersPerRepresentation: 1_000,
            remainingCharactersForRead);

        // Assert
        Assert.Equal(0, allowance.MaxCharacters);
        Assert.Equal(EmailBodyTruncation.ReadCharacterBudget, allowance.TruncationWhenCut);
    }
}
