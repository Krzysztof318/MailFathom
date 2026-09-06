// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Retrieval;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Planning;

/// <summary>Covers what a plan will and will not run one question as.</summary>
public sealed class RetrievalPlanTests
{
    private static readonly EmailKnowledgeBounds Bounds = EmailKnowledgeBounds.Default;

    [Fact]
    public void Create_LookupsInTheOrderTheyAreWorthRunning_KeepsThatOrder()
    {
        // Arrange
        IReadOnlyList<EmailKnowledgeQuery> lookups =
        [
            EmailKnowledgeQuery.ForText("invoice"),
            EmailKnowledgeQuery.ForText("faktura"),
        ];

        // Act
        var plan = RetrievalPlan.Create(Bounds, lookups, sufficientPassages: 4);

        // Assert
        Assert.Equal(["invoice", "faktura"], plan.Lookups.Select(lookup => lookup.QueryText));
    }

    [Fact]
    public void Create_NoLookup_IsRefusedBecauseItRetrievesNothing()
    {
        // Act
        var failure = Assert.Throws<ArgumentException>(() =>
            RetrievalPlan.Create(Bounds, [], sufficientPassages: 4));

        // Assert
        Assert.Equal("lookups", failure.ParamName);
    }

    /// <summary>Each lookup is a ranked read over a mailbox, so the count is what stops one question sweeping it.</summary>
    [Fact]
    public void Create_MoreLookupsThanTheBoundAdmits_IsRefused()
    {
        // Arrange
        var lookups = Enumerable
            .Range(0, RetrievalPlan.MaximumLookups + 1)
            .Select(position => EmailKnowledgeQuery.ForText($"wording {position}"))
            .ToArray();

        // Act
        var failure = Assert.Throws<ArgumentException>(() =>
            RetrievalPlan.Create(Bounds, lookups, sufficientPassages: 4));

        // Assert
        Assert.Equal("lookups", failure.ParamName);
    }

    /// <summary>Enough cannot exceed what the deployment would return, or the plan would never stop for it.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_EnoughBelowOne_IsRefused(int sufficientPassages)
    {
        // Act
        var failure = Assert.Throws<ArgumentOutOfRangeException>(() =>
            RetrievalPlan.Create(Bounds, [EmailKnowledgeQuery.ForText("invoice")], sufficientPassages));

        // Assert
        Assert.Equal("sufficientPassages", failure.ParamName);
    }

    [Fact]
    public void Create_EnoughAboveWhatRetrievalReturns_IsRefused()
    {
        // Act
        var failure = Assert.Throws<ArgumentOutOfRangeException>(() => RetrievalPlan.Create(
            Bounds,
            [EmailKnowledgeQuery.ForText("invoice")],
            Bounds.MaximumPassages + 1));

        // Assert
        Assert.Equal("sufficientPassages", failure.ParamName);
    }

    /// <summary>The plan holds its own copy, so a caller mutating the list it passed changes no plan.</summary>
    [Fact]
    public void Create_ALookupListTheCallerGoesOnEditing_DoesNotChangeThePlan()
    {
        // Arrange
        var lookups = new List<EmailKnowledgeQuery> { EmailKnowledgeQuery.ForText("invoice") };
        var plan = RetrievalPlan.Create(Bounds, lookups, sufficientPassages: 4);

        // Act
        lookups.Add(EmailKnowledgeQuery.ForText("faktura"));

        // Assert
        Assert.Single(plan.Lookups);
    }
}
