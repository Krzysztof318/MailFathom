// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Discovery.Presentation.Citations;

/// <summary>Serializes <see cref="StoredEmailId" /> as the bare UUID the client API already names an email by.</summary>
/// <remarks>
/// The converter is declared here and applied where the citation contract uses the identity, rather than on the domain
/// type itself. A stored-email identifier is not a wire concept — it is a local identity that happens to be what this
/// contract cites — and putting a serialization decision on the domain type would settle it for every other boundary
/// that ever meets one. Without it the identifier would be written as the object its property list describes, which is
/// neither what the client API publishes today nor anything a reader would recognize.
/// </remarks>
public sealed class StoredEmailIdJsonConverter : JsonConverter<StoredEmailId>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string, is not a UUID, or is the empty UUID.</exception>
    public override StoredEmailId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"An email identifier must be a JSON string, but the token was {reader.TokenType}.");
        }

        if (!reader.TryGetGuid(out var value) || value == Guid.Empty)
        {
            throw new JsonException("The value does not name a stored email.");
        }

        return StoredEmailId.Create(value);
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        StoredEmailId value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(value.Value);
    }
}
