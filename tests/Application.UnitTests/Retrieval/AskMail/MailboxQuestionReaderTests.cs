// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Retrieval.AskMail;

/// <summary>Covers what the use case owns: the question, the scope it may be answered from, and what one answer publishes.</summary>
/// <remarks>
/// The answering port is a recording double rather than a run, because everything a real run would add — the provider,
/// the tool loop, the passages it retrieves — is proved where it lives. What is proved here is that a refused request
/// never reaches a run at all, and that what a run produced is published under bounds a caller cannot widen.
/// </remarks>
public sealed class MailboxQuestionReaderTests
{
    private const string ServedAccountId = "personal";

    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly EmbeddingProfileId ProfileId =
        EmbeddingProfileId.Create(new Guid("0f9d6b0b-2f1e-4c2a-9a3d-7c8e5f4a1b20"));

    [Fact]
    public async Task AnswerQuestionAsync_AQuestionOverEveryServedAccount_AsksTheRunWithTheResolvedScope()
    {
        // Arrange
        var answerer = new RecordingMailQuestionAnswerer().Answering("The invoice was attached.");
        var reader = ReaderOver(answerer);

        // Act
        var result = await AnswerAsync(reader, new AskMailRequest { QuestionText = "was the invoice attached" });

        // Assert
        var question = Assert.Single(answerer.Questions);
        Assert.Equal("was the invoice attached", question.Text.Value);
        Assert.Equal([MailAccountId.Create(ServedAccountId)], question.Scope.AccountIds);
        Assert.Empty(question.Scope.FolderAliases);
        Assert.Equal("The invoice was attached.", result.AnswerText);
    }

    [Fact]
    public async Task AnswerQuestionAsync_AQuestionNamingAScope_AsksTheRunWithThatScopeAlone()
    {
        // Arrange
        var answerer = new RecordingMailQuestionAnswerer();
        var reader = ReaderOver(answerer);

        // Act
        await AnswerAsync(
            reader,
            new AskMailRequest
            {
                QuestionText = "what did the insurer agree to pay",
                AccountIds = [MailAccountId.Create(ServedAccountId)],
                FolderAliases = [MailFolderAlias.Create("archive")],
            });

        // Assert
        var question = Assert.Single(answerer.Questions);
        Assert.Equal([MailAccountId.Create(ServedAccountId)], question.Scope.AccountIds);
        Assert.Equal([MailFolderAlias.Create("ARCHIVE")], question.Scope.FolderAliases);
    }

