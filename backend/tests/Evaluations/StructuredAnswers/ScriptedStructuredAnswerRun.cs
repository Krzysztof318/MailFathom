// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Enrichment;
using MailFathom.Evaluations.Judging;
using MailFathom.Evaluations.Reporting;
using Microsoft.Extensions.AI;
using Xunit;

namespace MailFathom.Evaluations.StructuredAnswers;

/// <summary>Runs a structured-answer case against a scripted model and a scripted judge, into a real store of its own.</summary>
/// <remarks>
/// What every free test of a structured-answer scenario needs, and nothing it asserts: the store is written to a
/// temporary directory and read back for the reason <see cref="EmailEnrichmentScenarioTests" /> gives, since the verdict a
/// case reports is what the store files.
/// </remarks>
internal sealed class ScriptedStructuredAnswerRun : IDisposable
{
    /// <summary>The routed name every scripted model answers under.</summary>
    public const string ModelUnderTest = "model-under-test";

    /// <summary>A verdict in the shape the intent resolution evaluator reads, rating the answer five.</summary>
    private const string JudgeAnswer =
        """{"explanation":"A reasonable reading.","conversation_has_intent":true,"agent_perceived_intent":"find mail","actual_user_intent":"find mail","correct_intent_detected":true,"intent_resolved":true,"resolution_score":5}""";

    private static readonly Uri JudgeAddress = new("https://judge.invalid/v1/");

    private readonly DirectoryInfo store = Directory.CreateTempSubdirectory("mailfathom-evaluations-");

    /// <summary>Gets the scripted judge, which counts what it was asked.</summary>
    public ScriptedChatClient Judge { get; } =
        new(JudgeAnswer, new ChatClientMetadata("scripted-judge", JudgeAddress, "judge-model"));

    /// <summary>Opens a scripted model answering every call with one text.</summary>
    /// <param name="answer">What the model answers.</param>
    /// <returns>The model.</returns>
    public static ScriptedChatClient Model(string answer) =>
        new(answer, new ChatClientMetadata("scripted", defaultModelId: ModelUnderTest));

    /// <summary>Runs one case under a scripted model and files it in this run's store.</summary>
    /// <param name="request">The case.</param>
    /// <param name="model">The model under test.</param>
    /// <returns>What the answer was held to.</returns>
    public async Task<StructuredAnswer> RunAsync(StructuredAnswerRequest request, IChatClient model)
    {
        var declaration = JudgeDeclaration.Of(JudgeAddress, "judge-model", "judge-key", reasoningEffort: null);
        using var anonymousJudge = new AnonymousJudgeChatClient(this.Judge);
        var reporting = EvaluationStore.OpenAt(
            this.store.FullName,
            "only",
            anonymousJudge,
            declaration.CachingKey,
            request.Evaluators);

        return await StructuredAnswerScenario.RunAsync(
            reporting,
            model,
            PlanFor(ModelUnderTest),
            repetition: 1,
            request,
            new SpendMeter(),
            new SpendMeter(),
            TestContext.Current.CancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.Judge.Dispose();
        this.store.Delete(recursive: true);
    }

    /// <summary>Plans a scripted model the way a declared one is planned, under the routed name it answers as.</summary>
    /// <param name="model">The routed name.</param>
    /// <returns>The plan.</returns>
    public static ChatGenerationPlan PlanFor(string model) =>
        ChatGenerationPlan.Create(
            new ChatEndpoint("evaluation", Address: null, model, ChatProviderApi.ChatCompletions, PublishedModelName: string.Empty),
            maximumOutputTokens: 1024,
            temperature: null,
            topP: null,
            reasoningEffort: null,
            maximumMessagesPerRequest: 8,
            maximumRequestCharacters: 64_000,
            maximumRequestImageOctets: 1024,
            requestTimeout: TimeSpan.FromSeconds(30));
}
