// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Enrichment;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Enrichment;
using Xunit;

namespace MailFathom.AI.UnitTests.Enrichment;

/// <summary>Covers what a model's answer about one message is read as, and what is dropped when it cannot be believed.</summary>
/// <remarks>
/// Every case here is a pure function of the text and the passages the turn published, which is what lets a derivation
/// be asserted rather than observed: the answers a provider produces once in a thousand runs are ordinary examples in
/// this class.
/// </remarks>
public sealed class EmailEnrichmentReadingTests
{
    private const string AgentName = "mailfathom-email-enrichment";

    private static readonly EmailChunkId FirstPassage =
        EmailChunkId.Create(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    private static readonly EmailChunkId SecondPassage =
        EmailChunkId.Create(Guid.Parse("22222222-2222-2222-2222-222222222222"));

    private static readonly IReadOnlyList<EnrichablePassage> Passages =
    [
        new EnrichablePassage(FirstPassage, 0, "the racking quotation is attached"),
        new EnrichablePassage(SecondPassage, 1, "we need your answer by Friday"),
    ];

    [Fact]
    public void Read_AWellFormedAnswer_ReadsEveryAspectWithItsReasonAndEvidence()
    {
        // Arrange
        const string answer = """
            {
              "sense": { "text": "a racking quotation", "reason": "the first passage attaches one", "passages": [0] },
              "significance": { "text": "it is the last quote outstanding", "reason": "it names the answer", "passages": [0, 1] },
              "commitment": {
                "text": "answer the supplier",
                "reason": "the message asks for an answer",
                "passages": [1],
                "dueAt": "2026-09-11T00:00:00Z"
              }
            }
            """;

        // Act
        var marks = EmailEnrichmentReading.Read(answer, Passages, AgentName).Marks;

        // Assert
        Assert.Equal(
            [EmailEnrichmentAspect.Sense, EmailEnrichmentAspect.Significance, EmailEnrichmentAspect.Commitment],
            marks.Select(mark => mark.Aspect));
        Assert.Equal("a racking quotation", marks[0].Text);
        Assert.Equal("the first passage attaches one", marks[0].Reason);
        Assert.Equal([FirstPassage], marks[0].Evidence);
        Assert.Equal([FirstPassage, SecondPassage], marks[1].Evidence);
        Assert.Equal(new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero), marks[2].DueAt);
    }

    /// <summary>Every mark records the agent that wrote it, which is what tells a model's reading from a rule's.</summary>
    [Fact]
    public void Read_AWellFormedAnswer_RecordsTheAgentAsTheModelThatProducedEachMark()
    {
        // Arrange
        const string answer = """
            { "sense": { "text": "a quotation", "reason": "it attaches one", "passages": [0] } }
            """;

        // Act
        var marks = EmailEnrichmentReading.Read(answer, Passages, AgentName).Marks;

        // Assert
        Assert.Equal(EmailEnrichmentSource.Model, marks[0].Provenance.Source);
        Assert.Equal(AgentName, marks[0].Provenance.Origin);
    }

    /// <summary>A model told to answer with one object still fences it.</summary>
    [Fact]
    public void Read_AnAnswerInsideACodeFence_StillReadsTheMarks()
    {
        // Arrange
        var answer = string.Join(
            Environment.NewLine,
            "Here is what I found:",
            "```json",
            """{ "sense": { "text": "a quotation", "reason": "it attaches one", "passages": [0] } }""",
            "```");

        // Act
        var marks = EmailEnrichmentReading.Read(answer, Passages, AgentName).Marks;

        // Assert
        Assert.Equal("a quotation", Assert.Single(marks).Text);
    }

    /// <summary>A citation is a position in the list the turn published, so a number outside it names no passage.</summary>
    [Fact]
    public void Read_AMarkCitingAPassageTheTurnNeverPublished_DropsTheMark()
    {
        // Arrange
        const string answer = """
            { "sense": { "text": "a quotation", "reason": "it attaches one", "passages": [7, -1] } }
            """;

        // Act
        var marks = EmailEnrichmentReading.Read(answer, Passages, AgentName).Marks;

        // Assert
        Assert.Empty(marks);
    }

