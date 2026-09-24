// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using MailFathom.AI.Chat;
using MailFathom.AI.Providers;
using MailFathom.Evaluations.Reporting;
using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.Configuration.Provisioning;
using Microsoft.Extensions.Configuration;

namespace MailFathom.Evaluations.Providers;

/// <summary>The run a dispatch declares in one YAML block: the models per agent, how often each case is asked, and the embedding models.</summary>
/// <remarks>
/// <para>
/// One block rather than a variable per setting, because a workflow dispatch carries few inputs and each is one line, so
/// a new setting is a key here rather than a new input. Flow style keeps it on that one line:
/// <c>{MainModel: {Model: openai/gpt-5.6-luna, ReasoningEffort: medium}, ImageDescription: [model-a, model-b]}</c>.
/// </para>
/// <para>
/// A model is routed the way a deployment routes it: <c>MainModel</c> is the fallback, and each <see cref="ChatCapability" />
/// may name its own under the key its <c>Chat</c> section uses. A role takes one model or a list, so a list under
/// <c>ImageDescription</c> runs the image evaluations once per model and leaves every other evaluation on the main model.
/// A model is its routed name, or a mapping carrying the keys a <c>Chat:Models</c> declaration carries for what a
/// request asks for — <c>Model</c>, <c>Alias</c>, <c>Api</c>, <c>ReasoningEffort</c>, <c>Temperature</c>, <c>TopP</c>,
/// <c>ExtraHeaders</c>, and <c>AdditionalProperties</c> — and each is built into a plan through
/// <see cref="ChatModelDeclarationOptions" />, so a value is refused here as a deployment refuses it.
/// </para>
/// <para>
/// A header's value is written in the block rather than referenced as a secret, because the block is a dispatch input
/// that anyone reading the run can see. Nothing that must stay private belongs in it; the key stays in
/// <see cref="EvaluationEndpoint" />.
/// </para>
/// <para>
/// The block is read by the host's own YAML configuration reader, so it refuses what a provisioned configuration file
/// refuses, and every key it does not know fails the run naming the key rather than being ignored as a typo would be.
/// </para>
/// </remarks>
internal sealed class EvaluationDeclaration
{
    /// <summary>The variable carrying the block.</summary>
    public const string Variable = "MAILFATHOM_EVALUATION";

    /// <summary>The variable carrying the model measured as the main one where the block names none.</summary>
    /// <remarks>The model the provider-contract tests reach, so a repository configured for them can run this as it stands.</remarks>
    public const string MainModelVariable = "MAILFATHOM_CHAT_MODEL";

    private const string MainModelKey = "MainModel";
    private const string RepetitionsKey = "Repetitions";
    private const string EmbeddingModelsKey = "EmbeddingModels";
    private const string EmbeddingDimensionKey = "EmbeddingDimension";

    /// <summary>The commit the workflow evaluates, which it reads before this suite exists and which this suite ignores.</summary>
    private const string RefKey = "Ref";

    private static readonly HashSet<string> RunKeys = new(
        [RefKey, MainModelKey, RepetitionsKey, EmbeddingModelsKey, EmbeddingDimensionKey, .. Enum.GetNames<ChatCapability>()],
        StringComparer.OrdinalIgnoreCase);

    private readonly IReadOnlyList<ModelUnderTest> mainModels;
    private readonly Dictionary<ChatCapability, IReadOnlyList<ModelUnderTest>> capabilityModels;

    private EvaluationDeclaration(
        IReadOnlyList<ModelUnderTest> mainModels,
        Dictionary<ChatCapability, IReadOnlyList<ModelUnderTest>> capabilityModels,
        int repetitions,
        IReadOnlyList<string> embeddingModels,
        int? embeddingDimension)
    {
        this.mainModels = mainModels;
        this.capabilityModels = capabilityModels;
        this.Repetitions = repetitions;
        this.EmbeddingModels = embeddingModels;
        this.EmbeddingDimension = embeddingDimension;
    }

    /// <summary>Gets how many times each case is asked of each model, which is one where the block declares none.</summary>
    public int Repetitions { get; }

    /// <summary>Gets the embedding models the block names, empty where it names none.</summary>
    public IReadOnlyList<string> EmbeddingModels { get; }

