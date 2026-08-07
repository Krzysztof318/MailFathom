// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Embeddings;
using MailFathom.Application.Emails.Embeddings;

namespace MailFathom.IntegrationTests.Embeddings;

/// <summary>Reads what a provider-contract run was given, and refuses a run that was asked for without a credential.</summary>
/// <remarks>
/// <para>
/// Embedding is the first thing MailFathom does that costs money per unit of mail, so a paid call is never the default
/// — in the running service or in verification. These settings are absent on a developer's machine and on an ordinary
/// pipeline run, which is what makes the tests reading them skip and cost nothing.
/// </para>
/// <para>
/// Asking for the run without a credential fails rather than skips, and that asymmetry is the point: a run somebody
/// explicitly requested and which then quietly proved nothing is worse than one that never started.
/// </para>
/// </remarks>
internal static class EmbeddingProviderContractSettings
{
    /// <summary>The variable that turns the provider-contract tests on. Nothing sets it by default.</summary>
    public const string EnablingVariable = "MAILFATHOM_EMBEDDING_CONTRACT_TESTS";

    /// <summary>The reason a run nobody asked for reports against each skipped test.</summary>
    /// <remarks>
    /// xUnit requires this message beside a conditional skip and reads it before it reads the condition, so a test
    /// carrying <c>SkipUnless</c> without <c>Skip</c> fails on every run rather than skipping on the ones that asked
    /// for nothing.
    /// </remarks>
    public const string SkipReason =
        $"A provider-contract run calls a real provider and spends credit, so it happens only when {EnablingVariable} is set to true.";

    private const string ApiKeyVariable = "MAILFATHOM_EMBEDDING_API_KEY";
    private const string AddressVariable = "MAILFATHOM_EMBEDDING_ADDRESS";
    private const string ModelVariable = "MAILFATHOM_EMBEDDING_MODEL";
    private const string RoutedModelVariable = "MAILFATHOM_EMBEDDING_ROUTED_MODEL";
    private const string DimensionVariable = "MAILFATHOM_EMBEDDING_DIMENSION";

    /// <summary>Gets whether a run was explicitly asked for.</summary>
    /// <remarks>
    /// Read as a property rather than at class initialization, because xUnit evaluates the skip condition per test and
    /// a static read at load time would fix the answer before the runner had set anything.
    /// </remarks>
    public static bool ProviderContractTestsRequested =>
        Environment.GetEnvironmentVariable(EnablingVariable) is { Length: > 0 } requested
        && bool.TryParse(requested, out var enabled)
        && enabled;

    /// <summary>Builds the plan a contract run calls the provider with.</summary>
    /// <returns>The plan.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the run was asked for without everything it needs.</exception>
    public static EmbeddingGenerationPlan Plan()
    {
        var model = Required(ModelVariable);
        var dimension = int.Parse(Required(DimensionVariable), System.Globalization.CultureInfo.InvariantCulture);
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
    public static string ApiKey() => Required(ApiKeyVariable);

    private static string Required(string variableName) =>
        Environment.GetEnvironmentVariable(variableName) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"The provider-contract tests were turned on through {EnablingVariable} and {variableName} is not set. "
                + "A run that was asked for and then proved nothing is worse than one that never started, so this fails "
                + "rather than skipping.");
}
