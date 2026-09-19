// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.AI.Search;

/// <summary>Reads a field as the text a model wrote, and as nothing at all where it wrote something else there.</summary>
/// <remarks>
/// A field declared as one string is a field a model answers as an array of them: a sentence naming two senders is
/// answered with both addresses often enough to be the ordinary case rather than a rare one. Without this the document
/// fails to deserialize and the whole answer is lost — the criteria, the other filters, and the part the model said it
/// made nothing of, none of which the shape of one field says anything about. So a value that is not text costs that
/// field and nothing else, and <see cref="MailSearchPhraseDocumentReading" /> decides what the text that survives means.
/// </remarks>
internal sealed class WrittenTextJsonConverter : JsonConverter<string?>
{
    /// <inheritdoc />
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.String)
        {
            return reader.GetString();
        }

        // Past the whole value rather than only its first token, so an array or an object leaves the reader where the
        // next field begins instead of inside the one that was dropped.
        reader.Skip();

        return null;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