    /// <summary>A reading nothing backs is the one thing the record exists to rule out.</summary>
    [Fact]
    public void Read_AMarkCitingNothing_DropsTheMark()
    {
        // Arrange
        const string answer = """
            { "sense": { "text": "a quotation", "reason": "it attaches one", "passages": [] } }
            """;

        // Act
        var marks = EmailEnrichmentReading.Read(answer, Passages, AgentName).Marks;

        // Assert
        Assert.Empty(marks);
    }

    /// <summary>A reading with no reason cannot be checked, so it falls away and the others stay.</summary>
    [Fact]
    public void Read_AMarkWithoutAReason_DropsThatMarkAndKeepsTheRest()
    {
        // Arrange
        const string answer = """
            {
              "sense": { "text": "a quotation", "reason": "   ", "passages": [0] },
              "significance": { "text": "it is outstanding", "reason": "it asks for an answer", "passages": [1] }
            }
            """;

        // Act
        var marks = EmailEnrichmentReading.Read(answer, Passages, AgentName).Marks;

        // Assert
        Assert.Equal(EmailEnrichmentAspect.Significance, Assert.Single(marks).Aspect);
    }

    /// <summary>A date nothing can parse is dropped on its own, because the commitment is still worth showing.</summary>
    [Fact]
    public void Read_ACommitmentWhoseDateIsNotOne_KeepsTheCommitmentWithoutADate()
    {
        // Arrange
        const string answer = """
            {
              "commitment": {
                "text": "answer the supplier",
                "reason": "the message asks for an answer",
                "passages": [1],
                "dueAt": "soon"
              }
            }
            """;

        // Act
        var marks = EmailEnrichmentReading.Read(answer, Passages, AgentName).Marks;

        // Assert
        var mark = Assert.Single(marks);
        Assert.Equal("answer the supplier", mark.Text);
        Assert.Null(mark.DueAt);
    }

