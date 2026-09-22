// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Evaluations.Providers;

/// <summary>Reads the embedding models a run measures, the one endpoint they sit behind, and the width their vectors are cut to.</summary>
/// <remarks>
/// <para>
/// An endpoint of its own rather than the chat one, under the names the provider-contract tests already read —
/// <c>MAILFATHOM_EMBEDDING_ADDRESS</c> and <c>MAILFATHOM_EMBEDDING_API_KEY</c> — because a deployment declares its
/// embedding provider apart from its chat provider and the two are often different vendors.
/// </para>
/// <para>
/// The width is one for the whole run rather than one per model, because what it answers is what a narrower space costs,
/// and that question is asked of every model at the same width. A run that declares none measures each model at its own.
/// </para>
/// </remarks>
internal static class EmbeddingModelsUnderTest
{
    /// <summary>The variable carrying the models, separated by commas, whitespace, or line breaks.</summary>
    public const string ModelsVariable = "MAILFATHOM_EVALUATION_EMBEDDING_MODELS";

    /// <summary>The variable carrying the width every vector is cut to, where the run asks for one.</summary>
    public const string DimensionVariable = "MAILFATHOM_EVALUATION_EMBEDDING_DIMENSION";

    /// <summary>The variable carrying the address every embedding request goes to.</summary>
    public const string AddressVariable = "MAILFATHOM_EMBEDDING_ADDRESS";

    /// <summary>The variable carrying the key every embedding request authenticates with.</summary>
    public const string ApiKeyVariable = "MAILFATHOM_EMBEDDING_API_KEY";

    /// <summary>Reads one model per declared name.</summary>
    /// <returns>The models, in the order the run declared them.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable, when the run declared no model or a width that is not one.</exception>
    public static IReadOnlyList<EmbeddingModelUnderTest> Declared()
    {
        var models = AiEvaluationRun.Required(ModelsVariable)
            .Split([',', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (models.Length is 0)
        {
            throw new InvalidOperationException($"{ModelsVariable} names no model to measure.");
        }

        var address = AiEvaluationRun.Optional(AddressVariable) is { } declared ? new Uri(declared, UriKind.Absolute) : null;
        var dimension = ParseDimension(AiEvaluationRun.Optional(DimensionVariable));

        return [.. models.Distinct(StringComparer.Ordinal).Select(model => new EmbeddingModelUnderTest(model, address, dimension))];
    }

    /// <summary>Reads the key every embedding request is authenticated with.</summary>
    /// <returns>The key.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable, when the run was asked for without one.</exception>
    public static string ApiKey() => AiEvaluationRun.Required(ApiKeyVariable);

    /// <summary>Reads a declared width.</summary>
    /// <param name="declared">The declaration, or <see langword="null" /> where the run made none.</param>
    /// <returns>The width, or <see langword="null" /> where the run keeps each model's own.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable, when the declaration is not a positive whole number.</exception>
    public static int? ParseDimension(string? declared)
    {
        if (declared is null)
        {
            return null;
        }

        return int.TryParse(declared.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var dimension) && dimension > 0
            ? dimension
            : throw new InvalidOperationException($"{DimensionVariable} must be a positive whole number.");
    }
}
