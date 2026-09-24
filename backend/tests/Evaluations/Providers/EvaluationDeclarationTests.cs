// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using Xunit;

namespace MailFathom.Evaluations.Providers;

public sealed class EvaluationDeclarationTests
{
    private static readonly Uri Address = new("https://gateway.example.test/v1/");

    [Fact]
    public void Parse_NoBlock_MeasuresEveryAgentOnTheFallbackModel()
    {
        // Act
        var declaration = EvaluationDeclaration.Parse(block: null, fallbackMainModel: "main-model", Address);

        // Assert
        Assert.All(
            Enum.GetValues<ChatCapability>(),
            capability => Assert.Equal("main-model", Assert.Single(declaration.ModelsFor(capability)).Name));
        Assert.Equal(1, declaration.Repetitions);
        Assert.Empty(declaration.EmbeddingModels);
        Assert.Null(declaration.EmbeddingDimension);
    }

    [Fact]
    public void Parse_NoMainModelAndNoFallback_FailsNamingBothVariables()
    {
        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => EvaluationDeclaration.Parse("{Repetitions: 2}", fallbackMainModel: null, Address));

        // Assert
        Assert.Contains(EvaluationDeclaration.Variable, failure.Message, StringComparison.Ordinal);
        Assert.Contains(EvaluationDeclaration.MainModelVariable, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelsFor_AnAgentGivenTwoModels_RunsThatAgentOnBothAndEveryOtherOnTheMainModel()
    {
        // Arrange
        var declaration = EvaluationDeclaration.Parse("{MainModel: model-a, ImageDescription: [model-b, model-c]}", "fallback", Address);

        // Act
        var describing = declaration.ModelsFor(ChatCapability.ImageDescription);
        var answering = declaration.ModelsFor(ChatCapability.MailAnswering);

        // Assert
        Assert.Equal(["model-b", "model-c"], describing.Select(static model => model.Name));
        Assert.Equal("model-a", Assert.Single(answering).Name);
    }

    [Fact]
    public void ModelsFor_AModelDeclaredAsAMapping_BuildsThePlanADeploymentDeclaringItWould()
    {
        // Arrange
        const string Block =
            "{MainModel: {Model: vendor/model, Alias: careful, Api: Responses, ReasoningEffort: high, Temperature: 0.5, TopP: 0.9, "
            + "ExtraHeaders: [{Name: X-Title, Value: MailFathom}], AdditionalProperties: {provider: {order: [first, second]}, top_k: 40}}}";

        // Act
        var model = Assert.Single(EvaluationDeclaration.Parse(Block, fallbackMainModel: null, Address).ModelsFor(ChatCapability.Agent));

        // Assert
        Assert.Equal("careful", model.Name);
        Assert.Equal("vendor/model", model.Plan.Endpoint.RoutedModelName);
        Assert.Equal(Address, model.Plan.Endpoint.Address);
        Assert.Equal(ChatProviderApi.Responses, model.Plan.Endpoint.Api);
        Assert.Equal("high", model.Plan.ReasoningEffort);
        Assert.Equal(0.5f, model.Plan.Temperature);
        Assert.Equal(0.9f, model.Plan.TopP);
        Assert.Equal("""{"order":["first","second"]}""", model.Plan.AdditionalProperties["provider"].GetRawText());
        Assert.Equal("40", model.Plan.AdditionalProperties["top_k"].GetRawText());

        var header = Assert.Single(model.ExtraHeaders);
        Assert.Equal(("X-Title", "MailFathom"), (header.Name, header.Value));
    }

    [Theory]
    [InlineData("{MainModel: model-a, Repeat: 2}", "Repeat")]
    [InlineData("{MainModel: {Model: model-a, Thinking: high}}", "Thinking")]
    public void Parse_AKeyTheRunDoesNotRead_FailsNamingIt(string block, string key)
    {
        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => EvaluationDeclaration.Parse(block, fallbackMainModel: null, Address));

        // Assert
        Assert.Contains(key, failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{MainModel: {Model: model-a, ReasoningEffort: very high}}")]
    [InlineData("{MainModel: {Model: model-a, Temperature: 3}}")]
    [InlineData("{MainModel: {Model: model-a, AdditionalProperties: {temperature: 1}}}")]
    [InlineData("{MainModel: {Alias: nameless}}")]
    [InlineData("{MainModel: {Model: model-a, ExtraHeaders: [{Value: orphan}]}}")]
    [InlineData("{MainModel: model-a, Repetitions: 21}")]
    [InlineData("{MainModel: model-a, EmbeddingDimension: wide}")]
    [InlineData("MainModel: [model-a")]
    public void Parse_AValueADeploymentOrTheRunRefuses_FailsNamingTheVariable(string block)
    {
        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => EvaluationDeclaration.Parse(block, fallbackMainModel: null, Address));

        // Assert
        Assert.Contains(EvaluationDeclaration.Variable, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_TwoModelsOfOneAgentFiledUnderOneName_FailsAskingForAnAlias()
    {
        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => EvaluationDeclaration.Parse(
            "{MainModel: model-a, ImageDescription: [model-b, {Model: model-b, ReasoningEffort: high}]}",
            fallbackMainModel: null,
            Address));

        // Assert
        Assert.Contains("Alias", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ModelsFor_TwoModelsOfOneAgentToldApartByAnAlias_MeasuresBoth()
    {
        // Arrange
        var declaration = EvaluationDeclaration.Parse(
            "{MainModel: model-a, ImageDescription: [model-b, {Model: model-b, Alias: model-b-high, ReasoningEffort: high}]}",
            fallbackMainModel: null,
            Address);

        // Act
        var describing = declaration.ModelsFor(ChatCapability.ImageDescription);

        // Assert
        Assert.Equal(["model-b", "model-b-high"], describing.Select(static model => model.Name));
    }

    [Fact]
    public void Parse_RepetitionsAndEmbeddingModels_ReadsThemAsDeclared()
    {
        // Act
        var declaration = EvaluationDeclaration.Parse(
            "{MainModel: model-a, Repetitions: 3, EmbeddingModels: [embed-a, embed-b], EmbeddingDimension: 256, Ref: main}",
            fallbackMainModel: null,
            Address);

        // Assert
        Assert.Equal(3, declaration.Repetitions);
        Assert.Equal(["embed-a", "embed-b"], declaration.EmbeddingModels);
        Assert.Equal(256, declaration.EmbeddingDimension);
    }

    [Fact]
    public void PairingsFor_NeitherAgentNamed_PairsEachMainModelWithItselfOnly()
    {
        // Arrange
        var declaration = EvaluationDeclaration.Parse("{MainModel: [model-a, model-b]}", fallbackMainModel: null, Address);

        // Act
        var pairings = declaration.PairingsFor(ChatCapability.DiscoveryPlanning, ChatCapability.DiscoveryComposition);

        // Assert
        Assert.Equal(
            [("model-a", "model-a"), ("model-b", "model-b")],
            pairings.Select(static pairing => (pairing.First.Name, pairing.Second.Name)));
    }

    [Fact]
    public void PairingsFor_OneAgentGivenTwoModels_PairsEachOfThemWithTheOtherAgentsModel()
    {
        // Arrange
        var declaration = EvaluationDeclaration.Parse(
            "{MainModel: model-a, DiscoveryPlanning: [model-b, model-c]}",
            fallbackMainModel: null,
            Address);

        // Act
        var pairings = declaration.PairingsFor(ChatCapability.DiscoveryPlanning, ChatCapability.DiscoveryComposition);

        // Assert
        Assert.Equal(
            [("model-b", "model-a"), ("model-c", "model-a")],
            pairings.Select(static pairing => (pairing.First.Name, pairing.Second.Name)));
    }
}
