// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
/// <para>
/// One thing on a request is decided here rather than declared: what the provider may keep of what it was sent. That
/// follows the API a call is conducted through rather than anything an operator wrote, which is why it lives beside the
/// parameters instead of in the configuration they come from.
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

        options.RawRepresentationFactory = RequestOptionsFactoryFor(plan.Endpoint.Api, plan.ReasoningEffort);

        return options;
    }

    /// <summary>Builds the per-request hook its API needs, or none where the API needs nothing.</summary>
    /// <remarks>
    /// <para>
    /// The hook is where this deployment states what the client library's own request options carry, and the two APIs
    /// need it for different reasons. The responses API always needs one, because storage at the provider is decided
    /// per request and its default is to store; chat completions needs one only when an effort was declared, because
    /// its default is already what this deployment wants and a member nobody asked for is what a model rejects a whole
    /// request over.
    /// </para>
    /// <para>
    /// Everything else on the options is left alone — the abstraction fills the members it owns over whatever this
    /// returns, so the bounds, the sampling parameters, the instruction, and the tools all still reach the request.
    /// </para>
    /// </remarks>
    private static Func<IChatClient, object?>? RequestOptionsFactoryFor(ChatProviderApi api, string? effort)
    {
        if (api is ChatProviderApi.Responses)
        {
            return _ => StatelessResponseOptions(effort);
        }

        return effort is null ? null : _ => ChatCompletionOptionsFor(effort);
    }

    /// <summary>Builds the responses request that leaves the provider holding nothing once it has answered.</summary>
    /// <remarks>
    /// <para>
    /// A request here carries the question, the instruction, and the mail passages retrieval selected for it, and the
    /// responses API stores the whole of that for thirty days unless the request refuses — where it is readable in the
    /// provider's own console by anyone holding the account. So the refusal is stated on every call this API conducts
    /// rather than offered as a setting: a deployment that wanted a copy of its own correspondence kept by a third
    /// party is not a shape this system has. Chat completions defaults the other way and is left alone.
    /// </para>
    /// <para>
    /// Refusing the store is what makes the run stateless, and that has a second half. A reasoning model returns its
    /// reasoning as encrypted content the caller hands back on the next turn, and the provider emits that content only
    /// where the request asked for it. The answering run is a tool loop, so without it every turn after the first would
    /// begin without what the model worked out over the mail it had already read. Asking for it costs nothing on a
    /// model that does not reason, which returns none.
    /// </para>
    /// <para>
    /// The effort is stated here rather than through the provider-neutral reasoning member, and that is what lets a
    /// deployment name a level this build has never heard of. The neutral member carries a closed set fixed when its
    /// package was compiled, so a level a model gains later — as <c>xhigh</c> was gained — would be unsendable until a
    /// release of MailFathom caught up. The library's own type is built from a string, so the value the operator wrote
    /// is the value that goes out.
    /// </para>
    /// </remarks>
    // The whole responses request-options surface carries the evaluation-only marker in this release of the client
    // library, as does the chat completions reasoning member, so the suppression covers the two methods that build a
    // request and nothing else in the file inherits it.
#pragma warning disable OPENAI001
    private static CreateResponseOptions StatelessResponseOptions(string? effort)
    {
        var options = new CreateResponseOptions { StoredOutputEnabled = false };

        options.IncludedProperties.Add(IncludedResponseProperty.ReasoningEncryptedContent);

        if (effort is not null)
        {
            options.ReasoningOptions = new ResponseReasoningOptions
            {
                ReasoningEffortLevel = new ResponseReasoningEffortLevel(effort),
            };
        }

        return options;
    }

    /// <summary>Builds the chat completions request, which carries the declared effort and nothing else.</summary>
    private static ChatCompletionOptions ChatCompletionOptionsFor(string effort) =>
        new() { ReasoningEffortLevel = new ChatReasoningEffortLevel(effort) };
#pragma warning restore OPENAI001
}
