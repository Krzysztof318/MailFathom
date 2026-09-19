// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using MailFathom.AI.Chat;
using MailFathom.AI.ProviderAdapters;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Providers;
using Microsoft.Extensions.AI;

namespace MailFathom.Evaluations.Judging;

/// <summary>The one model every scenario is judged by, declared apart from the models under test.</summary>
/// <remarks>
/// <para>
/// Apart, because a model grading its own answers measures nothing. Pinned, because a verdict is only comparable with
/// another from the same judge: changing it invalidates every earlier result, so it is declared once, through
/// <c>vars.JUDGE_PROVIDER_MODEL</c>, and left alone. That model and how hard it reasons are the whole of the declaration —
/// the endpoint and the key are the run's own, read from <see cref="EvaluationEndpoint" /> exactly as the models under
/// test read them.
/// </para>
/// <para>
/// The reasoning effort is declared here, where the models under test deliberately declare none: they are measured as a
/// deployment calls them, while the judge is the run's own instrument, and what a verdict costs and whether one comes
/// back at all should not follow whichever default the judge's provider chose. Unset, nothing is sent and that default
/// applies.
/// </para>
/// <para>
/// Neither the model nor the effort may appear in the store, the report, a log line, or a failure message. The client this opens is
/// therefore wrapped in <see cref="AnonymousJudgeChatClient" />, and the key its cached verdicts are filed under is
/// <see cref="CachingKey" /> rather than the model's name.
/// </para>
/// </remarks>
internal sealed class JudgeDeclaration
{
    private const string ModelVariable = "MAILFATHOM_JUDGE_MODEL";
    private const string ReasoningEffortVariable = "MAILFATHOM_JUDGE_REASONING_EFFORT";

    private readonly ChatEndpoint endpoint;
    private readonly string apiKey;
    private readonly string? reasoningEffort;

    private JudgeDeclaration(ChatEndpoint endpoint, string apiKey, string? reasoningEffort)
    {
        this.endpoint = endpoint;
        this.apiKey = apiKey;
        this.reasoningEffort = reasoningEffort;
    }

    /// <summary>Gets the key the judge's cached verdicts are filed under.</summary>
    /// <remarks>
    /// <para>
    /// Keyed by the judge's own credential over its address, its model, and its reasoning effort, so a changed judge files
    /// its verdicts apart from the previous one's and none is reused across the change, while the key reveals none of
    /// them: a plain hash of a model name is reversed by guessing the name. The cost is that rotating the key re-asks
    /// every verdict once.
    /// </para>
    /// <para>
    /// The effort joins the key only where one is declared, so a judge that declares none keeps the verdicts it cached
    /// before the effort could be declared at all.
    /// </para>
    /// </remarks>
    public string CachingKey => Convert.ToHexString(HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(this.apiKey),
        Encoding.UTF8.GetBytes(this.reasoningEffort is null
            ? $"{this.endpoint.Address}\n{this.endpoint.RoutedModelName}"
            : $"{this.endpoint.Address}\n{this.endpoint.RoutedModelName}\n{this.reasoningEffort}")));

    /// <summary>Reads the declaration a requested run was given.</summary>
    /// <returns>The declaration.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable, when the run was asked for without the model or the key, or with an effort no provider could read as a level.</exception>
    public static JudgeDeclaration Read() =>
        Of(
            EvaluationEndpoint.Address(),
            AiEvaluationRun.Required(ModelVariable),
            EvaluationEndpoint.ApiKey(),
            AiEvaluationRun.Optional(ReasoningEffortVariable));

    /// <summary>Declares a judge from its values.</summary>
    /// <param name="address">Where the judge is reached, or <see langword="null" /> for the provider's own address.</param>
    /// <param name="model">The model that judges.</param>
    /// <param name="apiKey">The key the judge's provider authenticates with.</param>
    /// <param name="reasoningEffort">How hard the judge reasons, or <see langword="null" /> to send nothing and leave the provider's default.</param>
    /// <returns>The declaration.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable and never the value, when the effort is not shaped like a level.</exception>
    public static JudgeDeclaration Of(Uri? address, string model, string apiKey, string? reasoningEffort)
    {
        if (reasoningEffort is not null && !ChatGenerationPlan.IsUsableReasoningEffort(reasoningEffort))
        {
            throw new InvalidOperationException(
                $"{ReasoningEffortVariable} is not a single word a provider could read as a reasoning level.");
        }

        return new(
            new ChatEndpoint(
                AnonymousJudgeChatClient.Name,
                address,
                model,
                ChatProviderApi.ChatCompletions,
                PublishedModelName: string.Empty),
            apiKey,
            reasoningEffort);
    }

    /// <summary>Opens the judge, already anonymous.</summary>
    /// <param name="spend">What every verdict's tokens and charge are recorded against.</param>
    /// <returns>The client, which the caller disposes.</returns>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Present takes ownership of the provider's client, and the finally block disposes it only when Present never returned; the analyzer does not follow ownership through a method call.")]
    public IChatClient Open(SpendMeter spend)
    {
        ProviderChatClient? judge = null;

        try
        {
            judge = ProviderChatClient.Open(this.endpoint, this.apiKey, TimeSpan.FromMinutes(2), spend);
            var presented = this.Present(judge);
            judge = null;

            return presented;
        }
        finally
        {
            judge?.Dispose();
        }
    }

    /// <summary>Puts the declaration onto the judge's own client: its reasoning effort on every call, and its anonymity.</summary>
    /// <param name="judge">The judge's own client, which the result takes ownership of.</param>
    /// <returns>The client every verdict is asked through.</returns>
    /// <remarks>
    /// The effort is stated beneath the evaluators rather than by them, because they compose each request's options
    /// themselves and none of them takes one. It is stated through the mapping a deployment's own requests go through,
    /// so the judge's effort reaches the wire exactly as a deployment's would.
    /// </remarks>
    public IChatClient Present(IChatClient judge)
    {
        var requestOptions = ChatGenerationParameterMapping.RequestOptionsFactoryFor(this.endpoint.Api, this.reasoningEffort);

        var pipeline = judge.AsBuilder().Use(static inner => new AnonymousJudgeChatClient(inner));

        return requestOptions is null
            ? pipeline.Build()
            : pipeline.ConfigureOptions(options => options.RawRepresentationFactory = requestOptions).Build();
    }
}