    /// <summary>Gets the width every embedding is cut to, or <see langword="null" /> where each model keeps its own.</summary>
    public int? EmbeddingDimension { get; }

    /// <summary>Reads the run the environment declares.</summary>
    /// <returns>The declaration.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable and the key, when the block cannot be read or declares nothing to measure.</exception>
    public static EvaluationDeclaration Read() =>
        Parse(AiEvaluationRun.Optional(Variable), AiEvaluationRun.Optional(MainModelVariable), EvaluationEndpoint.Address());

    /// <summary>Reads a declared block.</summary>
    /// <param name="block">The block, or <see langword="null" /> where the run declared none.</param>
    /// <param name="fallbackMainModel">The model measured as the main one where the block names none, or <see langword="null" />.</param>
    /// <param name="address">The endpoint every model sits behind, or <see langword="null" /> for the provider's own default.</param>
    /// <returns>The declaration.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the variable and the key, when the block cannot be read or declares nothing to measure.</exception>
    public static EvaluationDeclaration Parse(string? block, string? fallbackMainModel, Uri? address)
    {
        var run = ReadBlock(block);

        if (run.GetChildren().Select(static key => key.Key).FirstOrDefault(key => !RunKeys.Contains(key)) is { } unknown)
        {
            throw new InvalidOperationException(
                $"{Variable} declares '{unknown}', which is not a key the run reads. It reads {string.Join(", ", RunKeys.Order(StringComparer.Ordinal))}.");
        }

        var mainModels = ModelsOf(run.GetSection(MainModelKey), address);

        if (mainModels.Count is 0)
        {
            mainModels = fallbackMainModel is { } model
                ? [Declare(new DeclaredModel { Model = model }, MainModelVariable, address)]
                : throw new InvalidOperationException(
                    $"{Variable} names no {MainModelKey} and {MainModelVariable} is not set, so there is no model to measure.");
        }

        var capabilityModels = Enum.GetValues<ChatCapability>()
            .Select(capability => (Capability: capability, Models: ModelsOf(run.GetSection(capability.ToString()), address)))
            .Where(static declared => declared.Models.Count > 0)
            .ToDictionary(static declared => declared.Capability, static declared => (IReadOnlyList<ModelUnderTest>)declared.Models);

        return new EvaluationDeclaration(
            mainModels,
            capabilityModels,
            EvaluationRepetitions.Parse(run[RepetitionsKey]),
            [.. NamesOf(run.GetSection(EmbeddingModelsKey))],
            EmbeddingModelsUnderTest.ParseDimension(run[EmbeddingDimensionKey]));
    }

    /// <summary>Names the models the evaluations of one agent run on.</summary>
    /// <param name="capability">The agent.</param>
    /// <returns>The models the block names for it, or the main models where it names none.</returns>
    public IReadOnlyList<ModelUnderTest> ModelsFor(ChatCapability capability) =>
        this.capabilityModels.GetValueOrDefault(capability) ?? this.mainModels;

    /// <summary>Pairs the models of two agents one evaluation joins, as a deployment would route each of them.</summary>
    /// <param name="first">The agent asked first.</param>
    /// <param name="second">The agent asked second.</param>
    /// <returns>
    /// Every pairing, once each: for each main model, every model of the first agent with every model of the second, where
    /// an agent the block names no model for runs on that main model — so two main models and no override are two runs,
    /// each on one model, rather than four.
    /// </returns>
    public IReadOnlyList<(ModelUnderTest First, ModelUnderTest Second)> PairingsFor(ChatCapability first, ChatCapability second) =>
    [
        .. this.mainModels
            .SelectMany(main => this.ModelsOr(first, main).SelectMany(firstModel => this.ModelsOr(second, main).Select(secondModel => (firstModel, secondModel))))
            .DistinctBy(static pairing => (pairing.firstModel.Name, pairing.secondModel.Name)),
    ];

    private static IConfiguration ReadBlock(string? block)
    {
        if (string.IsNullOrWhiteSpace(block))
        {
            return new ConfigurationBuilder().Build();
        }

        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(block));

