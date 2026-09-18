// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.AI.Chat;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Providers;
using Microsoft.Extensions.AI;

namespace MailFathom.Evaluations.Judging;

/// <summary>The one model every scenario is judged by, declared apart from the models under test.</summary>
/// <remarks>
/// <para>
/// Apart, because a model grading its own answers measures nothing. Pinned, because a verdict is only comparable with
/// another from the same judge: changing it invalidates every earlier result, so it is declared once, through
/// <c>vars.JUDGE_PROVIDER_ADDRESS</c>, <c>vars.JUDGE_PROVIDER_MODEL</c>, and <c>secrets.JUDGE_PROVIDER_API_KEY</c>,
/// and left alone.
/// </para>
/// <para>
/// None of the three may appear in the store, the report, a log line, or a failure message. The client this opens is
/// therefore wrapped in <see cref="AnonymousJudgeChatClient" />, and the key its cached verdicts are filed under is
/// <see cref="CachingKey" /> rather than the model's name.
/// </para>
/// </remarks>
internal sealed class JudgeDeclaration
{
    private const string AddressVariable = "MAILFATHOM_JUDGE_ADDRESS";
    private const string ModelVariable = "MAILFATHOM_JUDGE_MODEL";
    private const string ApiKeyVariable = "MAILFATHOM_JUDGE_API_KEY";

    private readonly ChatEndpoint endpoint;
    private readonly string apiKey;

    private JudgeDeclaration(ChatEndpoint endpoint, string apiKey)
    {
        this.endpoint = endpoint;
        this.apiKey = apiKey;
    }

    /// <summary>Gets the key the judge's cached verdicts are filed under.</summary>
    /// <remarks>
    /// Keyed by the judge's own credential over its address and model, so a changed judge files its verdicts apart from
    /// the previous one's and none is reused across the change, while the key reveals neither: a plain hash of a model
    /// name is reversed by guessing the name. The cost is that rotating the key re-asks every verdict once.
    /// </remarks>
    public string CachingKey => Convert.ToHexString(HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(this.apiKey),
        Encoding.UTF8.GetBytes($"{this.endpoint.Address}\n{this.endpoint.RoutedModelName}")));

    /// <summary>Reads the declaration a requested run was given.</summary>
    /// <returns>The declaration.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable, when the run was asked for without the model or the key.</exception>
    public static JudgeDeclaration Read()
    {
        var model = AiEvaluationRun.Required(ModelVariable);
        var apiKey = AiEvaluationRun.Required(ApiKeyVariable);
        var address = AiEvaluationRun.Optional(AddressVariable);

        return Of(address is null ? null : new Uri(address, UriKind.Absolute), model, apiKey);
    }

    /// <summary>Declares a judge from its three values.</summary>
    /// <param name="address">Where the judge is reached, or <see langword="null" /> for the provider's own address.</param>
    /// <param name="model">The model that judges.</param>
    /// <param name="apiKey">The key the judge's provider authenticates with.</param>
    /// <returns>The declaration.</returns>
    public static JudgeDeclaration Of(Uri? address, string model, string apiKey) =>
        new(
            new ChatEndpoint(
                AnonymousJudgeChatClient.Name,
                address,
                model,
                ChatProviderApi.ChatCompletions,
                PublishedModelName: string.Empty),
            apiKey);

    /// <summary>Opens the judge, already anonymous.</summary>
    /// <param name="spend">What every verdict's tokens and charge are recorded against.</param>
    /// <returns>The client, which the caller disposes.</returns>
    public IChatClient Open(SpendMeter spend)
    {
        ProviderChatClient? judge = null;

        try
        {
            judge = ProviderChatClient.Open(this.endpoint, this.apiKey, TimeSpan.FromMinutes(2), spend);
            var anonymous = new AnonymousJudgeChatClient(judge);
            judge = null;

            return anonymous;
        }
        finally
        {
            judge?.Dispose();
        }
    }
}
