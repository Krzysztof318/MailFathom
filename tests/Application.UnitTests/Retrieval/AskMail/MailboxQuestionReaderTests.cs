// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.Retrieval.AskMail.Audit;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Answering.Audit;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
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

    /// <summary>The literal the scanner in the guarded-egress tests reports, standing in for a credential in mail.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

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
        Assert.Empty(question.Scope.SelectedFolders);
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
                Accounts = [MailAccountSelector.Create(ServedAccountId)],
                Folders = [MailFolderReference.ToAlias(MailFolderAlias.Create("archive"))],
            });

        // Assert
        var question = Assert.Single(answerer.Questions);
        Assert.Equal([MailAccountId.Create(ServedAccountId)], question.Scope.AccountIds);
        Assert.Equal(
            [new MailFolderIdentity(MailAccountId.Create(ServedAccountId), MailFolderAlias.Create("ARCHIVE"))],
            question.Scope.SelectedFolders);
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
                Accounts = [MailAccountSelector.Create("somebody-elses")],
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

    /// <summary>The period's allowance is what a deployment agreed to spend, and reaching it refuses the question rather than degrading it.</summary>
    [Fact]
    public async Task AnswerQuestionAsync_APeriodWithNoAllowanceLeft_RefusesTheQuestionWithoutStartingARun()
    {
        // Arrange
        var answerer = new RecordingMailQuestionAnswerer();
        var spentLedger = Substitute.For<IMailAnsweringSpendLedger>();
        spentLedger.TryAdmitRun().Returns(false);
        var reader = ReaderOver(answerer, spendLedger: spentLedger);

        // Act
        var failure = await Assert.ThrowsAsync<MailAnsweringBudgetExhaustedException>(
            () => AnswerAsync(reader, new AskMailRequest { QuestionText = "what was agreed" }));

        // Assert
        Assert.Equal(MailAnsweringBudgetScope.Period, failure.Scope);
        Assert.Empty(answerer.Questions);
    }

    /// <summary>
    /// A question a deployment was never going to answer must not be charged against a ceiling on what it spends, so the
    /// capability is read first and the allowance is taken only once a run is about to begin.
    /// </summary>
    [Fact]
    public async Task AnswerQuestionAsync_ADeploymentThatAnswersNoQuestions_TakesNoAllowanceFromThePeriod()
    {
        // Arrange
        var ledger = Substitute.For<IMailAnsweringSpendLedger>();
        ledger.TryAdmitRun().Returns(true);
        var reader = ReaderOver(answerer: null, spendLedger: ledger);

        // Act
        await Assert.ThrowsAsync<MailAnsweringUnavailableException>(
            () => AnswerAsync(reader, new AskMailRequest { QuestionText = "was the invoice attached" }));

        // Assert
        ledger.DidNotReceive().TryAdmitRun();
    }

    /// <summary>A run stopped from reading further answered a narrower reading of the mailbox, and only saying so keeps that distinguishable.</summary>
    [Fact]
    public async Task AnswerQuestionAsync_ARunThatReachedItsRetrievalCeiling_PublishesThatTheMailboxWasNotReadInFull()
    {
        // Arrange
        var answerer = new RecordingMailQuestionAnswerer()
            .Answering("Partly, from what I could read.", PassageOf(1, "the claim was filed"))
            .HavingReachedTheRetrievalCeiling();
        var reader = ReaderOver(answerer);

        // Act
        var result = await AnswerAsync(reader, new AskMailRequest { QuestionText = "what was agreed" });

        // Assert
        Assert.True(result.RetrievalWasTruncated);
        Assert.False(result.AnswerWasTruncated);
        Assert.False(result.CitationsWereTruncated);
    }

    [Fact]
    public async Task AnswerQuestionAsync_ARunInsideEveryCeiling_PublishesNoTruncationAtAll()
    {
        // Arrange
        var answerer = new RecordingMailQuestionAnswerer()
            .Answering("The insurer agreed to pay 400.", PassageOf(1, "we will pay 400"));
        var reader = ReaderOver(answerer);

        // Act
        var result = await AnswerAsync(reader, new AskMailRequest { QuestionText = "what was agreed" });

        // Assert
        Assert.False(result.RetrievalWasTruncated);
        Assert.False(result.AnswerWasTruncated);
        Assert.False(result.CitationsWereTruncated);
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
        var cited = PassageOf(
            1,
            "an extract",
            senderVerification: new SenderVerification
            {
                AuthorAuthentication = AuthorAuthenticationOutcome.Authenticated,
                DeploymentTrust = SenderTrustLevel.Trusted,
            },
            machineAuthorship: MachineAuthorshipAssessment.Assessed(
                MachineAuthorshipBand.Possible,
                likelihood: 0.42,
                MachineAuthorshipSignals.HiddenCharacters,
                MachineAuthorshipProfile.Standard.Revision));
        var answerer = new RecordingMailQuestionAnswerer().Answering("An answer.", cited);
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
        Assert.Equal(AuthorAuthenticationOutcome.Authenticated, citation.SenderVerification.AuthorAuthentication);
        Assert.Equal(SenderTrustLevel.Trusted, citation.SenderVerification.DeploymentTrust);
        Assert.Equal(MachineAuthorshipBand.Possible, citation.MachineAuthorship.Band);
        Assert.Equal(0.42, citation.MachineAuthorship.Likelihood);
        Assert.Equal(MachineAuthorshipSignals.HiddenCharacters, citation.MachineAuthorship.Signals);
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

    /// <summary>The record of a clean run: what it read, which of that the answer named, and what produced it.</summary>
    [Fact]
    public async Task AnswerQuestionAsync_AnAnsweredQuestion_ReportsAndRecordsWhatTheRunRead()
    {
        // Arrange
        var read = PassageOf(1, "we will pay 4200");
        var answerer = new RecordingMailQuestionAnswerer().Answering("They agreed to pay 4200.", read);
        var runTelemetry = new RecordingMailAnsweringRunTelemetry();
        var auditTrail = new RecordingMailAnsweringAuditTrail();
        var reader = ReaderOver(answerer, runTelemetry: runTelemetry, auditTrail: auditTrail);

        // Act
        await AnswerAsync(reader, new AskMailRequest { QuestionText = "what was agreed" });

        // Assert
        var recorded = Assert.Single(auditTrail.Runs);

        Assert.Equal(MailAnsweringRunOutcome.Answered, recorded.Outcome);
        Assert.Equal([read.StoredEmailId], recorded.RetrievedEmailIds);
        Assert.Equal([read.StoredEmailId], recorded.CitedEmailIds);
        Assert.Equal(
            (RecordingMailQuestionAnswerer.EndpointAlias, RecordingMailQuestionAnswerer.InstructionsVersion),
            (recorded.ChatEndpointAlias, recorded.InstructionsVersion));
        Assert.Equal(
            (1, 1, 1, MailAnsweringRunOutcome.Answered),
            (runTelemetry.Published?.CandidateCount,
                runTelemetry.Published?.RelevantCandidateCount,
                runTelemetry.Published?.PassageCount,
                runTelemetry.Published?.Outcome));
    }

    /// <summary>A message the run read and the response could not name is exactly what the difference is for.</summary>
    [Fact]
    public async Task AnswerQuestionAsync_MoreEmailsReadThanTheResponseNames_RecordsTheCitedOnesAsASubset()
    {
        // Arrange
        var answerer = new RecordingMailQuestionAnswerer().Answering(
            "An answer.",
            [.. Enumerable.Range(1, 3).Select(position => PassageOf(position, "an extract"))]);
        var auditTrail = new RecordingMailAnsweringAuditTrail();
        var reader = ReaderOver(answerer, bounds: MailAnswerBounds.Create(20_000, 2), auditTrail: auditTrail);

        // Act
        await AnswerAsync(reader, new AskMailRequest { QuestionText = "what was agreed" });

        // Assert
        var recorded = Assert.Single(auditTrail.Runs);

        Assert.Equal(3, recorded.RetrievedEmailIds.Count);
        Assert.Equal(
            [StoredEmailId.Create(EmailIdentityAt(1)), StoredEmailId.Create(EmailIdentityAt(2))],
            recorded.CitedEmailIds);
    }

    /// <summary>Each way a run reads less of a mailbox reaches the report and the record as itself.</summary>
    [Theory]
    [InlineData(false, false, MailAnsweringRunDegradation.None)]
    [InlineData(true, false, MailAnsweringRunDegradation.RetrievalCeilingReached)]
    [InlineData(false, true, MailAnsweringRunDegradation.RelevanceFilterFellBack)]
    [InlineData(
        true,
        true,
        MailAnsweringRunDegradation.RetrievalCeilingReached | MailAnsweringRunDegradation.RelevanceFilterFellBack)]
    public async Task AnswerQuestionAsync_ADegradedRun_ReportsAndRecordsHowItDegraded(
        bool reachedTheCeiling,
        bool filterFellBack,
        MailAnsweringRunDegradation expected)
    {
        // Arrange
        var answerer = new RecordingMailQuestionAnswerer().Answering("An answer.", PassageOf(1, "an extract"));

        if (reachedTheCeiling)
        {
            answerer.HavingReachedTheRetrievalCeiling();
        }

        if (filterFellBack)
        {
            answerer.HavingFallenBackToTheUnjudgedRanking();
        }

        var runTelemetry = new RecordingMailAnsweringRunTelemetry();
        var auditTrail = new RecordingMailAnsweringAuditTrail();
        var reader = ReaderOver(answerer, runTelemetry: runTelemetry, auditTrail: auditTrail);

        // Act
        var result = await AnswerAsync(reader, new AskMailRequest { QuestionText = "what was agreed" });

        // Assert
        Assert.Equal(expected, Assert.Single(auditTrail.Runs).Degradation);
        Assert.Equal(expected, runTelemetry.Published?.Degradation);
        Assert.Equal(reachedTheCeiling, result.RetrievalWasTruncated);
    }

    /// <summary>
    /// The record of a run that failed is the one most worth having: it read somebody's mail before it failed, and the
    /// ending is what an operator diagnoses from.
    /// </summary>
    [Theory]
    [InlineData(nameof(ChatGenerationFailure.AnswerEmpty), MailAnsweringRunOutcome.AnswerEmpty)]
    [InlineData(nameof(ChatGenerationFailure.RateLimited), MailAnsweringRunOutcome.ProviderFailed)]
    public async Task AnswerQuestionAsync_ARunTheProviderEnded_ReportsAndRecordsTheEndingAndWhatItHadRead(
        string failure,
        MailAnsweringRunOutcome expected)
    {
        // Arrange
        var read = PassageOf(1, "we will pay 4200");
        var answerer = new RecordingMailQuestionAnswerer()
            .Answering("never published", read)
            .Failing(new ChatGenerationFailedException(
                "answering",
                Enum.Parse<ChatGenerationFailure>(failure)));
        var runTelemetry = new RecordingMailAnsweringRunTelemetry();
        var auditTrail = new RecordingMailAnsweringAuditTrail();
        var reader = ReaderOver(answerer, runTelemetry: runTelemetry, auditTrail: auditTrail);

        // Act
        await Assert.ThrowsAsync<ChatGenerationFailedException>(() =>
            AnswerAsync(reader, new AskMailRequest { QuestionText = "what was agreed" }));

        // Assert
        var recorded = Assert.Single(auditTrail.Runs);

        Assert.Equal(expected, recorded.Outcome);
        Assert.Equal([read.StoredEmailId], recorded.RetrievedEmailIds);
        Assert.Empty(recorded.CitedEmailIds);
        Assert.Equal(expected, runTelemetry.Published?.Outcome);
    }

    /// <summary>A run stopped by what one question may spend is a ceiling being met rather than an unnamed failure.</summary>
    [Fact]
    public async Task AnswerQuestionAsync_ARunThatReachedWhatOneQuestionMaySpend_RecordsThatEnding()
    {
        // Arrange
        var answerer = new RecordingMailQuestionAnswerer()
            .Answering("never published")
            .Failing(MailAnsweringBudgetExhaustedException.RunSpent());
        var auditTrail = new RecordingMailAnsweringAuditTrail();
        var reader = ReaderOver(answerer, auditTrail: auditTrail);

        // Act
        await Assert.ThrowsAsync<MailAnsweringBudgetExhaustedException>(() =>
            AnswerAsync(reader, new AskMailRequest { QuestionText = "what was agreed" }));

        // Assert
        Assert.Equal(MailAnsweringRunOutcome.RunBudgetExhausted, Assert.Single(auditTrail.Runs).Outcome);
    }

    /// <summary>A question refused before a run began is not a run, so nothing is reported and nothing is recorded.</summary>
    [Fact]
    public async Task AnswerQuestionAsync_AQuestionThePeriodHasNoAllowanceFor_ReportsAndRecordsNoRun()
    {
        // Arrange
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        spendLedger.TryAdmitRun().Returns(false);
        var runTelemetry = new RecordingMailAnsweringRunTelemetry();
        var auditTrail = new RecordingMailAnsweringAuditTrail();
        var reader = ReaderOver(
            new RecordingMailQuestionAnswerer(),
            spendLedger: spendLedger,
            runTelemetry: runTelemetry,
            auditTrail: auditTrail);

        // Act
        await Assert.ThrowsAsync<MailAnsweringBudgetExhaustedException>(() =>
            AnswerAsync(reader, new AskMailRequest { QuestionText = "what was agreed" }));

        // Assert
        Assert.Equal(0, runTelemetry.OpenedCount);
        Assert.Empty(auditTrail.Runs);
    }

    /// <summary>An answer is mail content restated, and it is the one text of a run nothing else has looked at.</summary>
    [Fact]
    public async Task AnswerQuestionAsync_ASwitchedOnScanner_RedactsTheAnswerBeforeItIsPublished()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var answerer = new RecordingMailQuestionAnswerer().Answering($"they sent the key {Marker} on Tuesday");
        var reader = ReaderOver(answerer, egressGuard: egress.Guard);

        // Act
        var result = await AnswerAsync(reader, new AskMailRequest { QuestionText = "what key did they send" });

        // Assert
        Assert.Equal("they sent the key [redacted:CloudKey] on Tuesday", result.AnswerText);
    }

    /// <summary>A citation carries one text of the message it names, and it is published to the caller like any other.</summary>
    [Fact]
    public async Task AnswerQuestionAsync_ASwitchedOnScanner_RedactsTheSubjectEveryCitationCarries()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var answerer = new RecordingMailQuestionAnswerer().Answering(
            "an answer",
            PassageOf(1, "the extract") with { Subject = $"re: {Marker}" });
        var reader = ReaderOver(answerer, egressGuard: egress.Guard);

        // Act
        var result = await AnswerAsync(reader, new AskMailRequest { QuestionText = "what key did they send" });

        // Assert
        Assert.Equal("re: [redacted:CloudKey]", Assert.Single(result.Citations).Subject);
    }

    /// <summary>A subject no response will publish is a scan nobody needs, and under the analyzer it is a round trip too.</summary>
    [Fact]
    public async Task AnswerQuestionAsync_MoreCitationsThanOneResponseNames_ScansOnlyTheOnesItPublishes()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var answerer = new RecordingMailQuestionAnswerer().Answering(
            "an answer",
            [.. Enumerable.Range(1, 4).Select(position =>
                PassageOf(position, "an extract") with { Subject = $"re: {position}" })]);
        var reader = ReaderOver(answerer, bounds: MailAnswerBounds.Create(20_000, 2), egressGuard: egress.Guard);

        // Act
        var result = await AnswerAsync(reader, new AskMailRequest { QuestionText = "what was agreed" });

        // Assert
        Assert.Equal(2, result.Citations.Count);
        Assert.True(result.CitationsWereTruncated);
        Assert.Equal(["an answer", "re: 1", "re: 2"], egress.Scanner.ScannedTexts);
    }

    /// <summary>Serving an answer a scanner could not read would be the leak the switch was turned on to prevent.</summary>
    [Fact]
    public async Task AnswerQuestionAsync_ADetectorThatCannotAnswer_RefusesTheResponseRatherThanServingItUnscanned()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Unavailable(TimeProvider.System);
        var answerer = new RecordingMailQuestionAnswerer().Answering("an ordinary answer");
        var reader = ReaderOver(answerer, egressGuard: egress.Guard);

        // Act, Assert
        await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(() =>
            AnswerAsync(reader, new AskMailRequest { QuestionText = "what was agreed" }));
    }

    /// <summary>An opt-in nobody took must not appear on this path at all.</summary>
    [Fact]
    public async Task AnswerQuestionAsync_ADeploymentThatScansNothing_PublishesTheAnswerUntouched()
    {
        // Arrange
        var answerer = new RecordingMailQuestionAnswerer().Answering($"they sent the key {Marker} on Tuesday");
        var reader = ReaderOver(answerer);

        // Act
        var result = await AnswerAsync(reader, new AskMailRequest { QuestionText = "what key did they send" });

        // Assert
        Assert.Equal($"they sent the key {Marker} on Tuesday", result.AnswerText);
    }

    /// <summary>Whether a credential may cause mail content to leave this process is the decision this grant carries, so it is asked for with the transport absent.</summary>
    [Fact]
    public async Task AnswerQuestionAsync_ACallerGrantedOnlyTheMailboxReadPermission_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var answerer = new RecordingMailQuestionAnswerer().Answering("The invoice was attached.");
        var reader = ReaderOver(
            answerer,
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            AnswerAsync(reader, new AskMailRequest { QuestionText = "was the invoice attached" }));

        // Assert
        Assert.Equal(MailFathomPermission.MailAsk, refusal.RequiredPermission);
    }

    /// <summary>Nothing runs before the grant is read, so a caller that may not ask causes no call to a model provider.</summary>
    [Fact]
    public async Task AnswerQuestionAsync_ACallerGrantedNothing_ReachesNoProvider()
    {
        // Arrange
        var answerer = new RecordingMailQuestionAnswerer().Answering("The invoice was attached.");
        var reader = ReaderOver(answerer, authorization: AccessAuthorizations.ForCallerGranted());

        // Act
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            AnswerAsync(reader, new AskMailRequest { QuestionText = "was the invoice attached" }));

        // Assert
        Assert.Empty(answerer.Questions);
    }

    private static Task<AskMailResult> AnswerAsync(MailboxQuestionReader reader, AskMailRequest request) =>
        reader.AnswerQuestionAsync(request, TestContext.Current.CancellationToken);

    private static EmailKnowledgePassage PassageOf(
        int position,
        string text,
        SenderVerification? senderVerification = null,
        MachineAuthorshipAssessment? machineAuthorship = null) => new()
        {
            StoredEmailId = StoredEmailId.Create(EmailIdentityAt(position)),
            AccountId = MailAccountId.Create(ServedAccountId),
            FolderAlias = MailFolderAlias.Create("INBOX"),
            Subject = "Quarterly invoice",
            ReceivedAt = Now,
            SenderVerification = senderVerification ?? SenderVerification.NotEstablished,
            MachineAuthorship = machineAuthorship ?? MachineAuthorshipAssessment.NotAssessed,
            Text = text,
        };

    /// <summary>Names one email by its position, so the same run of a test always uses the same identifiers.</summary>
    private static Guid EmailIdentityAt(int position) => new($"00000000-0000-0000-0000-{position:D12}");

    private static MailboxQuestionReader ReaderOver(
        IMailQuestionAnswerer? answerer,
        AiProviderHealthState chatState = AiProviderHealthState.Serving,
        MailAnswerBounds? bounds = null,
        IMailAnsweringSpendLedger? spendLedger = null,
        IMailAnsweringRunTelemetry? runTelemetry = null,
        IMailAnsweringAuditTrail? auditTrail = null,
        SensitiveContentEgressGuard? egressGuard = null,
        AccessAuthorization? authorization = null)
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
            new MailboxScopeResolver(
                CatalogServing(MailAccountId.Create(ServedAccountId)),
                StubMailFolderParticipation.Nothing,
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            spendLedger ?? LedgerAdmitting(),
            bounds ?? MailAnswerBounds.Default,
            runTelemetry ?? new RecordingMailAnsweringRunTelemetry(),
            auditTrail ?? new RecordingMailAnsweringAuditTrail(),
            timeProvider,
            egressGuard ?? SensitiveContentEgressGuards.Inactive(),
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailAsk));
    }

    /// <summary>A ledger with an allowance for whatever a test asks it.</summary>
    /// <remarks>
    /// Configured explicitly rather than left at the substitute's default, which is <see langword="false" /> and would
    /// silently refuse every question in a suite about what a question produces.
    /// </remarks>
    private static IMailAnsweringSpendLedger LedgerAdmitting()
    {
        var ledger = Substitute.For<IMailAnsweringSpendLedger>();
        ledger.TryAdmitRun().Returns(true);

        return ledger;
    }

    private static IMailAccountCatalog CatalogServing(params MailAccountId[] servedAccountIds)
    {
        var catalog = Substitute.For<IMailAccountCatalog>();
        catalog.ServedAccounts.Returns([.. servedAccountIds.Select(SyntheticServedAccount.Of)]);

        return catalog;
    }
}
