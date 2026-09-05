// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Retrieval;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Planning;

/// <summary>Covers what one question decided: that the composition follows the intent, and follows it alone.</summary>
public sealed class DiscoveryRunPlanTests
{
    private static readonly EmailKnowledgeBounds Bounds = EmailKnowledgeBounds.Default;

    /// <summary>The mapping is an invariant of the code rather than an instruction a model may drift from.</summary>
    [Theory]
    [InlineData(DiscoveryIntent.FindFactIdentity, PresentationBlockType.AnswerIdentity)]
    [InlineData(DiscoveryIntent.TrackChangeIdentity, PresentationBlockType.TimelineIdentity)]
    [InlineData(DiscoveryIntent.CompareTermsIdentity, PresentationBlockType.FactTableIdentity)]
    [InlineData(DiscoveryIntent.FindDocumentsIdentity, PresentationBlockType.AttachmentGalleryIdentity)]
    public void Compose_AQuestionOfEachNamedKind_OpensWithTheBlockThatKindNames(
        string intentIdentity,
        string blockIdentity)
    {
        // Arrange
        Assert.True(DiscoveryIntent.TryParse(intentIdentity, out var intent));

        // Act
        var plan = DiscoveryRunPlan.Compose(intent, PlanOf("terms"));

        // Assert
        Assert.Equal(blockIdentity, plan.Composition[0].Identity);
    }

    /// <summary>A question fitting none of the named kinds is answered rather than refused.</summary>
    [Fact]
    public void Compose_AQuestionOfNoNamedKind_IsStillAValidComposition()
    {
        // Act
        var plan = DiscoveryRunPlan.Compose(DiscoveryIntent.Unclassified, PlanOf("anything"));

        // Assert
        Assert.Equal(
            [PresentationBlockType.Answer, PresentationBlockType.EvidenceList],
            plan.Composition);
    }

    /// <summary>A reader checks a result the same way whatever was asked, so the sources are always on it.</summary>
    [Fact]
    public void Compose_AnyIntent_EndsWithTheEvidenceBehindTheAnswer()
    {
        // Act
        var compositions = DiscoveryIntent.All
            .Select(intent => DiscoveryRunPlan.Compose(intent, PlanOf("terms")).Composition[^1])
            .ToArray();

        // Assert
        Assert.Equal(
            [.. Enumerable.Repeat(PresentationBlockType.EvidenceList, DiscoveryIntent.All.Count)],
            compositions);
    }

    /// <summary>The same question over the same scope yields the same composition, because nothing but the intent decides it.</summary>
    [Fact]
    public void Compose_TheSameIntentTwiceOverDifferentLookups_ComposesTheSameBlocks()
    {
        // Act
        var first = DiscoveryRunPlan.Compose(DiscoveryIntent.CompareTerms, PlanOf("first wording"));
        var second = DiscoveryRunPlan.Compose(DiscoveryIntent.CompareTerms, PlanOf("another wording entirely"));

        // Assert
        Assert.Equal(first.Composition, second.Composition);
    }

    [Fact]
    public void Compose_AnIntentThatNamesNothing_IsRefused()
    {
        // Act
        var failure = Assert.Throws<ArgumentException>(() =>
            DiscoveryRunPlan.Compose(default, PlanOf("terms")));

        // Assert
        Assert.Equal("intent", failure.ParamName);
    }

    private static RetrievalPlan PlanOf(string queryText) =>
        RetrievalPlan.Create(Bounds, [EmailKnowledgeQuery.ForText(queryText)], sufficientPassages: 5);
}
