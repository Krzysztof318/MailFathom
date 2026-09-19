// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using System.Text.Json;
using MailFathom.Host.Configuration.Chat;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Chat;

/// <summary>Covers what a model's <c>AdditionalProperties</c> object becomes on the way from a configuration file to a request.</summary>
/// <remarks>
/// Configuration carries a document as a tree of text leaves, so both halves of what an operator wrote — the structure
/// and the JSON type of each value — are lost at the provider and have to be read back before the request is built.
/// These tests bind through the same strict binding the section is read with, because what survives that binding is
/// the half of the claim no other test would notice changing.
/// </remarks>
public sealed class ChatModelAdditionalPropertiesBindingTests
{
    [Fact]
    public void ToPlan_ScalarsWrittenInTheFile_ReachThePlanAsTheJsonTheyWereWrittenAs()
    {
        // Arrange
        var model = BindModel("""{ "top_k": 40, "min_p": 0.05, "ignore_eos": true, "seed": null, "route": "fallback" }""");

        // Act
        var properties = model.ToPlan().AdditionalProperties;

        // Assert
        Assert.Equal(JsonValueKind.Number, properties["top_k"].ValueKind);
        Assert.Equal(40, properties["top_k"].GetInt32());
        Assert.Equal(0.05, properties["min_p"].GetDouble());
        Assert.Equal(JsonValueKind.True, properties["ignore_eos"].ValueKind);
        Assert.Equal("fallback", properties["route"].GetString());
        Assert.Equal(JsonValueKind.Null, properties["seed"].ValueKind);
    }

    /// <summary>An array, an object, or a string that would otherwise read as a number is written as its JSON text and read back as that JSON.</summary>
    [Fact]
    public void ToPlan_AnArrayOrObjectWrittenAsJsonText_ReachesThePlanAsThatJson()
    {
        // Arrange
        var model = BindModel("""{ "stop": "[\"###\", \"END\"]", "provider": "{ \"order\": [\"first\"] }", "route": "\"40\"" }""");

        // Act
        var properties = model.ToPlan().AdditionalProperties;

        // Assert
        Assert.Equal(["###", "END"], properties["stop"].EnumerateArray().Select(element => element.GetString()));
        Assert.Equal("first", properties["provider"].GetProperty("order")[0].GetString());
        Assert.Equal("40", properties["route"].GetString());
    }

    /// <summary>
    /// The JSON configuration provider hands a <c>null</c> literal over as a missing value rather than as empty text, so
    /// the null an operator wrote reaches the request as null — and empty text, which is a different declaration, stays
    /// an empty string.
    /// </summary>
    [Fact]
    public void ToPlan_ANullLiteralAndAnEmptyString_ArriveAsTheTwoDifferentValuesTheyAre()
    {
        // Arrange
        var model = BindModel("""{ "seed": null, "suffix": "" }""");

        // Act
        var properties = model.ToPlan().AdditionalProperties;

        // Assert
        Assert.Equal(JsonValueKind.Null, properties["seed"].ValueKind);
        Assert.Equal(string.Empty, properties["suffix"].GetString());
    }

    [Fact]
    public void ToPlan_AModelDeclaringNoAdditionalProperties_CarriesNone()
    {
        // Arrange
        var model = BindModel(additionalProperties: null);

        // Act
        var properties = model.ToPlan().AdditionalProperties;

        // Assert
        Assert.Empty(properties);
    }

    /// <summary>A gateway's routing block is written as ordinary nested JSON and reaches the request as that structure.</summary>
    [Fact]
    public void ToPlan_AnObjectWrittenAsNestedJson_ReachesThePlanAsThatObject()
    {
        // Arrange
        var model = BindModel(
            """{ "provider": { "order": ["anthropic", "openai"], "allow_fallbacks": false, "max_price": { "prompt": 1 } } }""");

        // Act
        var provider = model.ToPlan().AdditionalProperties["provider"];

        // Assert
        Assert.Equal(["anthropic", "openai"], provider.GetProperty("order").EnumerateArray().Select(element => element.GetString()));
        Assert.Equal(JsonValueKind.False, provider.GetProperty("allow_fallbacks").ValueKind);
        Assert.Equal(1, provider.GetProperty("max_price").GetProperty("prompt").GetInt32());
    }