    /// <summary>The access decision is made before a provider is reached, so an unserved account costs nothing to refuse.</summary>
    [Fact]
    public async Task AnswerQuestionAsync_AnAccountThisDeploymentDoesNotServe_IsRefusedWithoutARun()
    {
        // Arrange
        var answerer = new RecordingMailQuestionAnswerer();
        var reader = ReaderOver(answerer);

        // Act
        await Assert.ThrowsAsync<MailAccountNotAccessibleException>(() => AnswerAsync(
            reader,
            new AskMailRequest
            {
                QuestionText = "was the invoice attached",
                AccountIds = [MailAccountId.Create("somebody-elses")],
            }));

        // Assert
        Assert.Empty(answerer.Questions);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnswerQuestionAsync_AQuestionThatAsksNothing_IsRefusedWithoutARun(string? questionText)
    {
        // Arrange
        var answerer = new RecordingMailQuestionAnswerer();
        var reader = ReaderOver(answerer);

        // Act
        var failure = await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(
            () => AnswerAsync(reader, new AskMailRequest { QuestionText = questionText }));

        // Assert
        Assert.Equal("question", failure.FilterName);
        Assert.Empty(answerer.Questions);
    }

    /// <summary>A deployment that answers questions and one that does not refuse a malformed one identically, so neither reveals which it is.</summary>
    [Fact]
    public async Task AnswerQuestionAsync_AMalformedQuestionOnADeploymentThatAnswersNone_ReportsTheQuestionRatherThanTheCapability()
    {
        // Arrange
        var reader = ReaderOver(answerer: null);

        // Act
        var failure = await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(
            () => AnswerAsync(reader, new AskMailRequest { QuestionText = "  " }));

        // Assert
        Assert.Equal("question", failure.FilterName);
    }

    [Fact]
    public async Task AnswerQuestionAsync_ADeploymentThatAnswersNoQuestions_ReportsTheCapabilityAsAbsent()
    {
        // Arrange
        var reader = ReaderOver(answerer: null);

        // Act
        var failure = await Assert.ThrowsAsync<MailAnsweringUnavailableException>(
            () => AnswerAsync(reader, new AskMailRequest { QuestionText = "was the invoice attached" }));

        // Assert
        Assert.Equal(MailAnsweringAvailability.Inactive, failure.Availability);
    }

    /// <summary>The tool is withheld while this holds, so a caller only meets it by acting on a listing it read before the provider stopped answering.</summary>
    [Fact]
    public async Task AnswerQuestionAsync_ADeploymentWhoseProviderIsRefusing_ReportsTheCapabilityAsTemporarilyUnable()
    {
        // Arrange
        var answerer = new RecordingMailQuestionAnswerer();
        var reader = ReaderOver(answerer, chatState: AiProviderHealthState.Misconfigured);

        // Act
        var failure = await Assert.ThrowsAsync<MailAnsweringUnavailableException>(
            () => AnswerAsync(reader, new AskMailRequest { QuestionText = "was the invoice attached" }));

        // Assert
        Assert.Equal(MailAnsweringAvailability.Degraded, failure.Availability);
        Assert.Empty(answerer.Questions);
    }

    /// <summary>A run makes several lookups and one message can answer more than one, so the sources are the messages rather than the findings.</summary>
    [Fact]
    public async Task AnswerQuestionAsync_AMessageRetrievedTwice_IsCitedOnceInTheOrderItWasFirstReached()
    {
        // Arrange
        var answerer = new RecordingMailQuestionAnswerer().Answering(
            "The insurer agreed to pay 400.",
            PassageOf(1, "the claim was filed"),
            PassageOf(2, "we will pay 400"),
            PassageOf(1, "the claim was filed"));
        var reader = ReaderOver(answerer);

        // Act
        var result = await AnswerAsync(reader, new AskMailRequest { QuestionText = "what was agreed" });

        // Assert
        Assert.Equal(
            [StoredEmailId.Create(EmailIdentityAt(1)), StoredEmailId.Create(EmailIdentityAt(2))],
            result.Citations.Select(static citation => citation.StoredEmailId));
        Assert.False(result.CitationsWereTruncated);
    }

    [Fact]
    public async Task AnswerQuestionAsync_ACitedEmail_CarriesTheIdentityAndTheFieldsThatRecognizeIt()
    {
        // Arrange
        var answerer = new RecordingMailQuestionAnswerer().Answering("An answer.", PassageOf(1, "an extract"));
        var reader = ReaderOver(answerer);

        // Act
        var result = await AnswerAsync(reader, new AskMailRequest { QuestionText = "what was agreed" });

        // Assert
        var citation = Assert.Single(result.Citations);
        Assert.Equal(StoredEmailId.Create(EmailIdentityAt(1)), citation.StoredEmailId);
        Assert.Equal(MailAccountId.Create(ServedAccountId), citation.AccountId);
        Assert.Equal(MailFolderAlias.Create("INBOX"), citation.FolderAlias);
        Assert.Equal("Quarterly invoice", citation.Subject);
        Assert.Equal(Now, citation.ReceivedAt);
    }

    /// <summary>Retrieval finding nothing is an ordinary outcome, and the answer that says so is a real answer.</summary>
    [Fact]
    public async Task AnswerQuestionAsync_ARunThatRetrievedNothing_PublishesTheAnswerWithNoCitations()
    {
        // Arrange
        var answerer = new RecordingMailQuestionAnswerer().Answering("I found no mail about that.");
        var reader = ReaderOver(answerer);

        // Act
        var result = await AnswerAsync(reader, new AskMailRequest { QuestionText = "what was agreed" });

        // Assert
        Assert.Equal("I found no mail about that.", result.AnswerText);
        Assert.Empty(result.Citations);
        Assert.False(result.AnswerWasTruncated);
        Assert.False(result.CitationsWereTruncated);
    }

    [Fact]
    public async Task AnswerQuestionAsync_AnAnswerLongerThanOneResponseCarries_IsCutAndSaysSo()
    {
        // Arrange
        var answerer = new RecordingMailQuestionAnswerer().Answering(new string('a', 40));
        var reader = ReaderOver(answerer, bounds: MailAnswerBounds.Create(20, 20));

        // Act
        var result = await AnswerAsync(reader, new AskMailRequest { QuestionText = "what was agreed" });

        // Assert
        Assert.Equal(20, result.AnswerText.Length);
        Assert.True(result.AnswerWasTruncated);
    }

    /// <summary>A cut that fell between the halves of a surrogate pair would publish text no serialization survives.</summary>
    [Fact]
    public async Task AnswerQuestionAsync_ACutFallingInsideASurrogatePair_TakesTheWholePair()
    {
        // Arrange
        var answerer = new RecordingMailQuestionAnswerer().Answering(new string('a', 19) + "\U0001F600");
        var reader = ReaderOver(answerer, bounds: MailAnswerBounds.Create(20, 20));

        // Act
        var result = await AnswerAsync(reader, new AskMailRequest { QuestionText = "what was agreed" });

        // Assert
        Assert.Equal(19, result.AnswerText.Length);
        Assert.True(result.AnswerWasTruncated);
    }

    [Fact]
    public async Task AnswerQuestionAsync_ARunCitingMoreEmailsThanOneResponseNames_CutsThemAndSaysSo()
    {
        // Arrange
        var answerer = new RecordingMailQuestionAnswerer().Answering(
            "An answer.",
            [.. Enumerable.Range(1, 4).Select(position => PassageOf(position, "an extract"))]);
        var reader = ReaderOver(answerer, bounds: MailAnswerBounds.Create(20_000, 2));

        // Act
        var result = await AnswerAsync(reader, new AskMailRequest { QuestionText = "what was agreed" });

        // Assert
        Assert.Equal(2, result.Citations.Count);
        Assert.True(result.CitationsWereTruncated);
        Assert.False(result.AnswerWasTruncated);
    }

    private static Task<AskMailResult> AnswerAsync(MailboxQuestionReader reader, AskMailRequest request) =>
        reader.AnswerQuestionAsync(request, TestContext.Current.CancellationToken);

    private static EmailKnowledgePassage PassageOf(int position, string text) => new()
    {
        StoredEmailId = StoredEmailId.Create(EmailIdentityAt(position)),
        AccountId = MailAccountId.Create(ServedAccountId),
        FolderAlias = MailFolderAlias.Create("INBOX"),
        Subject = "Quarterly invoice",
        ReceivedAt = Now,
        Text = text,
    };

    /// <summary>Names one email by its position, so the same run of a test always uses the same identifiers.</summary>
    private static Guid EmailIdentityAt(int position) => new($"00000000-0000-0000-0000-{position:D12}");

    private static MailboxQuestionReader ReaderOver(
        IMailQuestionAnswerer? answerer,
        AiProviderHealthState chatState = AiProviderHealthState.Serving,
        MailAnswerBounds? bounds = null)
    {
        var healthReader = Substitute.For<IAiProviderHealthReader>();
        healthReader.Read(AiProviderRole.Embedding)
            .Returns(new AiProviderHealth(AiProviderRole.Embedding, AiProviderHealthState.Serving, Now));
        healthReader.Read(AiProviderRole.Chat)
            .Returns(new AiProviderHealth(AiProviderRole.Chat, chatState, Now));

        var identity = EmbeddingProfileIdentity.Create(
            "a-provider",
            "a-model",
            modelVersion: null,
            dimension: 8,
            EmbeddingDistanceMetric.Cosine,
            EmbeddingInputPreparation.Create(2_000, passageInstruction: null, normalizesVector: true));

        var profileReader = Substitute.For<IActiveEmbeddingProfileReader>();
        profileReader.FindActiveProfileAsync(Arg.Any<CancellationToken>())
            .Returns(new RegisteredEmbeddingProfile(ProfileId, identity));

        var timeProvider = new FakeTimeProvider(Now);

        return new MailboxQuestionReader(
            new MailAnsweringCapability(
                new SemanticEmailSearch(
                    profileReader,
                    new InMemoryEmailVectorSearchIndex(),
                    healthReader,
                    timeProvider,
                    new ScriptedTextEmbeddingGenerator(identity, maximumPassagesPerCall: 8)),
                healthReader,
                timeProvider,
                answerer),
            new MailboxScopeResolver(CatalogServing(MailAccountId.Create(ServedAccountId))),
            bounds ?? MailAnswerBounds.Default);
    }

    private static IMailAccountCatalog CatalogServing(params MailAccountId[] servedAccountIds)
    {
        var catalog = Substitute.For<IMailAccountCatalog>();
        catalog.ServedAccountIds.Returns([.. servedAccountIds]);

        return catalog;
    }
}
