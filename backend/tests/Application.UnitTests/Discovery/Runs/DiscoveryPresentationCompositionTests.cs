// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Blocks;
using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Retrieval;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Runs;

/// <summary>Covers what a run can present out of the correspondence alone, and what it refuses to invent.</summary>
public sealed class DiscoveryPresentationCompositionTests
{
    private static readonly Guid FirstMessage = new("aaaaaaaa-0000-0000-0000-000000000001");

    private static readonly Guid SecondMessage = new("aaaaaaaa-0000-0000-0000-000000000002");

    /// <summary>Two facts drawn from one message rest on one source, which is what lets a reader see that they do.</summary>
    [Fact]
    public void CitationsFor_SeveralPassagesOfOneMessage_DeclaresThatSourceOnce()
    {
        // Arrange
        var evidence = EvidenceOf(
            Passage("the first half", FirstMessage),
            Passage("the second half", FirstMessage),
            Passage("another message", SecondMessage));

        // Act
        var citations = DiscoveryPresentationComposition.CitationsFor(evidence);

        // Assert
        Assert.Equal(["s1", "s2"], citations.Select(citation => citation.Id.Value));
        Assert.Equal(
            [FirstMessage, SecondMessage],
            citations.Select(citation => citation.Target.Email.Value));
    }

    /// <summary>A source reads as something before it is followed, and the subject is what a reader recognizes a message by.</summary>
    [Fact]
    public void CitationsFor_AMessageCarryingASubject_LabelsTheSourceWithIt()
    {
        // Act
        var citations = DiscoveryPresentationComposition.CitationsFor(
            EvidenceOf(Passage("the quotation", FirstMessage, subject: "Racking quotation")));

        // Assert
        Assert.Equal("Racking quotation", Assert.Single(citations).Label.Value);
    }

    /// <summary>A subject a plan may not carry cannot decide whether a source is reachable, so where it was read from stands in.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("<b>")]
    public void CitationsFor_AMessageWithNoUsableSubject_LabelsTheSourceByWhereItWasRead(string? subject)
    {
        // Act
        var citations = DiscoveryPresentationComposition.CitationsFor(
            EvidenceOf(Passage("the quotation", FirstMessage, subject)));

        // Assert
        Assert.Equal("primary / INBOX", Assert.Single(citations).Label.Value);
    }

    /// <summary>The evidence list quotes the extracts the run answered from, in the order retrieval handed them over.</summary>
    [Fact]
    public void BlocksFor_RetrievedPassages_ComposesOneEvidenceListInRetrievalOrder()
    {
        // Arrange
        var evidence = EvidenceOf(
            Passage("the quotation", FirstMessage),
            Passage("the revision", SecondMessage));
        var citations = DiscoveryPresentationComposition.CitationsFor(evidence);

        // Act
        var blocks = DiscoveryPresentationComposition.BlocksFor(evidence, citations);

        // Assert
        var list = Assert.IsType<EvidenceListBlock>(Assert.Single(blocks));
        Assert.Equal(PresentationBlockType.EvidenceList, list.Type);
        Assert.Equal(["the quotation", "the revision"], list.Entries.Select(entry => entry.Fragment.Value));
        Assert.Equal(["s1", "s2"], list.Entries.Select(entry => entry.Source.Value));
    }

    /// <summary>Retrieval attaches no score, so the place a passage was given is what the relevance beside it reports.</summary>
    [Fact]
    public void BlocksFor_RetrievedPassages_ReportsRelevanceAsThePlaceRetrievalGaveEach()
    {
        // Arrange
        var evidence = EvidenceOf(
            Passage("the quotation", FirstMessage),
            Passage("the revision", SecondMessage));

        // Act
        var blocks = DiscoveryPresentationComposition.BlocksFor(
            evidence,
            DiscoveryPresentationComposition.CitationsFor(evidence));

        // Assert
        var list = Assert.IsType<EvidenceListBlock>(Assert.Single(blocks));
        Assert.Equal([1d, 0.5d], list.Entries.Select(entry => entry.Relevance));
    }

    /// <summary>Every source a block names is one the run declared, which is the one way a citation contract fails quietly.</summary>
    [Fact]
    public void BlocksFor_APassageOfAMessageNothingDeclared_LeavesItOutRatherThanCitingIt()
    {
        // Arrange
        var evidence = EvidenceOf(
            Passage("the quotation", FirstMessage),
            Passage("the revision", SecondMessage));

        // Act
        var blocks = DiscoveryPresentationComposition.BlocksFor(
            evidence,
            [.. DiscoveryPresentationComposition.CitationsFor(evidence).Take(1)]);

        // Assert
        var list = Assert.IsType<EvidenceListBlock>(Assert.Single(blocks));
        Assert.Equal(["the quotation"], list.Entries.Select(entry => entry.Fragment.Value));
    }

    /// <summary>A mailbox holding nothing on the subject composes no block rather than a block asserting that it does.</summary>
    [Fact]
    public void BlocksFor_NothingRetrieved_ComposesNoBlock()
    {
        // Arrange
        var evidence = EvidenceOf();

        // Act
        var blocks = DiscoveryPresentationComposition.BlocksFor(
            evidence,
            DiscoveryPresentationComposition.CitationsFor(evidence));

        // Assert
        Assert.Empty(blocks);
    }

    /// <summary>The block rests on the sources its entries do, so a reader checks one block's citations rather than the run's.</summary>
    [Fact]
    public void BlocksFor_RetrievedPassages_RestsTheBlockOnTheSourcesItsEntriesName()
    {
        // Arrange
        var evidence = EvidenceOf(
            Passage("the first half", FirstMessage),
            Passage("the second half", FirstMessage));

        // Act
        var blocks = DiscoveryPresentationComposition.BlocksFor(
            evidence,
            DiscoveryPresentationComposition.CitationsFor(evidence));

        // Assert
        var list = Assert.IsType<EvidenceListBlock>(Assert.Single(blocks));
        Assert.Equal(PresentationSupport.Supported, list.Evidence.Support);
        Assert.Equal(["s1"], list.Evidence.Citations.Select(citation => citation.Value));
    }

    private static DiscoveryEvidence EvidenceOf(params EmailKnowledgePassage[] passages) =>
        new(passages, EmailSearchRetrievalMode.Hybrid, LookupsRun: 1, LookupsRefused: 0);

    private static EmailKnowledgePassage Passage(string text, Guid storedEmailId, string? subject = null) => new()
    {
        StoredEmailId = StoredEmailId.Create(storedEmailId),
        AccountId = MailAccountId.Create("primary"),
        FolderAlias = MailFolderAlias.Create("INBOX"),
        Subject = subject,
        ReceivedAt = null,
        SenderVerification = SenderVerification.NotEstablished,
        MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
        Text = text,
    };
}
