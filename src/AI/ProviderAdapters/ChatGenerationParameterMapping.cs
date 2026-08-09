// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using OpenAI.Responses;

namespace MailFathom.AI.ProviderAdapters;

/// <summary>Turns the declared generation parameters into the options one request carries.</summary>
/// <remarks>
/// <para>
/// One mapping rather than one per caller, because the single-request adapter and the answering run send the same
/// parameters to the same endpoint and differ only in what each adds on top. Two copies would let a parameter reach one
/// path and not the other, and the deployment would see a setting that worked for a question and was ignored for a
/// judgement.
/// </para>
/// <para>
/// It is also what keeps every parameter optional in the way the declaration means it: an unwritten value produces no
/// member on the options object, so the request carries nothing rather than a default this side invented. Several
/// current models reject a sampling parameter or a reasoning effort outright, so sending one nobody asked for turns
/// every call a deployment makes into a rejected request.
/// </para>
/// </remarks>
internal static class ChatGenerationParameterMapping
{
    /// <summary>Builds the options one request carries from what the deployment declared.</summary>
    /// <param name="plan">The validated declaration.</param>
    /// <returns>The options, carrying only the parameters the declaration wrote.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="plan" /> is <see langword="null" />.</exception>
    public static ChatOptions ToChatOptions(ChatGenerationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var options = new ChatOptions
        {
            MaxOutputTokens = plan.MaximumOutputTokens,
            Temperature = plan.Temperature,
            TopP = plan.TopP,
        };

        if (plan.ReasoningEffort is { } effort)
        {
            options.RawRepresentationFactory = ReasoningEffortFactoryFor(plan.Endpoint.Api, effort);
        }

        return options;
    }

    /// <summary>Builds the per-request hook that states the declared effort in the form its API reads.</summary>
    /// <remarks>
    /// <para>
    /// The effort is stated through the client library's own request options rather than through the provider-neutral
    /// reasoning member, and that is what lets a deployment name a level this build has never heard of. The neutral
    /// member carries a closed set fixed when its package was compiled, so a level a model gains later — as <c>xhigh</c>
    /// was gained — would be unsendable until a release of MailFathom caught up. The library's own type is built from a
    /// string, so the value the operator wrote is the value that goes out.
    /// </para>
    /// <para>
    /// The two APIs frame it differently, which is why the factory is chosen per API rather than shared: chat
    /// completions carries a top-level member and the responses API carries a reasoning block. Everything else on the
    /// options is left alone — the abstraction fills the members it owns over whatever this returns, so the bounds, the
    /// sampling parameters, the instruction, and the tools all still reach the request.
    /// </para>
    /// </remarks>
    // Every reasoning member of both request-option types carries the evaluation-only marker in this release of the
    // client library, so the suppression covers the whole choice rather than one expression. It stays confined to this
    // method, and to the reasoning members alone — nothing else in the file inherits it.
#pragma warning disable OPENAI001
    private static Func<IChatClient, object?> ReasoningEffortFactoryFor(ChatProviderApi api, string effort) =>
        api switch
        {
            ChatProviderApi.Responses => _ => new CreateResponseOptions
            {
                ReasoningOptions = new ResponseReasoningOptions
                {
                    ReasoningEffortLevel = new ResponseReasoningEffortLevel(effort),
                },
            },
            _ => _ => new ChatCompletionOptions { ReasoningEffortLevel = new ChatReasoningEffortLevel(effort) },
        };
#pragma warning restore OPENAI001
}
