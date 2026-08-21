// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Xml.Linq;
using MailFathom.AI.Orchestration;
using MailFathom.AI.Retrieval;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.AI;
using Xunit;

namespace MailFathom.AI.UnitTests.Retrieval;

/// <summary>Covers the lookup a run may make: what the model can ask for, what it cannot, and what comes back.</summary>
/// <remarks>
/// The tool is invoked directly rather than through a composed agent, because what these tests are about is the
/// contract between one lookup and the retrieval port. The composition tests state the other half — that this is the
/// only tool a run holds and that its result reaches the model as a tool result.
/// </remarks>
public sealed class ScopedMailKnowledgeRetrievalTests
{
    private const string Query = "what did the insurer agree to pay";

    /// <summary>The literal the scanner in the guarded-egress tests reports, standing in for a credential in mail.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

    private static readonly MailboxScope OnePrimaryAccount = MailboxScope.Create(
        [MailAccountId.Create("primary")],
        [new MailFolderIdentity(MailAccountId.Create("primary"), MailFolderAlias.Create("INBOX"))]);

    /// <summary>The whole point of the narrowing: it reaches the port as the query rather than as words that have to rank.</summary>
    [Fact]
    public async Task SearchTool_ALookupNamingEveryFilter_ReachesTheRetrievalAsThatQuery()
    {
        // Arrange
        var knowledgeSearch = new RecordingEmailKnowledgeSearch();
        var tool = ToolOver(knowledgeSearch);

        // Act
        await InvokeAsync(tool, new Dictionary<string, object?>
        {
            [ScopedMailKnowledgeRetrieval.QueryArgumentName] = Query,
            ["senderAddress"] = "anna@example.test",
            ["recipientAddress"] = "bruno@example.test",
            ["subjectFragment"] = "claim",
            ["receivedOnOrAfter"] = "2026-07-01T00:00:00+00:00",
            ["receivedBefore"] = "2026-07-08T00:00:00+00:00",
            ["isRemotelySeen"] = true,
            ["isRemotelyFlagged"] = true,
            ["keyword"] = "$Label",
            ["hasAttachments"] = false,
        });

        // Assert
        var query = Assert.Single(knowledgeSearch.Calls).Query;

        Assert.Equal(
            new EmailKnowledgeQuery
            {
                QueryText = Query,
                SenderAddress = "anna@example.test",
                RecipientAddress = "bruno@example.test",
                SubjectFragment = "claim",
                ReceivedOnOrAfter = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                ReceivedBefore = new DateTimeOffset(2026, 7, 8, 0, 0, 0, TimeSpan.Zero),
                IsRemotelySeen = true,
                IsRemotelyFlagged = true,
                Keyword = "$Label",
                HasAttachments = false,
            },
            query);
    }

    /// <summary>A lookup that narrows by nothing is the shape every lookup had before the filters existed, and it still works.</summary>
    [Fact]
    public async Task SearchTool_ALookupNamingOnlyItsQuery_ReachesTheRetrievalWithNoNarrowing()
    {
        // Arrange
        var knowledgeSearch = new RecordingEmailKnowledgeSearch();
        var tool = ToolOver(knowledgeSearch);

        // Act
        await InvokeAsync(tool, OneQuery);

        // Assert
        Assert.Equal(EmailKnowledgeQuery.ForText(Query), Assert.Single(knowledgeSearch.Calls).Query);
    }

    /// <summary>
    /// The scope was bound into this object when the run was composed, so it is the same value on every lookup and
    /// nothing the model writes reaches it.
    /// </summary>
    [Fact]
    public async Task SearchTool_AnyLookup_IsAnsweredFromTheScopeTheRunWasComposedWith()
    {
        // Arrange
        var knowledgeSearch = new RecordingEmailKnowledgeSearch();
        var tool = ToolOver(knowledgeSearch);

        // Act
        await InvokeAsync(tool, OneQuery);
        await InvokeAsync(tool, new Dictionary<string, object?>
        {
            [ScopedMailKnowledgeRetrieval.QueryArgumentName] = "everything in the secondary account",
        });

        // Assert
        Assert.Equal(
            [OnePrimaryAccount, OnePrimaryAccount],
            knowledgeSearch.Calls.Select(call => call.Scope));
    }

    /// <summary>How the mail was ranked decides how a further query is worth wording, so every lookup says which ranking answered it.</summary>
    [Theory]
    [InlineData(EmailSearchRetrievalMode.Lexical, RetrievedMailContextFormatter.LexicalRetrievalMode)]
    [InlineData(EmailSearchRetrievalMode.Hybrid, RetrievedMailContextFormatter.HybridRetrievalMode)]
    public async Task SearchTool_ALookup_ReportsHowTheMailWasRanked(
        EmailSearchRetrievalMode retrievalMode,
        string published)
    {
        // Arrange
        var knowledgeSearch = new RecordingEmailKnowledgeSearch()
            .RankingBy(retrievalMode)
            .Returning(Query, KnowledgePassages.Create("we will pay 4200"));
        var tool = ToolOver(knowledgeSearch);

        // Act
        var envelope = await InvokeAsync(tool, OneQuery);

        // Assert
        Assert.Equal(
            published,
            RootOf(envelope).Attribute(RetrievedMailContextFormatter.RetrievalModeAttributeName)?.Value);
    }

