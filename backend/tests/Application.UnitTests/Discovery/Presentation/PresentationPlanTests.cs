// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Blocks;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Presentation;

/// <summary>Covers what a plan holds together that no single block can.</summary>
public sealed class PresentationPlanTests
{
    [Fact]
    public void Compose_APlanThisBuildWrote_StampsTheSchemaVersionItWroteAgainst()
    {
        // Act
        var plan = PresentationPlanExample.Compose();

        // Assert
        Assert.Equal(PresentationPlan.CurrentSchemaVersion, plan.SchemaVersion);
    }

    [Fact]
    public void Compose_APlanHoldingOneBlockOfEveryType_KeepsThemInTheOrderGiven()
    {
        // Act
        var plan = PresentationPlanExample.Compose();

        // Assert
        Assert.Equal(
            [.. PresentationBlockType.All],
            [.. plan.Blocks.Select(block => block.Type)]);
    }

    /// <summary>The one way a citation contract fails quietly: a block naming a source the plan never declared.</summary>
    [Fact]
    public void Constructor_ABlockNamingACitationThePlanDoesNotDeclare_IsRefused()
    {
        // Arrange
        var block = new AnswerBlock(
            PresentationPlanExample.Supported(),
            PresentationPlanExample.Text("They accepted."),
            PresentationConfidence.High);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => PresentationPlan.Compose([block], [], []));
    }

    /// <summary>A reference from inside a block is a reference, which is why the check reaches into one.</summary>
    [Fact]
    public void Constructor_AnEntryNamingACitationThePlanDoesNotDeclare_IsRefused()
    {
        // Arrange
        var block = new TimelineBlock(
            PresentationEvidence.Unsupported(PresentationFreshness.Unknown),
            [
                new TimelineEntry(
                    PresentationPlanExample.ObservedAt,
                    PresentationPlanExample.Text("Figure revised"),
                    PresentationPlanExample.Text("Renewal"),
                    [PresentationPlanExample.SecondCitation]),
            ]);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => PresentationPlan.Compose([block], [], []));
    }

    [Fact]
    public void Constructor_TheSameCitationIdentifierDeclaredTwice_IsRefused()
    {
        // Arrange
        var citation = new PresentationCitation(
            PresentationPlanExample.FirstCitation,
            new EmailCitationTarget(StoredEmailId.Create(new Guid("11111111-1111-1111-1111-111111111111"))),
            PresentationPlanExample.Text("Revised figures"));

        var block = new AnswerBlock(
            PresentationPlanExample.Supported(),
            PresentationPlanExample.Text("They accepted."),
            PresentationConfidence.High);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => PresentationPlan.Compose([block], [citation, citation], []));
    }

    /// <summary>A citation declared under no name is one no block could ever point at.</summary>
    [Fact]
    public void Constructor_ACitationDeclaredUnderTheUnspecifiedDefault_IsRefused()
    {
        // Arrange
        var citation = new PresentationCitation(
            default,
            new EmailCitationTarget(StoredEmailId.Create(new Guid("11111111-1111-1111-1111-111111111111"))),
            PresentationPlanExample.Text("Revised figures"));

        var block = new AnswerBlock(
            PresentationEvidence.Unsupported(PresentationFreshness.Unknown),
            PresentationPlanExample.Text("Nothing found says either way."),
            PresentationConfidence.Low);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => PresentationPlan.Compose([block], [citation], []));
    }

    [Fact]
    public void Constructor_TheSameLimitationStatedTwice_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => PresentationPlan.Compose(
            PresentationPlanExample.EveryBlock(),
            PresentationPlanExample.Citations(),
            [PresentationLimitation.RetrievalTruncated, PresentationLimitation.RetrievalTruncated]));
    }

    /// <summary>A plan with no block is a run that produced nothing, which is reported rather than presented.</summary>
    [Fact]
    public void Constructor_NoBlocks_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => PresentationPlan.Compose([], [], []));
    }

    [Fact]
    public void Constructor_MoreBlocksThanTheBound_IsRefused()
    {
        // Arrange
        var tooMany = Enumerable
            .Range(0, PresentationPlan.MaxBlocks + 1)
            .Select(_ => (PresentationBlock)new AnswerBlock(
                PresentationEvidence.Unsupported(PresentationFreshness.Unknown),
                PresentationPlanExample.Text("Nothing found says either way."),
                PresentationConfidence.Low))
            .ToArray();

        // Act, Assert
        Assert.Throws<ArgumentException>(() => PresentationPlan.Compose(tooMany, [], []));
    }

    /// <summary>A schema version below one would say the plan was written against no revision at all.</summary>
    [Fact]
    public void Constructor_ASchemaVersionBelowOne_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new PresentationPlan(
            0,
            PresentationPlanExample.EveryBlock(),
            PresentationPlanExample.Citations(),
            []));
    }

    /// <summary>A run that reached everything it was asked about states no limitation, which is the ordinary case.</summary>
    [Fact]
    public void Constructor_ARunThatReachedEverything_StatesNoLimitation()
    {
        // Arrange
        var block = new AnswerBlock(
            PresentationEvidence.Unsupported(PresentationFreshness.Unknown),
            PresentationPlanExample.Text("Nothing in the mail says either way."),
            PresentationConfidence.Low);

        // Act
        var plan = PresentationPlan.Compose([block], [], []);

        // Assert
        Assert.Empty(plan.Limitations);
    }
}
