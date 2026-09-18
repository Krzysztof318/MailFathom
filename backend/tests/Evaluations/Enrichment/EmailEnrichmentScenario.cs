// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Enrichment;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Emails.Enrichment;
using MailFathom.Domain.Accounts;
using MailFathom.Evaluations.Corpus;
using MailFathom.Evaluations.Costing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailFathom.Evaluations.Enrichment;

/// <summary>Puts one corpus message to the enrichment agent under one model, reads its marks, and has the judge grade them.</summary>
/// <remarks>
/// <para>
/// The agent is composed by the composition a deployment uses, with the instruction, the turn, and the reading a
/// deployment uses, so a verdict here is about the prompt and the model rather than about a copy of either. What it
/// leaves out is what decides whether a derivation happens rather than what it says: the spend ledger, the egress
/// guard, and the fallback chain.
/// </para>
/// <para>
/// The model's answer passes through the run's response cache, filed under the model's name, so measuring a model
/// again over an unchanged prompt costs nothing — the same guarantee the judge's verdicts have. Spend is read from the
/// transport beneath that cache, so a run served from it reports nothing spent rather than yesterday's charge.
/// </para>
/// <para>
/// Marks are a structure, so whether there are any and what they cite is asserted plainly by the caller. The judge
/// grades only what a structure cannot: whether each sentence is supported by the message it is about.
/// </para>
/// </remarks>
internal static class EmailEnrichmentScenario
{
    /// <summary>The name the scenario is filed and reported under.</summary>
    public const string Name = "EmailEnrichment.InvoiceFollowUp";

    /// <summary>What every run of this scenario is judged on.</summary>
    public static IReadOnlyList<IEvaluator> Evaluators => [new GroundednessEvaluator()];

    /// <summary>
    /// A reply settling an export defect and chasing an outstanding invoice: it carries a subject, a request, amounts,
    /// and a date, which is every reading the agent may write.
    /// </summary>
    private static CorpusMessage Message { get; } = CorpusMessage.At(position: 3);

    /// <summary>Runs the scenario under one model and files the verdict in the run's store.</summary>
    /// <param name="reporting">The run's store, judge, and name.</param>
    /// <param name="model">The model under test's client.</param>
    /// <param name="plan">The plan the model is measured with, whose routed name is what the result is filed under.</param>
    /// <param name="modelSpend">What reaching that model has cost, which is the meter its client is opened over.</param>
    /// <param name="judgeSpend">What reaching the judge has cost, which is the meter the run's judge is opened over.</param>
    /// <param name="cancellationToken">Withdraws the run.</param>
    /// <returns>The marks the agent's answer produced and the judge's verdict on them.</returns>
    public static async Task<EmailEnrichmentOutcome> RunAsync(
        ReportingConfiguration reporting,
        IChatClient model,
        ChatGenerationPlan plan,
        SpendMeter modelSpend,
        SpendMeter judgeSpend,
        CancellationToken cancellationToken)
    {
        var modelName = plan.Endpoint.RoutedModelName;
        var iterationName = IterationNameFor(modelName);

        await using var scenarioRun = await reporting.CreateScenarioRunAsync(
            Name,
            iterationName,
            cancellationToken: cancellationToken);

        var turn = EmailEnrichmentInstructions.ComposeEnrichmentTurn(
            Message.Subject,
            Message.ReceivedAt,
            [.. Message.Passages.Select(static passage => passage.Text)]);

        using var cachedModel = await CacheOverAsync(reporting, model, plan, iterationName, cancellationToken);

        var response = await AskAsync(cachedModel, plan, turn, cancellationToken);
        var marks = EmailEnrichmentReading.Read(response, Message.Passages, EmailEnrichmentAgentComposition.AgentName);

        var readings = new ChatResponse(new ChatMessage(ChatRole.Assistant, Describe(marks))) { ModelId = modelName };
        var verdict = await scenarioRun.EvaluateAsync(
            [new ChatMessage(ChatRole.User, turn)],
            readings,
            [new GroundednessEvaluatorContext(Message.GroundingText)],
            cancellationToken);

        EvaluationCost.Record(verdict, modelName, modelSpend.Take(), judgeSpend.Take());

        return new EmailEnrichmentOutcome(modelName, marks, verdict);
    }

    /// <summary>Puts the run's response cache in front of the model under test, filed under the model and its address.</summary>
    /// <remarks>
    /// Opened for the whole run rather than for the one call, because the cache takes ownership of the client it wraps
    /// and disposing it mid-run would leave the caller's client closed while the run still holds it.
    /// </remarks>
    private static async Task<IChatClient> CacheOverAsync(
        ReportingConfiguration reporting,
        IChatClient model,
        ChatGenerationPlan plan,
        string iterationName,
        CancellationToken cancellationToken)
    {
        var cache = await reporting.ResponseCacheProvider!.GetCacheAsync(Name, iterationName, cancellationToken);

        return new DistributedCachingChatClient(model, cache)
        {
            CacheKeyAdditionalValues = [plan.Endpoint.RoutedModelName, plan.Endpoint.Address?.AbsoluteUri ?? string.Empty],
        };
    }

    /// <summary>Asks the model under test the scenario's turn, through the agent's own composition.</summary>
    private static async Task<string?> AskAsync(
        IChatClient model,
        ChatGenerationPlan plan,
        string turn,
        CancellationToken cancellationToken)
    {
        var agent = EmailEnrichmentAgentComposition.Compose(
            model,
            plan,
            MailAccountLanguage.English,
            new EmptyAgentInstructionEnvelope(),
            NullLoggerFactory.Instance);

        var answer = await agent.RunAsync(turn, session: null, options: null, cancellationToken);

        return answer.Text;
    }

    /// <summary>Writes the marks as the text the judge grades, one reading per line under its aspect.</summary>
    private static string Describe(IReadOnlyList<EmailEnrichmentMark> marks) =>
        string.Join('\n', marks.Select(static mark => $"{mark.Aspect}: {mark.Text}"));

    /// <summary>Turns a model's name into a name the store can file a result under.</summary>
    /// <remarks>
    /// The store files an iteration as a directory, and a routed name such as one naming its vendor carries a separator;
    /// the name stays readable as the model's with that character replaced.
    /// </remarks>
    private static string IterationNameFor(string modelName) =>
        string.Concat(modelName.Select(static character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
}
