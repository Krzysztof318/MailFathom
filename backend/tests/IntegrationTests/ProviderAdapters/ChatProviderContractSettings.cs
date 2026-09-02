// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;

namespace MailFathom.IntegrationTests.ProviderAdapters;

/// <summary>Reads which chat endpoint a provider-contract run calls, and refuses a run that was asked for without one.</summary>
/// <remarks>
/// The endpoint alone, for the reason <see cref="EmbeddingProviderContractSettings" /> holds only its own: whether the
/// run happens is <see cref="AiProviderContractRun" />'s question. There is no dimension and no profile identity here,
/// which is the difference a chat endpoint carries rather than a setting this forgot — an answer is produced,
/// presented, and gone, so nothing downstream asks which model wrote it.
/// </remarks>
internal static class ChatProviderContractSettings
{
    /// <summary>The output budget one contract answer may occupy.</summary>
    /// <remarks>
    /// Below the deployment default, because what these tests prove is the protocol, the authentication, the failure
    /// classification, and the shape of the answer, and none of the four needs a long answer — it bounds what a
    /// requested run costs. Not lower than this, though: a model that reasons before it answers spends this budget on
    /// reasoning first, and a ceiling tight enough to be reached before any text is produced would fail the run with an
    /// empty answer rather than prove anything about the adapter.
    /// </remarks>
    private const int MaximumOutputTokens = 512;

    private const string ApiKeyVariable = "MAILFATHOM_CHAT_API_KEY";
    private const string AddressVariable = "MAILFATHOM_CHAT_ADDRESS";
    private const string ModelVariable = "MAILFATHOM_CHAT_MODEL";
    private const string ReasoningEffortVariable = "MAILFATHOM_CHAT_REASONING_EFFORT";

    /// <summary>Builds the plan a contract run calls the provider with, over one of the two request APIs.</summary>
    /// <param name="api">Which of the provider's two request APIs the plan reaches it through.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the run was asked for without everything it needs.</exception>
    /// <remarks>
    /// The API is a parameter rather than a variable a run sets, because both are covered rather than one chosen: each
    /// is a distinct wire protocol against the same endpoint, and a surface nobody called is a surface whose first
    /// failure reaches an operator instead of this suite. The declared model has to serve both, which the first-party
    /// and cloud endpoints do for a request carrying no tools — and a contract request carries none.
    /// </remarks>
    public static ChatGenerationPlan Plan(ChatProviderApi api)
    {
        var model = AiProviderContractRun.Required(ModelVariable);
        var address = Environment.GetEnvironmentVariable(AddressVariable);

        var endpoint = new ChatEndpoint(
            "contract",
            address is { Length: > 0 } ? new Uri(address, UriKind.Absolute) : null,
            model,
            api);

        // Neither sampling parameter is sent, because several current models reject one outright and a contract run
        // exists to learn what the provider does with a request MailFathom actually makes, not to learn that a
        // parameter this suite chose is refused. The reasoning effort follows the same rule and is therefore unset
        // unless the run names one: a model that does not reason refuses the parameter, and a reasoning model that
        // requires it is exactly what a run naming one is pointed at.
        return ChatGenerationPlan.Create(
            endpoint,
            MaximumOutputTokens,
            temperature: null,
            topP: null,
            Environment.GetEnvironmentVariable(ReasoningEffortVariable) is { Length: > 0 } effort ? effort : null,
            maximumMessagesPerRequest: 8,
            maximumRequestCharacters: 8000,
            requestTimeout: TimeSpan.FromSeconds(60));
    }

    /// <summary>Reads the provider key a contract run authenticates with.</summary>
    /// <returns>The key.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the run was asked for without one.</exception>
    public static string ApiKey() => AiProviderContractRun.Required(ApiKeyVariable);
}
