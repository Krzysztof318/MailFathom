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
using MailFathom.Application.Retrieval.AskMail.Audit;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MailFathom.Mcp.UnitTests.TestDoubles;

/// <summary>Composes the answering half of a deployment, so a test states what it varies and nothing else.</summary>
/// <remarks>
/// The capability is a real one over stubbed providers rather than a substitute, because it is sealed on purpose: what
/// decides whether a question may run is one reading made in one place, and a test that replaced it would prove the
/// boundary composes with a fiction.
/// </remarks>
internal static class AnsweringDeployment
{
    /// <summary>The account every arrangement here serves.</summary>
    public const string ServedAccountId = "personal";

    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Composes the capability of a deployment configured as the arguments describe.</summary>
    /// <param name="answerer">The answering port, or <see langword="null" /> for a deployment that declared no chat endpoint.</param>
    /// <param name="chatState">What the last call to the chat endpoint established.</param>
    /// <returns>The capability the surface reads.</returns>
    public static MailAnsweringCapability Capability(
        IMailQuestionAnswerer? answerer,
        AiProviderHealthState chatState = AiProviderHealthState.Serving)
    {
        var healthReader = Substitute.For<IAiProviderHealthReader>();
        healthReader.Read(AiProviderRole.Embedding)
            .Returns(new AiProviderHealth(AiProviderRole.Embedding, AiProviderHealthState.Serving, Now));
        healthReader.Read(AiProviderRole.Chat)
            .Returns(new AiProviderHealth(AiProviderRole.Chat, chatState, Now));

        var timeProvider = new FakeTimeProvider(Now);

        return new MailAnsweringCapability(
            SemanticSearchOver(healthReader, timeProvider),
            healthReader,
            timeProvider,
            answerer);
    }

    /// <summary>Describes the one account this suite's deployment serves.</summary>
    /// <returns>The catalog the use case bounds its reads with and the tool publishes names from.</returns>
    public static IMailAccountCatalog AccountCatalog()
    {
        var accountCatalog = Substitute.For<IMailAccountCatalog>();
        accountCatalog.SynchronizationEnabled.Returns(true);
        accountCatalog.ServedAccounts.Returns([SyntheticServedAccount.Of(ServedAccountId)]);

        return accountCatalog;
    }

    /// <summary>Composes the use case the tool calls, over the capability the arguments describe.</summary>
    /// <param name="answerer">The answering port, or <see langword="null" /> for a deployment that declared no chat endpoint.</param>
    /// <param name="bounds">How much of one run's outcome a single answer publishes.</param>
    /// <param name="spendLedger">What the current period may still spend, or <see langword="null" /> for one that admits every question.</param>
    /// <returns>The use case.</returns>
    public static MailboxQuestionReader QuestionReader(
        IMailQuestionAnswerer? answerer,
        MailAnswerBounds? bounds = null,
        IMailAnsweringSpendLedger? spendLedger = null)
    {
        return new MailboxQuestionReader(
            Capability(answerer),
            new MailboxScopeResolver(AccountCatalog()),
            spendLedger ?? LedgerAdmitting(),
            bounds ?? MailAnswerBounds.Default,

            // Both are substituted because this suite is about what the tool converts and publishes: what a run reports
            // and what it records are the use case's own contract, asserted where that contract lives.
            Substitute.For<IMailAnsweringRunTelemetry>(),
            Substitute.For<IMailAnsweringAuditTrail>(),
            new FakeTimeProvider(Now));
    }

    /// <summary>Builds a ledger with an allowance for whatever a test asks it.</summary>
    /// <returns>The ledger.</returns>
    /// <remarks>
    /// Configured explicitly rather than left at the substitute's default, which is <see langword="false" /> and would
    /// silently refuse every question in a suite about what the tool publishes.
    /// </remarks>
    public static IMailAnsweringSpendLedger LedgerAdmitting()
    {
        var ledger = Substitute.For<IMailAnsweringSpendLedger>();
        ledger.TryAdmitRun().Returns(true);

        return ledger;
    }

    /// <summary>Builds an embedding half that has a profile, a generator agreeing with it, and a provider that answers.</summary>
    private static SemanticEmailSearch SemanticSearchOver(
        IAiProviderHealthReader healthReader,
        TimeProvider timeProvider)
    {
        var identity = EmbeddingProfileIdentity.Create(
            "a-provider",
            "a-model",
            modelVersion: null,
            dimension: 8,
            EmbeddingDistanceMetric.Cosine,
            EmbeddingInputPreparation.Create(2_000, passageInstruction: null, normalizesVector: true));

        var profileReader = Substitute.For<IActiveEmbeddingProfileReader>();
        profileReader.FindActiveProfileAsync(Arg.Any<CancellationToken>())
            .Returns(new RegisteredEmbeddingProfile(
                EmbeddingProfileId.Create(new Guid("0f9d6b0b-2f1e-4c2a-9a3d-7c8e5f4a1b20")),
                identity));

        var generator = Substitute.For<ITextEmbeddingGenerator>();
        generator.Identity.Returns(identity);

        return new SemanticEmailSearch(
            profileReader,
            Substitute.For<IEmailVectorSearchIndexReader>(),
            healthReader,
            timeProvider,
            generator);
    }
}
