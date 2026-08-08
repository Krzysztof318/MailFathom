// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Embeddings;
using MailFathom.Application.Emails.Embeddings;

namespace MailFathom.IntegrationTests.ProviderAdapters;

/// <summary>Reads which embedding endpoint a provider-contract run calls, and refuses a run that was asked for without one.</summary>
/// <remarks>
/// The endpoint alone. Whether the run happens at all is <see cref="AiProviderContractRun" />'s question, and it is the
/// same question for every provider this suite reaches; what is here is what only an embedding call needs — the model,
/// the width it answers in, and where to send the request.
/// </remarks>
internal static class EmbeddingProviderContractSettings
{
    private const string ApiKeyVariable = "MAILFATHOM_EMBEDDING_API_KEY";
    private const string AddressVariable = "MAILFATHOM_EMBEDDING_ADDRESS";
    private const string ModelVariable = "MAILFATHOM_EMBEDDING_MODEL";
    private const string RoutedModelVariable = "MAILFATHOM_EMBEDDING_ROUTED_MODEL";
    private const string DimensionVariable = "MAILFATHOM_EMBEDDING_DIMENSION";

    /// <summary>Builds the plan a contract run calls the provider with.</summary>
    /// <returns>The plan.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the run was asked for without everything it needs.</exception>
    public static EmbeddingGenerationPlan Plan()
    {
        var model = AiProviderContractRun.Required(ModelVariable);
        var dimension = int.Parse(
            AiProviderContractRun.Required(DimensionVariable),
            System.Globalization.CultureInfo.InvariantCulture);
        var address = Environment.GetEnvironmentVariable(AddressVariable);
        var routedModel = Environment.GetEnvironmentVariable(RoutedModelVariable);

        var endpoint = new EmbeddingEndpoint(
            "contract",
            EmbeddingProfileIdentity.Create(
                "contract-provider",
                model,
                modelVersion: null,
                dimension,
                EmbeddingDistanceMetric.Cosine,
                EmbeddingInputPreparation.Create(8000, passageInstruction: null, normalizesVector: true)),
            address is { Length: > 0 } ? new Uri(address, UriKind.Absolute) : null,
            routedModel is { Length: > 0 } ? routedModel : model,
            SupportsRequestedDimension: true);

        return EmbeddingGenerationPlan.Create([endpoint], false, 8, TimeSpan.FromSeconds(30));
    }

    /// <summary>Reads the provider key a contract run authenticates with.</summary>
    /// <returns>The key.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the run was asked for without one.</exception>
    public static string ApiKey() => AiProviderContractRun.Required(ApiKeyVariable);
}
