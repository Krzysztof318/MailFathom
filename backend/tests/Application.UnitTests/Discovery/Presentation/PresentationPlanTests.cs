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
        Assert.Throws<ArgumentException>(() => PresentationPlan.Compose([block], [], [], []));
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
        Assert.Throws<ArgumentException>(() => PresentationPlan.Compose([block], [], [], []));
    }

    [Fact]
    public void Constructor_TheSameCitationIdentifierDeclaredTwice_IsRefused()
    {
        // Arrange
        var citation = new PresentationCitation(
            PresentationPlanExample.FirstCitation,
            new EmailCitationTarget(StoredEmailId.Create(new Guid("11111111-1111-1111-1111-111111111111"))),
            PresentationPlanExample.Text("Revised figures"),
            PresentationSourceMedium.Written);

        var block = new AnswerBlock(
            PresentationPlanExample.Supported(),
            PresentationPlanExample.Text("They accepted."),
            PresentationConfidence.High);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => PresentationPlan.Compose([block], [citation, citation], [], []));
    }

    /// <summary>A citation declared under no name is one no block could ever point at.</summary>
    [Fact]
    public void Constructor_ACitationDeclaredUnderTheUnspecifiedDefault_IsRefused()
    {
        // Arrange
        var citation = new PresentationCitation(
            default,
            new EmailCitationTarget(StoredEmailId.Create(new Guid("11111111-1111-1111-1111-111111111111"))),
            PresentationPlanExample.Text("Revised figures"),
            PresentationSourceMedium.Written);

        var block = new AnswerBlock(
            PresentationEvidence.Unsupported(PresentationFreshness.Unknown),
            PresentationPlanExample.Text("Nothing found says either way."),
            PresentationConfidence.Low);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => PresentationPlan.Compose([block], [citation], [], []));
    }

    [Fact]
    public void Constructor_TheSameLimitationStatedTwice_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => PresentationPlan.Compose(
            PresentationPlanExample.EveryBlock(),
            PresentationPlanExample.Citations(),
            PresentationPlanExample.Coverage(),
            [PresentationLimitation.RetrievalTruncated, PresentationLimitation.RetrievalTruncated]));
    }

    /// <summary>A plan with no block is a run that produced nothing, which is reported rather than presented.</summary>
    [Fact]
    public void Constructor_NoBlocks_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => PresentationPlan.Compose([], [], [], []));
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
        Assert.Throws<ArgumentException>(() => PresentationPlan.Compose(tooMany, [], [], []));
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
            PresentationPlanExample.Coverage(),
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
        var plan = PresentationPlan.Compose([block], [], [], []);

        // Assert
        Assert.Empty(plan.Limitations);
    }

    /// <summary>Which accounts an answer drew on and how current each was is part of the answer.</summary>
    [Fact]
    public void Compose_ARunOverTwoAccounts_ReportsWhatItReadOfEach()
    {
        // Arrange
        var coverage = new AccountCoverage[]
        {
            new(
                PresentationPlanExample.Text("work"),
                PresentationFreshness.CurrentAt(PresentationPlanExample.ObservedAt),
                PresentationPlanExample.ObservedAt.AddDays(-30),
                PresentationPlanExample.ObservedAt),
            new(
                PresentationPlanExample.Text("archive"),
                PresentationFreshness.StaleSince(PresentationPlanExample.ObservedAt.AddDays(-2)),
                earliestReceivedAt: null,
                latestReceivedAt: null),
        };

        // Act
        var plan = PresentationPlan.Compose(
            PresentationPlanExample.EveryBlock(),
            PresentationPlanExample.Citations(),
            coverage,
            []);

        // Assert
        Assert.Equal(["work", "archive"], plan.Coverage.Select(account => account.Account.Value));
    }

    [Fact]
    public void Constructor_TheSameAccountReportedTwice_IsRefused()
    {
        // Arrange
        var twice = PresentationPlanExample.Coverage().Concat(PresentationPlanExample.Coverage()).ToArray();

        // Act, Assert
        Assert.Throws<ArgumentException>(() => PresentationPlan.Compose(
            PresentationPlanExample.EveryBlock(),
            PresentationPlanExample.Citations(),
            twice,
            []));
    }

    [Fact]
    public void Constructor_MoreAccountsThanTheBound_IsRefused()
    {
        // Arrange
        var tooMany = Enumerable
            .Range(0, PresentationPlan.MaxAccountsCovered + 1)
            .Select(index => new AccountCoverage(
                PresentationPlanExample.Text($"account-{index}"),
                PresentationFreshness.Unknown,
                earliestReceivedAt: null,
                latestReceivedAt: null))
            .ToArray();

        // Act, Assert
        Assert.Throws<ArgumentException>(() => PresentationPlan.Compose(
            PresentationPlanExample.EveryBlock(),
            PresentationPlanExample.Citations(),
            tooMany,
            []));
    }

    /// <summary>A fact drawn from a source nobody could read is a fact drawn from nothing.</summary>
    [Fact]
    public void Constructor_ABlockRestingOnASourceNothingCouldRead_IsRefused()
    {
        // Arrange
        var block = new AnswerBlock(
            PresentationPlanExample.Supported(),
            PresentationPlanExample.Text("The contract renews in March."),
            PresentationConfidence.High);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => PresentationPlan.Compose(
            [block],
            [Unreadable(PresentationPlanExample.FirstCitation, UnreadableSourceReason.Encrypted)],
            [],
            []));
    }

    /// <summary>The state exists so a run can say a contract was there and yielded nothing, which is an unsupported answer beside a declared source.</summary>
    [Fact]
    public void Compose_AnUnansweredQuestionBesideASourceNothingCouldRead_KeepsTheReason()
    {
        // Arrange
        var block = new AnswerBlock(
            PresentationEvidence.Unsupported(PresentationFreshness.Unknown),
            PresentationPlanExample.Text("The mail this run read does not answer the question."),
            PresentationConfidence.Low);

        // Act
        var plan = PresentationPlan.Compose(
            [block],
            [Unreadable(PresentationPlanExample.FirstCitation, UnreadableSourceReason.Encrypted)],
            [],
            [PresentationLimitation.SourcesUnavailable]);

        // Assert
        Assert.Equal(UnreadableSourceReason.Encrypted, plan.Citations[0].Unreadable);
    }

    /// <summary>A fact resting on this deployment's reading of pictures alone is corroboration rather than evidence.</summary>
    [Fact]
    public void RestsOnDepictedSourcesOnly_ABlockRestingOnADescribedImageAlone_IsTrue()
    {
        // Arrange
        var block = new AnswerBlock(
            PresentationPlanExample.Supported(),
            PresentationPlanExample.Text("The photographed invoice reads £40,000."),
            PresentationConfidence.Moderate);
        var plan = PresentationPlan.Compose(
            [block],
            [Depicted(PresentationPlanExample.FirstCitation)],
            [],
            []);

        // Act, Assert
        Assert.True(plan.RestsOnDepictedSourcesOnly(block));
    }

    /// <summary>One written source among them is what a fact rests on, so the block is no longer resting on pictures alone.</summary>
    [Fact]
    public void RestsOnDepictedSourcesOnly_ABlockRestingOnAWrittenSourceToo_IsFalse()
    {
        // Arrange
        var block = new AnswerBlock(
            new PresentationEvidence(
                PresentationSupport.Supported,
                [PresentationPlanExample.FirstCitation, PresentationPlanExample.SecondCitation],
                PresentationFreshness.CurrentAt(PresentationPlanExample.ObservedAt)),
            PresentationPlanExample.Text("They accepted the revised figure."),
            PresentationConfidence.High);
        var plan = PresentationPlan.Compose(
            [block],
            [Depicted(PresentationPlanExample.FirstCitation), .. PresentationPlanExample.Citations().Skip(1).Take(1)],
            [],
            []);

        // Act, Assert
        Assert.False(plan.RestsOnDepictedSourcesOnly(block));
    }

    /// <summary>A block resting on nothing rests on no picture either, which is a distinction a client draws differently.</summary>
    [Fact]
    public void RestsOnDepictedSourcesOnly_ABlockRestingOnNothing_IsFalse()
    {
        // Arrange
        var block = new AnswerBlock(
            PresentationEvidence.Unsupported(PresentationFreshness.Unknown),
            PresentationPlanExample.Text("The mail this run read does not answer the question."),
            PresentationConfidence.Low);
        var plan = PresentationPlan.Compose([block], [], [], []);

        // Act, Assert
        Assert.False(plan.RestsOnDepictedSourcesOnly(block));
    }

    private static PresentationCitation Depicted(PresentationCitationId id) =>
        new(
            id,
            new AttachmentCitationTarget(
                StoredEmailId.Create(new Guid("11111111-1111-1111-1111-111111111111")),
                attachmentPosition: 0),
            PresentationPlanExample.Text("invoice.jpg"),
            PresentationSourceMedium.Depicted);

    private static PresentationCitation Unreadable(PresentationCitationId id, UnreadableSourceReason reason) =>
        new(
            id,
            new AttachmentCitationTarget(
                StoredEmailId.Create(new Guid("11111111-1111-1111-1111-111111111111")),
                attachmentPosition: 0),
            PresentationPlanExample.Text("contract.pdf"),
            PresentationSourceMedium.Written,
            reason);
}
