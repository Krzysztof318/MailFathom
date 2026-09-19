// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Chat;
using MailFathom.Infrastructure.Secrets.Discovery;
using Microsoft.Extensions.Configuration;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Builds the chat declarations the configuration tests read, so each test states only what it varies.</summary>
/// <remarks>
/// The section became an array of models and a set of references into it, so the shape every test needs — one declared
/// model, reached by a key, named as the one that answers — is three nested objects rather than a handful of properties.
/// Building it here is what keeps each test's arrangement to the one thing it is about.
/// </remarks>
internal static class DeclaredChatModels
{
    /// <summary>Builds one model declaration.</summary>
    public static ChatModelDeclarationOptions Model(
        string alias = "answering",
        string model = "a-chat-model",
        string address = "https://provider.invalid/v1/",
        bool authenticated = true) =>
        new()
        {
            Alias = alias,
            Model = model,
            Address = address,
            ApiKey = authenticated ? new ConfiguredSecret { SecretReference = "env:CHAT_KEY" } : null,
            Unauthenticated = !authenticated,
        };

    /// <summary>Builds the configuration subtree a model's additional request members are declared as.</summary>
    /// <param name="members">Each member, by the configuration key it sits under — <c>provider:order:0</c> for a nested one — and the text configuration carries it as.</param>
    /// <returns>The subtree, ready to be assigned to a declaration.</returns>
    /// <remarks>
    /// The property is the subtree itself rather than a bound value, so a test that varies one member states it the
    /// way a configuration provider would rather than reaching past the binding it is exercising.
    /// </remarks>
    public static IConfigurationSection AdditionalProperties(params (string Key, string? Value)[] members) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(members.Select(member =>
                new KeyValuePair<string, string?>($"{nameof(AdditionalProperties)}:{member.Key}", member.Value)))
            .Build()
            .GetSection(nameof(AdditionalProperties));

    /// <summary>Builds a section declaring the models given, with the first of them answering.</summary>
    /// <remarks>The main model is left unwritten where exactly one is declared, which is the shape the section resolves for itself and therefore the one worth exercising by default.</remarks>
    public static ChatModelOptions Section(params ChatModelDeclarationOptions[] models)
    {
        var settings = new ChatModelOptions();

        foreach (var model in models.Length > 0 ? models : [Model()])
        {
            settings.Models.Add(model);
        }

        if (settings.Models.Count > 1)
        {
            settings.MainModel.Alias = settings.Models[0].Alias;
        }

        return settings;
    }

    /// <summary>Builds a section declaring one model, with a second standing behind it as the fallback.</summary>
    public static ChatModelOptions SectionWithFallback(
        string alias = "answering",
        string fallbackAlias = "standby")
    {
        var settings = Section(Model(alias), Model(fallbackAlias, model: "a-standby-model"));

        settings.MainModel.Alias = alias;
        settings.MainModel.Fallback = fallbackAlias;

        return settings;
    }
}
