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
/// Configuration carries every value as text, so the JSON type an operator wrote is lost at the provider and has to be
/// read back before the request is built. These tests bind through the same strict binding the section is read with,
/// because what the binder keeps and what it refuses is the half of the claim no other test would notice changing.
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

    /// <summary>
    /// An array written as JSON rather than as JSON text flattens into keys the dictionary cannot hold, and the strict
    /// binding the section is read with refuses it rather than dropping the member without a word.
    /// </summary>
    [Fact]
    public void Bind_AnArrayWrittenAsJsonRatherThanAsText_IsRefused()
    {
        // Act, Assert
        Assert.Throws<InvalidOperationException>(() => BindModel("""{ "stop": ["###", "END"] }"""));
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
