// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Application.Agent.Conversations;

/// <summary>Serializes <see cref="AgentMessageId" /> as the bare UUID a turn is addressed by.</summary>
/// <remarks>
/// Every entry of a conversation names the turn it belongs to, so this identifier is in every stored payload. Without a
/// converter it would be written as the object its property list describes and read back as nothing at all — the struct
/// has no settable member for the serializer to fill, so the unmapped token is dropped in silence and every entry
/// returns naming the empty turn. That is not a failure anything would report: the entries read back as their own kinds
/// and only the fold notices, when the second turn of a conversation collides with the first on one empty identity.
/// </remarks>
public sealed class AgentMessageIdJsonConverter : JsonConverter<AgentMessageId>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string, is not a UUID, or is the empty UUID.</exception>
    public override AgentMessageId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"A message identifier must be a JSON string, but the token was {reader.TokenType}.");
        }

        if (!reader.TryGetGuid(out var value) || value == Guid.Empty)
        {
            throw new JsonException("The value does not name a message.");
        }

        return AgentMessageId.Create(value);
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        AgentMessageId value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(value.Value);
    }
}
