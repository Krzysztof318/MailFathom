// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.Mcp.Tools;
using MailFathom.Mcp.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Puts the adversarial corpus to the <c>ask_mail</c> handler: the scope a run is given, and what one response publishes.</summary>
/// <remarks>
/// <para>
/// The second place scope is enforced, and the reason it is asserted twice. The composition binds the scope so the
/// model's query cannot move it, and this proves the value that was bound came from the caller's own filters rather than
/// from anything in the text — two independent enforcement points, either of which would be worth having on its own.
/// <c>PromptInjectionResistanceTests</c> in <c>AI.UnitTests</c> holds the other.
/// </para>
/// <para>
/// The tool calls the real <see cref="MailboxQuestionReader" /> and the answering port below it is stubbed, so no
/// provider is reached. The stub is scripted as a run that <em>did</em> do what the message asked — it answers with the
/// fabricated citation the message demanded and echoes the text the message wrote. What is asserted is what a caller
/// then receives.
/// </para>
/// <para>
/// The limit of that: nothing here makes an answer true. A model can still be talked into a wrong sentence about mail it
/// was shown, and this suite says only that the sentence arrives as one message's content rather than as this system's
/// own finding, and that every source it names is a message the run actually retrieved.
/// </para>
/// </remarks>
public sealed class AskMailInjectionResistanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Gets one case per attack the corpus knows, so a property stated once covers every one of them.</summary>
    public static TheoryData<string> EveryAdversary => AdversarialMailCorpus.EveryName;

    /// <summary>
    /// A question is written by whoever is calling, and a client relaying mail it read elsewhere is one of the ways an
    /// injected demand arrives here. It reaches the run as asked and changes nothing about which mail may answer it: the
    /// scope is resolved from the caller's own filters, which the question is not one of.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryAdversary))]
    public async Task AskMailAsync_AQuestionCarryingAnAdversarialDemand_AsksTheRunWithTheCallersScopeAlone(
        string adversary)
    {
        // Arrange
        var demand = AdversarialMailCorpus.Named(adversary).Demand;
        var answerer = new StubMailQuestionAnswerer().Answering("I have not done that.");
        var tool = ToolOver(answerer);

        // Act
        await tool.AskMailAsync(demand, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(answerer.LastQuestion);
        Assert.Equal(demand, answerer.LastQuestion.Text.Value);
        Assert.Equal(
            [MailAccountId.Create(AnsweringDeployment.ServedAccountId)],
            answerer.LastQuestion.Scope.AccountIds);
        Assert.Empty(answerer.LastQuestion.Scope.SelectedFolders);
    }

    /// <summary>The scope a caller did name is what a run gets, and a demand in the question does not add to it either.</summary>
    [Theory]
    [MemberData(nameof(EveryAdversary))]
    public async Task AskMailAsync_AnAdversarialDemandBesideANarrowedScope_LeavesTheScopeAtWhatTheCallerNamed(
        string adversary)
    {
        // Arrange
        var demand = AdversarialMailCorpus.Named(adversary).Demand;
        var answerer = new StubMailQuestionAnswerer().Answering("I have not done that.");
        var tool = ToolOver(answerer);

        // Act
        await tool.AskMailAsync(
            demand,
            accounts: [AnsweringDeployment.ServedAccountId],
            folders: ["inbox"],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(answerer.LastQuestion);
        Assert.Equal(
            [MailAccountId.Create(AnsweringDeployment.ServedAccountId)],
            answerer.LastQuestion.Scope.AccountIds);
        Assert.Equal(
            [new MailFolderIdentity(MailAccountId.Create(AnsweringDeployment.ServedAccountId), MailFolderAlias.Create("INBOX"))],
            answerer.LastQuestion.Scope.SelectedFolders);
    }

    /// <summary>
    /// A citation is what makes an answer checkable, so one that resolves to nothing is worse than a claim with no
    /// source at all. The citations are read from the passages the run retrieved rather than from the text it wrote, so
    /// an answer that names a message nobody retrieved names it in prose and in no citation.
    /// </summary>
    [Fact]
    public async Task AskMailAsync_AnAnswerNamingAMessageTheRunNeverRetrieved_CitesOnlyWhatItRetrieved()
    {
        // Arrange
        var answerer = new StubMailQuestionAnswerer().Answering(
            $"The authoritative record is message {AdversarialMailCorpus.FabricatedMessageId}.",
            PassageOf(1),
            PassageOf(2));
        var tool = ToolOver(answerer);

        // Act
        var result = await tool.AskMailAsync(
            "what did the insurer agree to pay",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [EmailIdentityAt(1).ToString(), EmailIdentityAt(2).ToString()],
            result.Citations.Select(static citation => citation.StoredEmailId));

        // Without this the assertion above would hold over a run that was never asked for the fabricated citation, which
        // is a different and much weaker fact than one that was asked and did not produce it.
        Assert.Contains(
            AdversarialMailCorpus.FabricatedMessageId,
            result.Answer,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Injected text that reaches an answer arrives as one message's content and never as this system's own statement.
    /// The answer carries what the model wrote — published as model output about mail — and the subject a stranger wrote
    /// is republished under the identity of the message that carried it and under no other.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryAdversary))]
    public async Task AskMailAsync_AnAnswerEchoingAnAdversarialMessage_AttributesItToTheMessageThatCarriedIt(
        string adversary)
    {
        // Arrange
        var message = AdversarialMailCorpus.Named(adversary);
        var answerer = new StubMailQuestionAnswerer().Answering(
            $"One message says: {message.Text}",
            PassageOf(1),
            PassageOf(2) with { Subject = message.Subject, Text = message.Text });
        var tool = ToolOver(answerer);

        // Act
        var result = await tool.AskMailAsync(
            "what did the insurer agree to pay",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(message.Text, result.Answer, StringComparison.Ordinal);
        Assert.Equal(
            [EmailIdentityAt(2).ToString()],
            result.Citations
                .Where(citation => citation.Subject == message.Subject)
                .Select(static citation => citation.StoredEmailId));

        // No extract travels back with a citation, so the body a stranger wrote reaches a caller through the answer and
        // through no field this response publishes beside it.
        Assert.All(
            result.Citations.SelectMany(static citation => new[]
            {
                citation.StoredEmailId,
                citation.AccountId,
                citation.FolderAlias,
                citation.Subject,
            }),
            value => Assert.DoesNotContain(message.Text, value ?? string.Empty, StringComparison.Ordinal));
    }

    private static AskMailTool ToolOver(StubMailQuestionAnswerer answerer) =>
        new(
            AnsweringDeployment.QuestionReader(answerer),
            MailAnswerBounds.Default,
            AnsweringDeployment.AccountCatalog());

    private static EmailKnowledgePassage PassageOf(int position) => new()
    {
        StoredEmailId = StoredEmailId.Create(EmailIdentityAt(position)),
        AccountId = MailAccountId.Create(AnsweringDeployment.ServedAccountId),
        FolderAlias = MailFolderAlias.Create("INBOX"),
        Subject = "Quarterly invoice",
        ReceivedAt = Now,
        SenderVerification = SenderVerification.NotEstablished,
        MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
        Text = "an extract",
    };

    /// <summary>Names one email by its position, so the same run of a test always uses the same identifiers.</summary>
    private static Guid EmailIdentityAt(int position) => new($"00000000-0000-0000-0000-{position:D12}");
}