    /// <summary>A node whose children are the integers below their own count is an array, which is how a configuration provider carries one.</summary>
    [Fact]
    public void ToPlan_AnArrayWrittenAsNestedJson_ReachesThePlanAsAnArrayInIndexOrder()
    {
        // Arrange
        var model = BindModel("""{ "transforms": ["middle-out", "compress", "trim"] }""");

        // Act
        var transforms = model.ToPlan().AdditionalProperties["transforms"];

        // Assert
        Assert.Equal(JsonValueKind.Array, transforms.ValueKind);
        Assert.Equal(["middle-out", "compress", "trim"], transforms.EnumerateArray().Select(element => element.GetString()));
    }

    /// <summary>
    /// An array of eleven elements is where index order and key order disagree, because a configuration provider hands
    /// its children over sorted as text and <c>10</c> sorts before <c>2</c>.
    /// </summary>
    [Fact]
    public void ToPlan_AnArrayLongerThanTenElements_KeepsTheOrderItWasWrittenIn()
    {
        // Arrange
        var written = Enumerable.Range(0, 11).Select(position => $"stop-{position}").ToArray();
        var model = BindModel($$"""{ "stop": {{JsonSerializer.Serialize(written)}} }""");

        // Act
        var stop = model.ToPlan().AdditionalProperties["stop"];

        // Assert
        Assert.Equal(written, stop.EnumerateArray().Select(element => element.GetString()));
    }

    /// <summary>
    /// A deployment configured through environment variables writes the same tree under <c>__</c>, so the two
    /// provisioning shapes reach one request member rather than one of them being a file-only affordance.
    /// </summary>
    [Fact]
    public void ToPlan_AMemberDeclaredThroughEnvironmentVariables_ReachesThePlanAsTheSameJson()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Chat:Models:0:Alias"] = "main",
                ["Chat:Models:0:Model"] = "a-chat-model",
                ["Chat:Models:0:Unauthenticated"] = "true",
                ["Chat:Models:0:AdditionalProperties:provider:order:0"] = "anthropic",
                ["Chat:Models:0:AdditionalProperties:provider:allow_fallbacks"] = "false",
            })
            .Build();

        var model = configuration.GetSection(ChatModelOptions.SectionName)
            .Get<ChatModelOptions>(binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)!
            .Models[0];

        // Act
        var provider = model.ToPlan().AdditionalProperties["provider"];

        // Assert
        Assert.Equal("anthropic", provider.GetProperty("order")[0].GetString());
        Assert.Equal(JsonValueKind.False, provider.GetProperty("allow_fallbacks").ValueKind);
    }

    /// <summary>An object with no members carries no children and no value, which is the same thing a null literal carries, so the reference states that it arrives as null.</summary>
    [Fact]
    public void ToPlan_AnEmptyObject_ArrivesAsNull()
    {
        // Arrange
        var model = BindModel("""{ "provider": {} }""");

        // Act
        var properties = model.ToPlan().AdditionalProperties;

        // Assert
        Assert.Equal(JsonValueKind.Null, properties["provider"].ValueKind);
    }

    private static ChatModelDeclarationOptions BindModel(string? additionalProperties)
    {
        var block = additionalProperties is null ? string.Empty : $""", "AdditionalProperties": {additionalProperties}""";
        var json = $$"""{ "Chat": { "Models": [ { "Alias": "main", "Model": "a-chat-model", "Unauthenticated": true{{block}} } ] } }""";

        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
            .Build();

        var section = configuration.GetSection(ChatModelOptions.SectionName)
            .Get<ChatModelOptions>(binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)!;

        return section.Models[0];
    }
}
