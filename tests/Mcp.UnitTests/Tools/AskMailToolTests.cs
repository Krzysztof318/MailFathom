// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Mcp.Tools;
using MailFathom.Mcp.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Covers what the <c>ask_mail</c> tool itself owns: converting arguments and publishing an answer.</summary>
/// <remarks>
/// <para>
/// The tool calls the real <see cref="MailboxQuestionReader" /> rather than a substitute for it, because the use case is
/// where the question bound, the account authorization, and the capability decision live, and a substitute would only
/// prove that the tool composes with a fiction. What is stubbed is the answering port below it.
/// </para>
/// <para>
/// Two properties are asserted throughout rather than in one test of their own: a refused call never reaches a run, and
/// no failure message carries the question or the value that was refused. Both hold for every path through the boundary.
/// </para>
/// </remarks>
public sealed class AskMailToolTests
{
    private const string Question = "what did the insurer agree to pay";

    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AskMailAsync_AQuestionAlone_AsksItOfEveryServedAccount()
    {
        // Arrange
        var answerer = new StubMailQuestionAnswerer().Answering("They agreed to pay 400.");
        var tool = ToolOver(answerer);

        // Act
        var result = await tool.AskMailAsync(Question, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(answerer.LastQuestion);
        Assert.Equal(Question, answerer.LastQuestion.Text.Value);
        Assert.Equal(
            [MailAccountId.Create(AnsweringDeployment.ServedAccountId)],
            answerer.LastQuestion.Scope.AccountIds);
        Assert.Equal("They agreed to pay 400.", result.Answer);
    }

    [Fact]
    public async Task AskMailAsync_AScopeNamedAsText_ConvertsItIntoTheIdentitiesTheRunIsBoundTo()
    {
        // Arrange
        var answerer = new StubMailQuestionAnswerer();
        var tool = ToolOver(answerer);

        // Act
        await tool.AskMailAsync(
            Question,
            accountIds: [AnsweringDeployment.ServedAccountId],
            folderAliases: ["archive"],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(answerer.LastQuestion);
        Assert.Equal(
            [MailAccountId.Create(AnsweringDeployment.ServedAccountId)],
            answerer.LastQuestion.Scope.AccountIds);
        Assert.Equal([MailFolderAlias.Create("ARCHIVE")], answerer.LastQuestion.Scope.FolderAliases);
    }

    [Fact]
    public async Task AskMailAsync_ARunThatRetrievedMail_PublishesOneCitationPerEmail()
    {
        // Arrange
        var answerer = new StubMailQuestionAnswerer().Answering(
            "They agreed to pay 400.",
            PassageOf(1),
            PassageOf(2));
        var tool = ToolOver(answerer);

        // Act
        var result = await tool.AskMailAsync(Question, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [EmailIdentityAt(1).ToString(), EmailIdentityAt(2).ToString()],
            result.Citations.Select(static citation => citation.StoredEmailId));
        Assert.All(
            result.Citations,
            citation =>
            {
                Assert.Equal(AnsweringDeployment.ServedAccountId, citation.AccountId);
                Assert.Equal("INBOX", citation.FolderAlias);
                Assert.Equal("Quarterly invoice", citation.Subject);
                Assert.Equal(Now, citation.ReceivedAt);
            });
    }

    /// <summary>An empty citation list is an ordinary answer: the mailbox was searched and held nothing about the question.</summary>
    [Fact]
    public async Task AskMailAsync_ARunThatRetrievedNothing_PublishesTheAnswerWithNoCitations()
    {
        // Arrange
        var tool = ToolOver(new StubMailQuestionAnswerer().Answering("I found no mail about that."));

        // Act
        var result = await tool.AskMailAsync(Question, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Citations);
        Assert.False(result.AnswerTruncated);
        Assert.False(result.CitationsTruncated);
    }

    [Fact]
    public async Task AskMailAsync_AnAnswerTheUseCaseCut_ReportsTheTruncation()
    {
        // Arrange
        var tool = ToolOver(
            new StubMailQuestionAnswerer().Answering(new string('a', 40)),
            MailAnswerBounds.Create(20, 20));

        // Act
        var result = await tool.AskMailAsync(Question, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(20, result.Answer.Length);
        Assert.True(result.AnswerTruncated);
    }

    /// <summary>How many of a mailbox's messages one call names is a deployment control, applied again on what is published.</summary>
    [Fact]
    public async Task AskMailAsync_MoreCitationsThanOneAnswerNames_CutsThemAndReportsTheTruncation()
    {
        // Arrange
        var tool = ToolOver(
            new StubMailQuestionAnswerer().Answering(
                "An answer.",
                [.. Enumerable.Range(1, 4).Select(PassageOf)]),
            MailAnswerBounds.Create(20_000, 2));

        // Act
        var result = await tool.AskMailAsync(Question, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Citations.Count);
        Assert.True(result.CitationsTruncated);
    }

    /// <summary>The question is the most revealing argument this surface takes, so a refusal names the filter and never the text.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AskMailAsync_AQuestionThatAsksNothing_IsRefusedWithoutReachingARun(string question)
    {
        // Arrange
        var answerer = new StubMailQuestionAnswerer();
        var tool = ToolOver(answerer);

        // Act
        var failure = await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(() =>
            tool.AskMailAsync(question, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("question", failure.FilterName);
        Assert.Null(answerer.LastQuestion);
    }

    [Fact]
    public async Task AskMailAsync_AnAccountThisDeploymentDoesNotServe_IsRefusedWithoutReachingARun()
    {
        // Arrange
        var answerer = new StubMailQuestionAnswerer();
        var tool = ToolOver(answerer);

        // Act
        await Assert.ThrowsAsync<MailAccountNotAccessibleException>(() => tool.AskMailAsync(
            Question,
            accountIds: ["somebody-elses"],
            cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Null(answerer.LastQuestion);
    }

    /// <summary>Text that names no identifier this system issues is refused at this boundary, and the value is never repeated.</summary>
    [Fact]
    public async Task AskMailAsync_AnAccountIdentifierNoSystemIssues_IsRefusedWithoutRepeatingIt()
    {
        // Arrange
        var answerer = new StubMailQuestionAnswerer();
        var tool = ToolOver(answerer);

        // Act
        var failure = await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(() => tool.AskMailAsync(
            Question,
            accountIds: ["with\na-newline"],
            cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.DoesNotContain("a-newline", failure.Message, StringComparison.Ordinal);
        Assert.Null(answerer.LastQuestion);
    }

    /// <summary>A client acting on a listing it read before the provider stopped answering meets the refusal rather than a run.</summary>
    [Fact]
    public async Task AskMailAsync_ADeploymentThatAnswersNoQuestions_IsRefusedWithACodedCapabilityFailure()
    {
        // Arrange
        var tool = new AskMailTool(
            AnsweringDeployment.QuestionReader(answerer: null),
            MailAnswerBounds.Default);

        // Act
        var failure = await Assert.ThrowsAsync<MailAnsweringUnavailableException>(() =>
            tool.AskMailAsync(Question, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailAnsweringAvailability.Inactive, failure.Availability);
    }

    private static AskMailTool ToolOver(StubMailQuestionAnswerer answerer, MailAnswerBounds? bounds = null)
    {
        // One instance for both, as the host composes them: the use case cuts what it publishes by these bounds and the
        // boundary applies the citation count again to what came back.
        var answerBounds = bounds ?? MailAnswerBounds.Default;

        return new AskMailTool(AnsweringDeployment.QuestionReader(answerer, answerBounds), answerBounds);
    }

    private static EmailKnowledgePassage PassageOf(int position) => new()
    {
        StoredEmailId = StoredEmailId.Create(EmailIdentityAt(position)),
        AccountId = MailAccountId.Create(AnsweringDeployment.ServedAccountId),
        FolderAlias = MailFolderAlias.Create("INBOX"),
        Subject = "Quarterly invoice",
        ReceivedAt = Now,
        Text = "an extract",
    };

    /// <summary>Names one email by its position, so the same run of a test always uses the same identifiers.</summary>
    private static Guid EmailIdentityAt(int position) => new($"00000000-0000-0000-0000-{position:D12}");
}
