// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Host.Configuration.Answering;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Answering;

/// <summary>Covers the step between a bound answering declaration and the four ceilings the boundaries enforce.</summary>
public sealed class MailAnsweringBudgetMapperTests
{
    [Fact]
    public void Map_ADeclaration_CarriesEveryCeilingOntoTheBoundaryThatEnforcesIt()
    {
        // Arrange
        var settings = new MailAnsweringOptions
        {
            MaxPassagesPerRetrieval = 5,
            MaxCharactersPerPassage = 900,
            MaxRetrievedCharactersPerRun = 9_000,
            MaxProviderCallsPerRun = 4,
            MaxTokensPerRun = 40_000,
            MaxAnswerCharacters = 12_000,
            MaxCitations = 12,
            AggregatePeriod = TimeSpan.FromMinutes(30),
            MaxRunsPerPeriod = 15,
            MaxTokensPerPeriod = 150_000,
        };

        // Act
        var budget = MailAnsweringBudgetMapper.Map(settings);

        // Assert
        Assert.Equal(5, budget.Retrieval.MaximumPassages);
        Assert.Equal(900, budget.Retrieval.MaximumCharactersPerPassage);
        Assert.Equal(9_000, budget.Run.MaximumRetrievedCharacters);
        Assert.Equal(4, budget.Run.MaximumProviderCalls);
        Assert.Equal(40_000L, budget.Run.MaximumTokens);
        Assert.Equal(12_000, budget.Answer.MaximumAnswerCharacters);
        Assert.Equal(12, budget.Answer.MaximumCitations);
        Assert.Equal(TimeSpan.FromMinutes(30), budget.Period.Period);
        Assert.Equal(15, budget.Period.MaximumRuns);
        Assert.Equal(150_000L, budget.Period.MaximumTokens);
    }

    /// <summary>An absent section binds to the defaults, and the defaults are what the value objects state — never a second copy of them.</summary>
    [Fact]
    public void Map_ASectionNobodyWrote_IsTheBudgetTheValueObjectsDeclare()
    {
        // Act
        var budget = MailAnsweringBudgetMapper.Map(new MailAnsweringOptions());

        // Assert
        Assert.Equal(MailAnsweringBudget.Default, budget);
    }

    [Fact]
    public void Map_WithoutADeclaration_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => MailAnsweringBudgetMapper.Map(null!));
    }
}