            return new ConfigurationBuilder().AddInMemoryCollection(YamlConfigurationDocument.Flatten(stream)).Build();
        }
        catch (FormatException failure)
        {
            throw new InvalidOperationException($"{Variable} is not a YAML mapping the run can read. {failure.Message}", failure);
        }
    }

    private static List<ModelUnderTest> ModelsOf(IConfigurationSection role, Uri? address)
    {
        List<ModelUnderTest> models = [.. ElementsOf(role).Select(element => Declare(Bound(element), element.Path, address))];

        if (models.GroupBy(static model => model.Name, StringComparer.Ordinal).FirstOrDefault(static named => named.Count() > 1) is { } repeated)
        {
            throw new InvalidOperationException(
                $"{Variable} names two models under '{role.Path}' whose results would both be filed as '{repeated.Key}'. Give one an Alias.");
        }

        return models;
    }

    /// <summary>Reads a role as the models it lists: nothing where it is absent, one where it is a name or a mapping, each element where it is a list.</summary>
    /// <remarks>The configuration a block flattens to writes a list as keys numbered from zero, which no key of a model's mapping is.</remarks>
    private static IConfigurationSection[] ElementsOf(IConfigurationSection role)
    {
        var children = role.GetChildren().ToArray();

        if (children.Length > 0 && children.All(static child => int.TryParse(child.Key, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            return children;
        }

        return role.Exists() ? [role] : [];
    }

    private static IEnumerable<string> NamesOf(IConfigurationSection names) =>
        ElementsOf(names).Select(static name => name.Value is { Length: > 0 } value
            ? value.Trim()
            : throw new InvalidOperationException($"{Variable} declares '{name.Path}' as something other than a model's name."));

    private static DeclaredModel Bound(IConfigurationSection element)
    {
        if (element.Value is { } name)
        {
            return new DeclaredModel { Model = name };
        }

        var declared = new DeclaredModel();

        try
        {
            element.Bind(declared, static options => options.ErrorOnUnknownConfiguration = true);
        }
        catch (InvalidOperationException failure)
        {
            throw new InvalidOperationException($"{Variable} declares the model at '{element.Path}' in a shape the run cannot read. {failure.Message}", failure);
        }

        return declared;
    }

    private static ModelUnderTest Declare(DeclaredModel declared, string path, Uri? address)
    {
        var model = declared.Model.Trim();

        if (model.Length is 0)
        {
            throw new InvalidOperationException($"{Variable} declares a model at '{path}' with no Model to route it to.");
        }

        if (declared.ExtraHeaders.Any(static header => string.IsNullOrWhiteSpace(header.Name)))
        {
            throw new InvalidOperationException($"{Variable} declares a header with no Name under '{path}'.");
        }

        var options = new ChatModelDeclarationOptions
        {
            Alias = declared.Alias?.Trim() is { Length: > 0 } alias ? alias : model,
            Model = model,
            Address = address?.AbsoluteUri ?? string.Empty,
            Api = declared.Api,
            ReasoningEffort = declared.ReasoningEffort,
            Temperature = declared.Temperature,
            TopP = declared.TopP,
            AdditionalProperties = declared.AdditionalProperties,
        };

        try
        {
            return new ModelUnderTest(
                options.ToPlan(),
                [.. declared.ExtraHeaders.Select(static header => new ProviderEndpointHeader(header.Name.Trim(), header.Value))]);
        }
        catch (ArgumentException failure)
        {
            throw new InvalidOperationException($"{Variable} declares a model at '{path}' that a deployment would refuse. {failure.Message}", failure);
        }
    }

    private IEnumerable<ModelUnderTest> ModelsOr(ChatCapability capability, ModelUnderTest main) =>
        this.capabilityModels.GetValueOrDefault(capability) ?? [main];

    /// <summary>What one model's mapping may carry, which is bound strictly so a mistyped key fails the run.</summary>
    private sealed class DeclaredModel
    {
        public string Model { get; set; } = string.Empty;

        public string? Alias { get; set; }

        public ChatProviderApi Api { get; set; } = ChatProviderApi.ChatCompletions;

        public string? ReasoningEffort { get; set; }

        public float? Temperature { get; set; }

        public float? TopP { get; set; }

        public IList<DeclaredHeader> ExtraHeaders { get; } = [];

        public IConfigurationSection? AdditionalProperties { get; set; }
    }

    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The configuration binder materializes each header a model's mapping lists.")]
    private sealed class DeclaredHeader
    {
        public string Name { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;
    }
}
