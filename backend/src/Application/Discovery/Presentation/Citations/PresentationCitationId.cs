// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Application.Discovery.Presentation.Citations;

/// <summary>Names one citation within one plan, which is how a block points at the source behind a fact.</summary>
/// <remarks>
/// <para>
/// The identifier is local to the plan that declares it and means nothing outside it. That is deliberate: a block
/// referring to a mail identifier directly would put the same identity in the plan as many times as it is cited, and
/// a reader checking two facts against one message would have no way to see that they rest on the same source. One
/// citation is declared once and referred to by this name as often as the answer needs it.
/// </para>
/// <para>
/// The spelling is restricted so a plan cannot smuggle content through a name a renderer prints beside a fact:
/// lower-case ASCII letters, digits, and the hyphen, bounded in length. Being a struct,
/// <see langword="default" /> is reachable and names nothing; it reports itself through <see cref="IsSpecified" /> and
/// is refused wherever a citation is required.
/// </para>
/// </remarks>
[JsonConverter(typeof(PresentationCitationIdJsonConverter))]
public readonly record struct PresentationCitationId
{
    /// <summary>The greatest number of characters one identifier may hold.</summary>
    public const int MaxLength = 32;

    private readonly string? value;

    private PresentationCitationId(string value) => this.value = value;

    /// <summary>Gets whether this value names a citation rather than the unusable struct default.</summary>
    public bool IsSpecified => this.value is not null;

    /// <summary>Gets the identifier as the plan spells it.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than an identifier.</exception>
    public string Value => this.value
        ?? throw new InvalidOperationException("The value is the default of the struct and names no citation.");

    /// <summary>Creates a citation identifier.</summary>
    /// <param name="value">The identifier, which is trimmed of surrounding whitespace before it is checked.</param>
    /// <returns>The validated identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when the value is blank, longer than <see cref="MaxLength" />, or spelled with anything but lower-case ASCII letters, digits, and hyphens.</exception>
    public static PresentationCitationId Create(string? value)
    {
        if (!TryCreate(value, out var created))
        {
            throw new ArgumentException(
                $"A citation identifier is between 1 and {MaxLength} lower-case ASCII letters, digits, and hyphens.",
                nameof(value));
        }

        return created;
    }

    /// <summary>Creates a citation identifier, reporting whether the value was acceptable instead of throwing.</summary>
    /// <param name="value">The identifier, which is trimmed of surrounding whitespace before it is checked.</param>
    /// <param name="created">The validated identifier when the value is acceptable; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the value names a citation; otherwise <see langword="false" />.</returns>
    public static bool TryCreate(string? value, out PresentationCitationId created)
    {
        created = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();

        if (trimmed.Length > MaxLength || !trimmed.All(IsPermitted))
        {
            return false;
        }

        created = new PresentationCitationId(trimmed);

        return true;
    }

    /// <inheritdoc />
    public override string ToString() => this.value ?? "(unspecified)";

    private static bool IsPermitted(char character) =>
        character is >= 'a' and <= 'z' || character is >= '0' and <= '9' || character is '-';
}

/// <summary>Serializes <see cref="PresentationCitationId" /> as the name the plan declares it under.</summary>
/// <remarks>
/// The type carries this converter through <see cref="JsonConverterAttribute" />, so an identifier read off the wire is
/// held to the same spelling as one composed in process.
/// </remarks>
public sealed class PresentationCitationIdJsonConverter : JsonConverter<PresentationCitationId>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string or does not spell an identifier.</exception>
    public override PresentationCitationId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"A citation identifier must be a JSON string, but the token was {reader.TokenType}.");
        }

        PresentationJsonBounds.EnsureCouldHoldAtMost(ref reader, PresentationCitationId.MaxLength, "citation identifier");

        return ParseOrThrow(reader.GetString());
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void Write(
        Utf8JsonWriter writer,
        PresentationCitationId value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(SpecifiedOrThrow(value));
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the property name does not spell an identifier.</exception>
    public override PresentationCitationId ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        PresentationJsonBounds.EnsureCouldHoldAtMost(ref reader, PresentationCitationId.MaxLength, "citation identifier");

        return ParseOrThrow(reader.GetString());
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        PresentationCitationId value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WritePropertyName(SpecifiedOrThrow(value));
    }

    private static PresentationCitationId ParseOrThrow(string? value) =>
        PresentationCitationId.TryCreate(value, out var parsed)
            ? parsed
            : throw new JsonException("The value does not spell a citation identifier.");

    private static string SpecifiedOrThrow(PresentationCitationId value) => value.IsSpecified
        ? value.Value
        : throw new JsonException("A citation identifier cannot be written from the unspecified default of the struct.");
}
