// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Discovery;
using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Blocks;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Retrieval;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using Xunit;

namespace MailFathom.AI.UnitTests.Discovery;

/// <summary>Covers what a composing agent's answer is read as, and what a run says when it cannot be believed.</summary>
/// <remarks>
/// Every case here is a pure function of the answer text, the sources the run declared, and what it read of its own
/// accounts, which is what lets the honesty rules be asserted rather than observed: an invented citation, a
/// contradiction, and a mailbox that is behind are ordinary examples in this class rather than a provider's rare day.
/// </remarks>
public sealed class DiscoveryCompositionReadingTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId Work = MailAccountId.Create("work");

    private static readonly MailAccountId Archive = MailAccountId.Create("archive");

    private static readonly DateTimeOffset BehindSince = ObservedAt.AddDays(-3);

    [Fact]
    public void Read_AnAnswerRestingOnAnOfferedSource_ComposesItAsWhatTheCorrespondenceSays()
    {
        // Arrange
        const string answer = """
            { "answer": "They accepted the revised figure.", "confidence": "high", "sources": ["s1"] }
            """;

        // Act
        var plan = Read(answer, DiscoveryIntent.FindFact, Sources("we accept the revised figure"));

        // Assert
        var block = Assert.IsType<AnswerBlock>(plan.Blocks[0]);
        Assert.Equal("They accepted the revised figure.", block.Text.Value);
        Assert.Equal(PresentationSupport.Supported, block.Evidence.Support);
        Assert.Equal(PresentationConfidence.High, block.Confidence);
    }

    /// <summary>Nothing a model writes becomes a reference to mail, so a name nobody offered rests on nothing.</summary>
    [Fact]
    public void Read_AnAnswerCitingASourceNobodyOffered_RestsOnNothingAndSaysSo()
    {
        // Arrange
        const string answer = """
            { "answer": "They accepted the revised figure.", "confidence": "high", "sources": ["s9"] }
            """;

        // Act
        var plan = Read(answer, DiscoveryIntent.FindFact, Sources("we accept the revised figure"));

        // Assert
        var block = Assert.IsType<AnswerBlock>(plan.Blocks[0]);
        Assert.Equal(PresentationSupport.Unsupported, block.Evidence.Support);
        Assert.Equal("The mail this run read does not answer the question.", block.Text.Value);
        Assert.Equal(PresentationConfidence.Low, block.Confidence);
    }

    /// <summary>An empty source list is the instruction's own way of saying the mail does not settle the question.</summary>
    [Fact]
    public void Read_AnAnswerRestingOnNoSource_SaysTheMailDoesNotAnswerTheQuestion()
    {
        // Arrange
        const string answer = """
            { "answer": "It is probably the usual supplier.", "confidence": "high", "sources": [] }
            """;

        // Act
        var plan = Read(answer, DiscoveryIntent.FindFact, Sources("a quotation"));

        // Assert
        var block = Assert.IsType<AnswerBlock>(plan.Blocks[0]);
        Assert.Equal("The mail this run read does not answer the question.", block.Text.Value);
        Assert.Empty(block.Evidence.Citations);
    }

    /// <summary>A model that wrote prose, or nothing at all, still produces a result over the mail the run read.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("I am afraid I cannot help with that.")]
    public void Read_AnAnswerNothingCanBeReadOutOf_StillComposesAResultOverTheSameSources(string? answer)
    {
        // Act
        var plan = Read(answer, DiscoveryIntent.FindFact, Sources("a quotation"));

        // Assert
        Assert.Equal(PresentationSupport.Unsupported, plan.Blocks[0].Evidence.Support);
        Assert.Single(plan.Citations);
        Assert.IsType<EvidenceListBlock>(plan.Blocks[1]);
    }

    /// <summary>A model told to answer with one object still fences it.</summary>
    [Fact]
    public void Read_AnAnswerInsideACodeFence_StillReadsTheResult()
    {
        // Arrange
        var answer = string.Join(
            Environment.NewLine,
            "```json",
            """{ "answer": "They accepted.", "confidence": "moderate", "sources": ["s1"] }""",
            "```");

        // Act
        var plan = Read(answer, DiscoveryIntent.FindFact, Sources("we accept"));

        // Assert
        Assert.Equal("They accepted.", Assert.IsType<AnswerBlock>(plan.Blocks[0]).Text.Value);
    }

    /// <summary>A supplier who quoted twice has two figures, and an answer naming one has answered a question nobody asked.</summary>
    [Fact]
    public void Read_SourcesThatContradictEachOther_CarriesBothSidesRatherThanChoosing()
    {
        // Arrange
        const string answer = """
            {
              "answer": "The correspondence quotes two figures.",
              "confidence": "high",
              "sources": ["s1", "s2"],
              "conflict": [
                { "statement": "£40,000", "sources": ["s1"] },
                { "statement": "£44,000", "sources": ["s2"] }
              ]
            }
            """;

        // Act
        var plan = Read(answer, DiscoveryIntent.FindFact, Sources("we quote £40,000", "we quote £44,000"));

        // Assert
        var block = Assert.IsType<AnswerBlock>(plan.Blocks[0]);
        Assert.Equal(PresentationSupport.Conflicting, block.Evidence.Support);
        Assert.Equal(
            ["£40,000", "£44,000"],
            block.Evidence.ConflictingClaims.Select(side => side.Statement.Value));
    }

    /// <summary>A side may only name what the answer rests on, or the block would carry a reference its own evidence does not.</summary>
    [Fact]
    public void Read_ASideNamingASourceTheAnswerDidNotCite_DropsThatSide()
    {
        // Arrange
        const string answer = """
            {
              "answer": "The correspondence quotes two figures.",
              "confidence": "high",
              "sources": ["s1", "s2"],
              "conflict": [
                { "statement": "£40,000", "sources": ["s1"] },
                { "statement": "£44,000", "sources": ["s3"] }
              ]
            }
            """;

        // Act
        var plan = Read(
            answer,
            DiscoveryIntent.FindFact,
            Sources("we quote £40,000", "we quote £44,000", "an unrelated note"));

        // Assert
        var block = Assert.IsType<AnswerBlock>(plan.Blocks[0]);
        Assert.Equal(PresentationSupport.Supported, block.Evidence.Support);
        Assert.Empty(block.Evidence.ConflictingClaims);
    }

    /// <summary>An event naming a source the answer did not cite is a reference the block cannot carry either.</summary>
    [Fact]
    public void Read_AnEventNamingASourceTheAnswerDidNotCite_KeepsTheEventWithoutThatReference()
    {
        // Arrange
        const string answer = """
            {
              "answer": "The figure was revised.",
              "confidence": "moderate",
              "sources": ["s1"],
              "events": [
                { "occurredAt": "2026-03-02T09:30:00+00:00", "summary": "Figure revised", "subject": "Renewal", "sources": ["s2"] }
              ]
            }
            """;

        // Act
        var plan = Read(answer, DiscoveryIntent.TrackChange, Sources("the figure was revised", "an unrelated note"));

        // Assert
        var block = Assert.IsType<TimelineBlock>(plan.Blocks[0]);
        Assert.Empty(block.Entries[0].Sources);
    }

    /// <summary>A model reporting a settled answer over a contradiction is reporting its own fluency.</summary>
    [Fact]
    public void Read_AHighConfidenceReportedOverAContradiction_IsCappedAtModerate()
    {
        // Arrange
        const string answer = """
            {
              "answer": "The correspondence quotes two figures.",
              "confidence": "high",
              "sources": ["s1", "s2"],
              "conflict": [
                { "statement": "£40,000", "sources": ["s1"] },
                { "statement": "£44,000", "sources": ["s2"] }
              ]
            }
            """;

        // Act
        var plan = Read(answer, DiscoveryIntent.FindFact, Sources("we quote £40,000", "we quote £44,000"));

        // Assert
        Assert.Equal(PresentationConfidence.Moderate, Assert.IsType<AnswerBlock>(plan.Blocks[0]).Confidence);
    }

    /// <summary>A band nothing can be read out of arrives no more confident than a well-formed one.</summary>
    [Fact]
    public void Read_AConfidenceBandThatNamesNothing_IsReadAsModerate()
    {
        // Arrange
        const string answer = """
            { "answer": "They accepted.", "confidence": "very sure indeed", "sources": ["s1"] }
            """;

        // Act
        var plan = Read(answer, DiscoveryIntent.FindFact, Sources("we accept"));

        // Assert
        Assert.Equal(PresentationConfidence.Moderate, Assert.IsType<AnswerBlock>(plan.Blocks[0]).Confidence);
    }

    /// <summary>An answer read from a copy known to be behind may be missing what arrived since, and says so.</summary>
    [Fact]
    public void Read_ASourceFromAnAccountKnownToBeBehind_IsStaleRatherThanSupported()
    {
        // Arrange
        const string answer = """
            { "answer": "They accepted.", "confidence": "high", "sources": ["s1"] }
            """;

        // Act
        var plan = Read(
            answer,
            DiscoveryIntent.FindFact,
            Sources("we accept"),
            coverage: [Coverage(Work, PresentationFreshness.StaleSince(ObservedAt))]);

        // Assert
        var block = Assert.IsType<AnswerBlock>(plan.Blocks[0]);
        Assert.Equal(PresentationSupport.Stale, block.Evidence.Support);
        Assert.Equal(PresentationConfidence.Moderate, block.Confidence);
        Assert.Contains(PresentationLimitation.LocalCopyBehind, plan.Limitations);
    }

    [Fact]
    public void Read_AQuestionAboutHowSomethingChanged_ComposesTheCourseOfEvents()
    {
        // Arrange
        const string answer = """
            {
              "answer": "The figure was revised twice.",
              "confidence": "moderate",
              "sources": ["s1"],
              "events": [
                { "occurredAt": "2026-03-02T09:30:00+00:00", "summary": "Figure revised", "subject": "Renewal", "sources": ["s1"] }
              ]
            }
            """;

        // Act
        var plan = Read(answer, DiscoveryIntent.TrackChange, Sources("the figure was revised"));

        // Assert
        var block = Assert.IsType<TimelineBlock>(plan.Blocks[0]);
        Assert.Equal("Figure revised", block.Entries[0].Summary.Value);
    }

    /// <summary>Nothing here composes a course of events out of no events, so the honest answer takes its place.</summary>
    [Fact]
    public void Read_AQuestionAboutHowSomethingChangedAndNoDatedEvent_FallsBackToTheAnswerItself()
    {
        // Arrange
        const string answer = """
            { "answer": "The figure was revised twice.", "confidence": "moderate", "sources": ["s1"], "events": [] }
            """;

        // Act
        var plan = Read(answer, DiscoveryIntent.TrackChange, Sources("the figure was revised"));

        // Assert
        Assert.IsType<AnswerBlock>(plan.Blocks[0]);
    }

    [Fact]
    public void Read_AQuestionComparingOffers_ComposesTheComparison()
    {
        // Arrange
        const string answer = """
            {
              "answer": "Northwind quoted least.",
              "confidence": "high",
              "sources": ["s1", "s2"],
              "columns": ["party", "amount"],
              "rows": [
                { "cells": [{ "value": "Northwind", "sources": ["s1"] }, { "value": "£40,000", "sources": ["s1"] }] },
                { "cells": [{ "value": "Contoso", "sources": ["s2"] }] }
              ]
            }
            """;

        // Act
        var plan = Read(answer, DiscoveryIntent.CompareTerms, Sources("we quote £40,000", "we quote £44,000"));

        // Assert
        var block = Assert.IsType<FactTableBlock>(plan.Blocks[0]);
        Assert.Equal([FactTableColumn.Party, FactTableColumn.Amount], block.Columns);
        Assert.Single(block.Rows);
    }

    /// <summary>A column nobody can label is one the catalogue does not hold, and a table of none is no table.</summary>
    [Fact]
    public void Read_AComparisonOverAColumnTheCatalogueDoesNotHold_FallsBackToTheAnswerItself()
    {
        // Arrange
        const string answer = """
            {
              "answer": "Northwind quoted least.",
              "confidence": "high",
              "sources": ["s1"],
              "columns": ["vibe"],
              "rows": [{ "cells": [{ "value": "Northwind", "sources": ["s1"] }] }]
            }
            """;

        // Act
        var plan = Read(answer, DiscoveryIntent.CompareTerms, Sources("we quote £40,000"));

        // Assert
        Assert.IsType<AnswerBlock>(plan.Blocks[0]);
    }

    /// <summary>A passage carries the message it was cut from and not the attachment within it, so no gallery is composed today.</summary>
    [Fact]
    public void Read_AQuestionLookingForFiles_FallsBackToTheAnswerItself()
    {
        // Arrange
        const string answer = """
            { "answer": "The renewal was attached in March.", "confidence": "moderate", "sources": ["s1"] }
            """;

        // Act
        var plan = Read(answer, DiscoveryIntent.FindDocuments, Sources("the renewal is attached"));

        // Assert
        Assert.IsType<AnswerBlock>(plan.Blocks[0]);
    }

    /// <summary>The evidence list is what the run read rather than what the model cited, so a thin answer is checkable.</summary>
    [Fact]
    public void Read_AnAnswerCitingOneOfTwoSources_ListsBothAsWhatTheRunRead()
    {
        // Arrange
        const string answer = """
            { "answer": "They accepted.", "confidence": "high", "sources": ["s1"] }
            """;

        // Act
        var plan = Read(answer, DiscoveryIntent.FindFact, Sources("we accept", "we will revert"));

        // Assert
        var block = Assert.IsType<EvidenceListBlock>(plan.Blocks[1]);
        Assert.Equal(["s1", "s2"], block.Entries.Select(entry => entry.Source.Value));
    }

    /// <summary>A run that retrieved nothing still owes an answer, and it is one block saying so.</summary>
    [Fact]
    public void Read_ARunThatRetrievedNothing_ComposesTheAnswerAloneOverNoCitation()
    {
        // Act
        var plan = Read("""{ "answer": "Nothing.", "sources": [] }""", DiscoveryIntent.FindFact, []);

        // Assert
        Assert.Single(plan.Blocks);
        Assert.Empty(plan.Citations);
    }

    /// <summary>A lookup the deployment refused is mail the answer would have rested on and did not.</summary>
    [Fact]
    public void Read_ARunWhoseLookupsWereRefused_SaysSourcesWereUnavailable()
    {
        // Act
        var plan = Read(
            """{ "answer": "They accepted.", "sources": ["s1"] }""",
            DiscoveryIntent.FindFact,
            Sources("we accept"),
            evidence: Evidence(Sources("we accept"), EmailSearchRetrievalMode.Hybrid, lookupsRefused: 2));

        // Assert
        Assert.Contains(PresentationLimitation.SourcesUnavailable, plan.Limitations);
    }

    /// <summary>A ranking that fell back to words alone is a question answered without any reading of meaning.</summary>
    [Fact]
    public void Read_ARunRankedByWordsAlone_SaysSemanticRankingWasUnavailable()
    {
        // Act
        var plan = Read(
            """{ "answer": "They accepted.", "sources": ["s1"] }""",
            DiscoveryIntent.FindFact,
            Sources("we accept"),
            evidence: Evidence(Sources("we accept"), EmailSearchRetrievalMode.Lexical, lookupsRefused: 0));

        // Assert
        Assert.Contains(PresentationLimitation.SemanticRankingUnavailable, plan.Limitations);
    }

    /// <summary>A shape nothing backs is not a shape: an unsupported result opens with the sentence about the run rather than with an empty table.</summary>
    [Fact]
    public void Read_AQuestionComparingOffersThatNothingBacks_FallsBackToTheAnswerItself()
    {
        // Arrange
        const string answer = """
            {
              "answer": "They quoted different figures.",
              "sources": [],
              "columns": ["supplier", "price"],
              "rows": [{ "cells": [{ "value": "Northwind", "sources": [] }, { "value": "1200", "sources": [] }] }]
            }
            """;

        // Act
        var plan = Read(answer, DiscoveryIntent.CompareTerms, Sources("a quotation"));

        // Assert
        var block = Assert.IsType<AnswerBlock>(plan.Blocks[0]);
        Assert.Equal(PresentationSupport.Unsupported, block.Evidence.Support);
    }

    /// <summary>The evidence list names what a claim could have rested on, so it stops where a block's own citations stop.</summary>
    [Fact]
    public void Read_MoreSourcesThanOneBlockMayCite_ListsAsManyAsABlockMayRestOn()
    {
        // Arrange
        var sources = Enumerable
            .Range(1, PresentationEvidence.MaxCitations + 3)
            .Select(index => new DiscoveryComposedSource(
                new PresentationCitation(
                    PresentationCitationId.Create($"s{index}"),
                    new EmailCitationTarget(StoredEmailId.Create(Guid.CreateVersion7())),
                    PresentationText.Create($"extract {index}"),
                    PresentationSourceMedium.Written),
                Work,
                $"extract {index}",
                Relevance: 0.5))
            .ToArray();

        // Act
        var plan = Read("""{ "answer": "They accepted.", "sources": [] }""", DiscoveryIntent.FindFact, sources);

        // Assert
        var listed = Assert.IsType<EvidenceListBlock>(plan.Blocks[^1]);
        Assert.Equal(PresentationEvidence.MaxCitations, listed.Entries.Count);
    }

    /// <summary>A block resting on a current account and one that is behind is only as current as the worse of them.</summary>
    [Fact]
    public void Read_SourcesFromACurrentAccountAndOneBehind_IsStaleOverTheOlderInstant()
    {
        // Arrange
        const string answer = """
            { "answer": "They accepted.", "confidence": "high", "sources": ["s1", "s2"] }
            """;
        var sources = DiscoveryComposedSources.Declare(
            [Passage("we accept", Work), Passage("they confirmed", Archive)]);

        // Act
        var plan = Read(
            answer,
            DiscoveryIntent.FindFact,
            sources,
            coverage:
            [
                Coverage(Work, PresentationFreshness.CurrentAt(ObservedAt)),
                Coverage(Archive, PresentationFreshness.StaleSince(BehindSince)),
            ]);

        // Assert
        var block = Assert.IsType<AnswerBlock>(plan.Blocks[0]);
        Assert.Equal(PresentationSupport.Stale, block.Evidence.Support);
        Assert.Equal(BehindSince, block.Evidence.Freshness.ObservedAt);
    }

    /// <summary>A run that found more messages than it may declare as sources reached less than the question asked about.</summary>
    [Fact]
    public void Read_MoreMessagesThanTheRunMayDeclare_SaysRetrievalWasTruncated()
    {
        // Arrange
        var passages = Enumerable
            .Range(0, PresentationEvidence.MaxCitations + 6)
            .Select(index => Passage($"extract {index}"))
            .ToArray();
        var sources = DiscoveryComposedSources.Declare(passages);

        // Act
        var plan = Read(
            """{ "answer": "They accepted.", "sources": ["s1"] }""",
            DiscoveryIntent.FindFact,
            sources,
            evidence: new DiscoveryEvidence(
                passages,
                EmailSearchRetrievalMode.Hybrid,
                LookupsRun: 6,
                LookupsRefused: 0,
                RetrievalTruncated: false));

        // Assert
        Assert.Contains(PresentationLimitation.RetrievalTruncated, plan.Limitations);
    }

    /// <summary>A run its own character ceiling cut says so too, on the same limitation and with every message it found declared.</summary>
    /// <remarks>
    /// The other half of that limitation, and the one no other case here reaches: every source the run found is
    /// declared, so the count comparison above says nothing, and what a reader has to be told comes from retrieval
    /// having stopped rather than from a source list that was trimmed.
    /// </remarks>
    [Fact]
    public void Read_ARunItsOwnCeilingCut_SaysRetrievalWasTruncated()
    {
        // Arrange
        var sources = Sources("we accept");
        var passages = new[] { Passage("we accept") };

        // Act
        var plan = Read(
            """{ "answer": "They accepted.", "sources": ["s1"] }""",
            DiscoveryIntent.FindFact,
            sources,
            evidence: new DiscoveryEvidence(
                passages,
                EmailSearchRetrievalMode.Hybrid,
                LookupsRun: 1,
                LookupsRefused: 0,
                RetrievalTruncated: true));

        // Assert
        Assert.Equal(sources.Count, passages.Select(passage => passage.StoredEmailId).Distinct().Count());
        Assert.Contains(PresentationLimitation.RetrievalTruncated, plan.Limitations);
    }

    /// <summary>An answer past what a block may carry is cut, because dropping it would cite mail while saying no mail answered.</summary>
    [Fact]
    public void Read_AnAnswerLongerThanABlockMayCarry_CutsItRatherThanDroppingIt()
    {
        // Arrange
        var overlong = new string('a', PresentationText.MaxLength + 100);
        var answer = $$"""{ "answer": "{{overlong}}", "confidence": "high", "sources": ["s1"] }""";

        // Act
        var plan = Read(answer, DiscoveryIntent.FindFact, Sources("we accept"));

        // Assert
        var block = Assert.IsType<AnswerBlock>(plan.Blocks[0]);
        Assert.Equal(PresentationText.MaxLength, block.Text.Value.Length);
        Assert.Equal(PresentationSupport.Supported, block.Evidence.Support);
    }

    private static PresentationPlan Read(
        string? answerText,
        DiscoveryIntent intent,
        IReadOnlyList<DiscoveryComposedSource> sources,
        IReadOnlyList<AccountCoverage>? coverage = null,
        DiscoveryEvidence? evidence = null) =>
        DiscoveryCompositionReading.Read(
            answerText,
            DiscoveryRunPlan.Compose(
                intent,
                RetrievalPlan.Create(
                    EmailKnowledgeBounds.Default,
                    [EmailKnowledgeQuery.ForText("quotation")],
                    sufficientPassages: 5)),
            sources,
            evidence ?? Evidence(sources, EmailSearchRetrievalMode.Hybrid, lookupsRefused: 0),
            coverage ?? [Coverage(Work, PresentationFreshness.CurrentAt(ObservedAt))]);

    private static IReadOnlyList<DiscoveryComposedSource> Sources(params string[] extracts) =>
        DiscoveryComposedSources.Declare([.. extracts.Select(Passage)]);

    private static DiscoveryEvidence Evidence(
        IReadOnlyList<DiscoveryComposedSource> sources,
        EmailSearchRetrievalMode retrievalMode,
        int lookupsRefused) =>
        new(
            [.. sources.Select(source => Passage(source.Extract))],
            retrievalMode,
            LookupsRun: 1,
            lookupsRefused,
            RetrievalTruncated: false);

    private static AccountCoverage Coverage(MailAccountId accountId, PresentationFreshness freshness) =>
        new(
            PresentationText.Create(accountId.Value),
            freshness,
            earliestReceivedAt: null,
            latestReceivedAt: null);

    private static EmailKnowledgePassage Passage(string text) => Passage(text, Work);

    private static EmailKnowledgePassage Passage(string text, MailAccountId accountId) => new()
    {
        StoredEmailId = StoredEmailId.Create(Guid.CreateVersion7()),
        AccountId = accountId,
        FolderAlias = MailFolderAlias.Create("INBOX"),
        Subject = null,
        ReceivedAt = null,
        SenderVerification = SenderVerification.NotEstablished,
        MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
        Text = text,
    };
}
