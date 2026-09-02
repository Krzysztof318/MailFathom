// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Application.EmailContent.Rendering.Document;

/// <summary>One opaque colour a message asked for, as three channels.</summary>
/// <remarks>
/// <para>
/// A colour rather than a CSS colour value: whatever notation the message wrote — a keyword, three or six hexadecimal
/// digits, or an <c>rgb()</c> function — is resolved during the reduction and what crosses the wire is three numbers.
/// So a client parses nothing, and a notation nobody implemented cannot arrive as text a renderer would have to decide
/// about.
/// </para>
/// <para>
/// Opaque deliberately. Alpha would let a message make its own text invisible against the pane it is drawn in, which is
/// a legibility decision the pane keeps rather than one a sender takes, and a translucent background would compose with
/// whichever theme the reader is in rather than with the one the sender assumed.
/// </para>
/// </remarks>
/// <param name="Red">The red channel.</param>
/// <param name="Green">The green channel.</param>
/// <param name="Blue">The blue channel.</param>
[JsonConverter(typeof(MailDocumentColourJsonConverter))]
public readonly record struct MailDocumentColour(byte Red, byte Green, byte Blue)
{
    /// <summary>Gets the colour in the <c>#rrggbb</c> notation the wire uses.</summary>
    public string Notation => string.Create(
        CultureInfo.InvariantCulture,
        $"#{this.Red:x2}{this.Green:x2}{this.Blue:x2}");

    /// <summary>Parses the <c>#rrggbb</c> notation the wire uses.</summary>
    /// <param name="notation">The notation to parse.</param>
    /// <param name="colour">The colour when the notation is one this contract writes; otherwise the default.</param>
    /// <returns><see langword="true" /> when the notation was read; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// Only the one notation this contract writes is accepted, rather than every notation CSS admits. What a message
    /// wrote was already resolved when the document was reduced, so anything else arriving here is a document this
    /// deployment did not produce.
    /// </remarks>
    public static bool TryParse(string? notation, out MailDocumentColour colour)
    {
        colour = default;

        if (notation is not { Length: 7 } || notation[0] != '#')
        {
            return false;
        }

        var digits = notation.AsSpan(1);

        // AllowHexSpecifier alone, because HexNumber also admits surrounding whitespace: "#  ff 00" would otherwise
        // parse as a colour, and this notation is one the contract publishes rather than one a person types.
        if (!byte.TryParse(digits[..2], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var red)
            || !byte.TryParse(digits[2..4], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var green)
            || !byte.TryParse(digits[4..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var blue))
        {
            return false;
        }

        colour = new MailDocumentColour(red, green, blue);

        return true;
    }

    /// <inheritdoc />
    public override string ToString() => this.Notation;
}

/// <summary>Serializes <see cref="MailDocumentColour" /> as the <c>#rrggbb</c> notation.</summary>
/// <remarks>
/// One string rather than three numbered members, because a colour is read by a person looking at a response as often
/// as by a client, and three fields would be three chances for a client to compose them in the wrong order.
/// </remarks>
public sealed class MailDocumentColourJsonConverter : JsonConverter<MailDocumentColour>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string in the notation this contract writes.</exception>
    public override MailDocumentColour Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"A colour must be a JSON string, but the token was {reader.TokenType}.");
        }

        return MailDocumentColour.TryParse(reader.GetString(), out var colour)
            ? colour
            : throw new JsonException("A colour is written as #rrggbb.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, MailDocumentColour value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(value.Notation);
    }
}