    /// <summary>
    /// A refused lookup is a document naming the argument rather than an empty envelope, because the caller is a tool
    /// loop: a model told the mailbox holds nothing concludes the mail does not exist and stops asking, while a model
    /// told its argument was refused writes another one.
    /// </summary>
    [Fact]
    public async Task SearchTool_AFilterTheSearchRefuses_ComesBackAsARefusalNamingTheArgument()
    {
        // Arrange
        var knowledgeSearch = new RecordingEmailKnowledgeSearch().Refusing("sender address");
        var tool = ToolOver(knowledgeSearch);

        // Act
        var document = await InvokeAsync(tool, OneQuery);

        // Assert
        var root = RootOf(document);

        Assert.Equal(RetrievedMailContextFormatter.RefusalElementName, root.Name.LocalName);
        Assert.Equal(
            "sender address",
            root.Attribute(RetrievedMailContextFormatter.RefusedFilterAttributeName)?.Value);
    }

    /// <summary>A refusal is not a retrieval, so nothing about it reaches the record of what the run read.</summary>
    [Fact]
    public async Task SearchTool_AFilterTheSearchRefuses_LeavesTheRunsRecordUntouched()
    {
        // Arrange
        var knowledgeSearch = new RecordingEmailKnowledgeSearch().Refusing("subject fragment");
        var retrieval = new ScopedMailKnowledgeRetrieval(
            knowledgeSearch,
            OnePrimaryAccount,
            new MailAnsweringRunLedger(MailAnsweringRunBounds.Default),
            SensitiveContentEgressGuards.Inactive());

        // Act
        await InvokeAsync(retrieval.CreateSearchTool(), OneQuery);

        // Assert
        var report = retrieval.Report;

        Assert.Empty(report.Passages);
        Assert.Equal(0, report.CandidateCount);
    }

    /// <summary>A run may be handed no more mail than its ceiling allows, whatever the lookup narrowed to.</summary>
    [Fact]
    public async Task SearchTool_MoreMailThanTheRunMaySend_HandsOverWhatFitsAndSaysThereIsNoMore()
    {
        // Arrange
        var knowledgeSearch = new RecordingEmailKnowledgeSearch().Returning(
            Query,
            KnowledgePassages.Create(new string('a', 60)),
            KnowledgePassages.Create(new string('b', 60)));
        var retrieval = new ScopedMailKnowledgeRetrieval(
            knowledgeSearch,
            OnePrimaryAccount,
            new MailAnsweringRunLedger(MailAnsweringRunBounds.Create(
                maximumRetrievedCharacters: 60,
                maximumProviderCalls: 8,
                maximumTokens: 80_000)),
            SensitiveContentEgressGuards.Inactive());

        // Act
        var envelope = await InvokeAsync(retrieval.CreateSearchTool(), OneQuery);

        // Assert
        var root = RootOf(envelope);

        Assert.Single(root.Elements(RetrievedMailContextFormatter.MessageElementName));
        Assert.Equal(
            "true",
            root.Attribute(RetrievedMailContextFormatter.RetrievalLimitReachedAttributeName)?.Value);
    }

    /// <summary>The counts an operator and an audit read are summed across the run, because a model decides how many lookups to make.</summary>
    [Fact]
    public async Task SearchTool_SeveralLookups_AddsWhatEachOneFoundIntoOneRecord()
    {
        // Arrange
        var knowledgeSearch = new RecordingEmailKnowledgeSearch()
            .Returning(Query, KnowledgePassages.Create("we will pay 4200"))
            .Returning("claim 41", KnowledgePassages.Create("the claim was opened"));
        var retrieval = new ScopedMailKnowledgeRetrieval(
            knowledgeSearch,
            OnePrimaryAccount,
            new MailAnsweringRunLedger(MailAnsweringRunBounds.Default),
            SensitiveContentEgressGuards.Inactive());
        var tool = retrieval.CreateSearchTool();

        // Act
        await InvokeAsync(tool, OneQuery);
        await InvokeAsync(
            tool,
            new Dictionary<string, object?> { [ScopedMailKnowledgeRetrieval.QueryArgumentName] = "claim 41" });

        // Assert
        var report = retrieval.Report;

        Assert.Equal(2, report.Passages.Count);
        Assert.Equal(2, report.CandidateCount);
    }

