// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;
using MailFathom.Application.Emails.Chunking;

namespace MailFathom.Application.Discovery.Presentation.Citations;

/// <summary>Serializes <see cref="EmailChunkId" /> as the bare UUID one persisted passage is stored under.</summary>
/// <remarks>
/// Declared beside the citation contract rather than on the identity, for the reason
/// <see cref="StoredEmailIdJsonConverter" /> gives: which boundaries publish a passage identifier is not something a
/// chunking type should decide on their behalf.
/// </remarks>
public sealed class EmailChunkIdJsonConverter : JsonConverter<EmailChunkId>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string, is not a UUID, or is the empty UUID.</exception>
    public override EmailChunkId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"A fragment identifier must be a JSON string, but the token was {reader.TokenType}.");
        }

        if (!reader.TryGetGuid(out var value) || value == Guid.Empty)
        {
            throw new JsonException("The value does not name a stored fragment.");
        }

        return EmailChunkId.Create(value);
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        EmailChunkId value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(value.Value);
    }
}