    /// <summary>A date on any other aspect is a value nothing reads, so the reading refuses it before it is stored.</summary>
    [Fact]
    public void Read_ASenseCarryingADate_KeepsTheReadingAndDropsTheDate()
    {
        // Arrange
        const string answer = """
            {
              "sense": {
                "text": "a quotation",
                "reason": "it attaches one",
                "passages": [0],
                "dueAt": "2026-09-11T00:00:00Z"
              }
            }
            """;

        // Act
        var marks = EmailEnrichmentReading.Read(answer, Passages, AgentName).Marks;

        // Assert
        Assert.Null(Assert.Single(marks).DueAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("I could not read that message.")]
    [InlineData("{ not json at all")]
    [InlineData("{}")]
    public void Read_AnAnswerNothingCanBeReadFrom_ProducesNoMarks(string? answer)
    {
        // Act
        var marks = EmailEnrichmentReading.Read(answer, Passages, AgentName).Marks;

        // Assert
        Assert.Empty(marks);
    }

    /// <summary>A producer citing the whole message is bounded, and what is kept is what it named first.</summary>
    [Fact]
    public void Read_AMarkCitingMorePassagesThanAMarkMayHold_KeepsTheLeadingCitations()
    {
        // Arrange
        IReadOnlyList<EnrichablePassage> manyPassages =
        [
            .. Enumerable.Range(0, 8).Select(ordinal => new EnrichablePassage(
                EmailChunkId.Create(Guid.CreateVersion7()),
                ordinal,
                $"passage {ordinal}")),
        ];

        const string answer = """
            { "sense": { "text": "a long thread", "reason": "it runs on", "passages": [0, 1, 2, 3, 4, 5] } }
            """;

        // Act
        var marks = EmailEnrichmentReading.Read(answer, manyPassages, AgentName).Marks;

        // Assert
        Assert.Equal(
            [.. manyPassages.Take(EmailEnrichmentMark.MaximumEvidenceCount).Select(passage => passage.Id)],
            Assert.Single(marks).Evidence);
    }

    /// <summary>What the message asks of the person is read beside the marks, out of the one answer both came in.</summary>
    [Fact]
    public void Read_AnAnswerListingWhatTheMessageAsksFor_ReadsEachOfThemAsAProposal()
    {
        // Arrange
        const string answer = """
            {
              "sense": { "text": "a racking quotation", "reason": "the first passage attaches one", "passages": [0] },
              "tasks": [
                { "title": "Answer the supplier", "dueOn": "2026-09-11" },
                { "title": "Countersign the quotation" }
              ]
            }
            """;

        // Act
        var derivation = EmailEnrichmentReading.Read(answer, Passages, AgentName);

        // Assert
        Assert.Equal(
            ["Answer the supplier", "Countersign the quotation"],
            derivation.Tasks.Select(task => task.Title));
        Assert.Equal(new DateOnly(2026, 9, 11), derivation.Tasks[0].DueOn);
        Assert.Null(derivation.Tasks[1].DueOn);
        Assert.Single(derivation.Marks);
    }

    /// <summary>Most mail asks for nothing, and an answer that says so proposes nothing.</summary>
    [Fact]
    public void Read_AnAnswerListingNothingToDo_ProposesNoTask()
    {
        // Arrange
        const string answer = """
            { "sense": { "text": "a newsletter", "reason": "it announces a release", "passages": [0] } }
            """;

        // Act
        var derivation = EmailEnrichmentReading.Read(answer, Passages, AgentName);

        // Assert
        Assert.Empty(derivation.Tasks);
    }

    /// <summary>A task is due on a day, so an instant a model wrote instead is read as the day it falls on.</summary>
    [Fact]
    public void Read_AProposalDatedByTheInstant_ReadsItAsTheDayItFallsOn()
    {
        // Arrange
        const string answer = """
            { "tasks": [{ "title": "Answer the supplier", "dueOn": "2026-09-11T16:30:00Z" }] }
            """;

        // Act
        var derivation = EmailEnrichmentReading.Read(answer, Passages, AgentName);

        // Assert
        Assert.Equal(new DateOnly(2026, 9, 11), Assert.Single(derivation.Tasks).DueOn);
    }

    /// <summary>A day nothing can read falls away on its own rather than taking with it something somebody was asked to do.</summary>
    [Fact]
    public void Read_AProposalDatedByNothingReadable_KeepsTheProposalWithoutADay()
    {
        // Arrange
        const string answer = """
            { "tasks": [{ "title": "Answer the supplier", "dueOn": "as soon as you can" }] }
            """;

        // Act
        var derivation = EmailEnrichmentReading.Read(answer, Passages, AgentName);

        // Assert
        Assert.Null(Assert.Single(derivation.Tasks).DueOn);
    }

    /// <summary>A proposal with no line states nothing a person could act on.</summary>
    [Theory]
    [InlineData("""{ "tasks": [{ "dueOn": "2026-09-11" }] }""")]
    [InlineData("""{ "tasks": [{ "title": "   " }] }""")]
    public void Read_AProposalStatingNothingToDo_DropsIt(string answer)
    {
        // Act
        var derivation = EmailEnrichmentReading.Read(answer, Passages, AgentName);

        // Assert
        Assert.Empty(derivation.Tasks);
    }

    /// <summary>A person handed a list of twenty things from one message has been given work rather than saved it.</summary>
    [Fact]
    public void Read_MoreProposalsThanOneMessageProduces_KeepsTheLeadingOnes()
    {
        // Arrange
        var written = string.Join(
            ",",
            Enumerable.Range(0, EmailTaskProposal.MaximumPerEmail + 3)
                .Select(ordinal => $$"""{ "title": "Task {{ordinal}}" }"""));

        // Act
        var derivation = EmailEnrichmentReading.Read($$"""{ "tasks": [{{written}}] }""", Passages, AgentName);

        // Assert
        Assert.Equal(EmailTaskProposal.MaximumPerEmail, derivation.Tasks.Count);
        Assert.Equal("Task 0", derivation.Tasks[0].Title);
    }
}
