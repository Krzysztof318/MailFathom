// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Retrieval;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using Xunit;

namespace MailFathom.Application.UnitTests.Retrieval;

/// <summary>Covers the one invariant a lookup's counts have to keep for the pair to mean anything.</summary>
public sealed class EmailKnowledgeLookupTests
{
    /// <summary>A lookup that judged nothing must not report a filter it does not have as having dropped mail.</summary>
    [Fact]
    public void Unfiltered_APassageList_ReportsAsManyCandidatesAsItHandsOver()
    {
        // Arrange
        IReadOnlyList<EmailKnowledgePassage> passages = [Passage(), Passage()];

        // Act
        var lookup = EmailKnowledgeLookup.Unfiltered(passages, EmailSearchRetrievalMode.Hybrid);

        // Assert
        Assert.Equal((passages, 2, false), (lookup.Passages, lookup.CandidateCount, lookup.RelevanceFilterFellBack));
    }

    /// <summary>How the mail was ranked describes the instance, so an unjudged lookup reports it unchanged.</summary>
    [Theory]
    [InlineData(EmailSearchRetrievalMode.Lexical)]
    [InlineData(EmailSearchRetrievalMode.Hybrid)]
    public void Unfiltered_ARetrievalMode_ReportsTheModeItWasRankedBy(EmailSearchRetrievalMode retrievalMode)
    {
        // Act
        var lookup = EmailKnowledgeLookup.Unfiltered([], retrievalMode);

        // Assert
        Assert.Equal(retrievalMode, lookup.RetrievalMode);
    }

    [Fact]
    public void Unfiltered_WithoutPassages_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => EmailKnowledgeLookup.Unfiltered(null!, EmailSearchRetrievalMode.Lexical));
    }

    private static EmailKnowledgePassage Passage() => new()
    {
        StoredEmailId = StoredEmailId.Create(Guid.CreateVersion7()),
        AccountId = MailAccountId.Create("work"),
        FolderAlias = MailFolderAlias.Create("inbox"),
        SenderVerification = SenderVerification.NotEstablished,
        Text = "the invoice is attached",
    };
}
