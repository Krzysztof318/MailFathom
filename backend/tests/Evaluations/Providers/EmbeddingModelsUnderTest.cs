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
    /// <summary>The variable carrying the model measured where the run's block names no <c>EmbeddingModels</c>.</summary>
    /// <remarks>The model the provider-contract tests reach, so a repository configured for them can run this as it stands.</remarks>
    public const string ModelVariable = "MAILFATHOM_EMBEDDING_MODEL";

    /// <summary>The variable carrying the address every embedding request goes to.</summary>
    public const string AddressVariable = "MAILFATHOM_EMBEDDING_ADDRESS";

    /// <summary>The variable carrying the key every embedding request authenticates with.</summary>
    public const string ApiKeyVariable = "MAILFATHOM_EMBEDDING_API_KEY";

    /// <summary>Reads one model per declared name.</summary>
    /// <returns>The models, in the order the run declared them.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable, when the run declared no model or a width that is not one.</exception>
    public static IReadOnlyList<EmbeddingModelUnderTest> Declared()
    {
        var declaration = EvaluationDeclaration.Read();
        IReadOnlyList<string> models = declaration.EmbeddingModels.Count > 0
            ? declaration.EmbeddingModels
            : [AiEvaluationRun.Required(ModelVariable)];

        var address = AiEvaluationRun.Optional(AddressVariable) is { } declared ? new Uri(declared, UriKind.Absolute) : null;

        return [.. models.Distinct(StringComparer.Ordinal).Select(model => new EmbeddingModelUnderTest(model, address, declaration.EmbeddingDimension))];
    }

    /// <summary>Reads the key every embedding request is authenticated with.</summary>
    /// <returns>The key.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable, when the run was asked for without one.</exception>
    public static string ApiKey() => AiEvaluationRun.Required(ApiKeyVariable);

    /// <summary>Reads a declared width.</summary>
    /// <param name="declared">The <c>EmbeddingDimension</c> the run's block declares, or <see langword="null" /> where it declares none.</param>
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
            : throw new InvalidOperationException($"{EvaluationDeclaration.Variable} declares an EmbeddingDimension that is not a positive whole number.");
    }
}
