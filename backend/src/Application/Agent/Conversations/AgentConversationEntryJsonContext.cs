// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Citations;

namespace MailFathom.Application.Agent.Conversations;

/// <summary>The one shape a conversation's entry is written into its row as, with the readers and writers generated at compile time.</summary>
/// <remarks>
/// <para>
/// A conversation is durable, so the payload column outlives every process that wrote into it by years rather than by
/// minutes — and a rolling upgrade has one build writing it while another reads it. That makes it a contract rather
/// than an implementation detail, which is why it is declared here instead of being left to whatever options the store
/// happened to have: a property renamed on one side and not the other would be a block that reads back empty rather
/// than a compilation failure.
/// </para>
/// <para>
/// Source-generated in the same mode and with the same three converters the Discover run's journal uses, so a block or
/// a citation is written here exactly as it is written there. The mode is metadata because the block catalogue, the
/// citation targets, and the entries themselves are all polymorphic and the fast path carries no discriminator.
/// </para>
/// <para>
/// It is not what a client is handed. The routes serialize the entries they read with the surface's own options, so
/// this shape is between the conversation and its rows and changing it changes no wire form.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [
        typeof(AgentMessageIdJsonConverter),
        typeof(StoredEmailIdJsonConverter),
        typeof(EmailChunkIdJsonConverter),
        typeof(EmailAddressJsonConverter),
    ])]
[JsonSerializable(typeof(AgentConversationEntry))]
public sealed partial class AgentConversationEntryJsonContext : JsonSerializerContext;