    /// <summary>The extracts are where mail reaches a chat provider, so they are scanned before the envelope is written.</summary>
    [Fact]
    public async Task SearchTool_ASwitchedOnScanner_RedactsTheExtractAndTheSubjectBeforeTheyReachTheModel()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var knowledgeSearch = new RecordingEmailKnowledgeSearch().Returning(
            Query,
            KnowledgePassages.Create($"sign in with {Marker} today", subject: $"re: {Marker}"));
        var retrieval = new ScopedMailKnowledgeRetrieval(
            knowledgeSearch,
            OnePrimaryAccount,
            new MailAnsweringRunLedger(MailAnsweringRunBounds.Default),
            egress.Guard);

        // Act
        var envelope = await InvokeAsync(retrieval.CreateSearchTool(), OneQuery);

        // Assert
        var message = Assert.Single(
            RootOf(envelope).Elements(RetrievedMailContextFormatter.MessageElementName));

        Assert.Equal(
            "sign in with [redacted:CloudKey] today",
            message.Element(RetrievedMailContextFormatter.ExtractElementName)?.Value);
        Assert.Equal(
            "re: [redacted:CloudKey]",
            message.Element(RetrievedMailContextFormatter.SubjectElementName)?.Value);
    }

    /// <summary>Handing a model mail a scanner could not read would be the leak the switch was turned on to prevent.</summary>
    [Fact]
    public async Task SearchTool_ADetectorThatCannotAnswer_RefusesTheLookupRatherThanSendingItUnscanned()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Unavailable(TimeProvider.System);
        var knowledgeSearch = new RecordingEmailKnowledgeSearch().Returning(
            Query,
            KnowledgePassages.Create("an ordinary extract"));
        var retrieval = new ScopedMailKnowledgeRetrieval(
            knowledgeSearch,
            OnePrimaryAccount,
            new MailAnsweringRunLedger(MailAnsweringRunBounds.Default),
            egress.Guard);
        var tool = retrieval.CreateSearchTool();

        // Act, Assert
        await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(() => InvokeAsync(tool, OneQuery));
    }

    /// <summary>What a run reports having retrieved is read inside this process, so redacting it would guard nothing.</summary>
    [Fact]
    public async Task SearchTool_ASwitchedOnScanner_LeavesWhatTheRunRecordsAsItWasFound()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var knowledgeSearch = new RecordingEmailKnowledgeSearch().Returning(
            Query,
            KnowledgePassages.Create($"sign in with {Marker} today"));
        var retrieval = new ScopedMailKnowledgeRetrieval(
            knowledgeSearch,
            OnePrimaryAccount,
            new MailAnsweringRunLedger(MailAnsweringRunBounds.Default),
            egress.Guard);

        // Act
        await InvokeAsync(retrieval.CreateSearchTool(), OneQuery);

        // Assert
        Assert.Equal($"sign in with {Marker} today", Assert.Single(retrieval.Report.Passages).Text);
    }

    /// <summary>An opt-in nobody took must not appear on this path at all.</summary>
    [Fact]
    public async Task SearchTool_ADeploymentThatScansNothing_WritesTheExtractUntouched()
    {
        // Arrange
        var knowledgeSearch = new RecordingEmailKnowledgeSearch().Returning(
            Query,
            KnowledgePassages.Create($"sign in with {Marker} today"));

        // Act
        var envelope = await InvokeAsync(ToolOver(knowledgeSearch), OneQuery);

        // Assert
        Assert.Equal(
            $"sign in with {Marker} today",
            RootOf(envelope)
                .Elements(RetrievedMailContextFormatter.MessageElementName)
                .Single()
                .Element(RetrievedMailContextFormatter.ExtractElementName)?.Value);
    }

    private static Dictionary<string, object?> OneQuery =>
        new() { [ScopedMailKnowledgeRetrieval.QueryArgumentName] = Query };

    private static AIFunction ToolOver(RecordingEmailKnowledgeSearch knowledgeSearch) =>
        new ScopedMailKnowledgeRetrieval(
            knowledgeSearch,
            OnePrimaryAccount,
            new MailAnsweringRunLedger(MailAnsweringRunBounds.Default),
            SensitiveContentEgressGuards.Inactive()).CreateSearchTool();

    /// <summary>Calls the tool the way the framework's tool loop does, and reads the document it answered with.</summary>
    /// <remarks>
    /// The framework marshals a returned string through JSON, so the result arrives as a serialized value rather than as
    /// the string itself. Reading it back here keeps every assertion about the document this system wrote.
    /// </remarks>
    private static async Task<string> InvokeAsync(AIFunction tool, IDictionary<string, object?> arguments)
    {
        var result = await tool.InvokeAsync(
            new AIFunctionArguments(arguments),
            TestContext.Current.CancellationToken);

        return result switch
        {
            JsonElement element => element.GetString() ?? element.ToString(),
            string text => text,
            _ => result?.ToString() ?? string.Empty,
        };
    }

    /// <summary>Reads the answer back as the document it claims to be, which is itself part of what is asserted.</summary>
    private static XElement RootOf(string document) =>
        XDocument.Parse(document).Root
        ?? throw new InvalidOperationException("The tool answered with no root element.");
}
