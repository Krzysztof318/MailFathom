// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Application.Discovery.Presentation;

/// <summary>Holds the only kind of free text a presentation plan may carry: plain text a client draws as text.</summary>
/// <remarks>
/// <para>
/// Every text a plan holds passes through this type, which is what makes "a plan is never code" a property of the
/// contract rather than a rule a reviewer applies. Two things are refused. A value that <em>is</em> markup — one whose
/// content opens with <c>&lt;</c> and closes with <c>&gt;</c> — is refused outright, because that is the shape a model
/// returning a fragment of XAML, HTML, or SVG produces and there is no reading of it as prose. Control characters are
/// refused beside it, because a plan reaches a renderer, a log, and a screen reader, and none of those three agrees on
/// what one means.
/// </para>
/// <para>
/// What it deliberately does not do is sanitize text that merely mentions an angle bracket. A fragment quoted from mail
/// legitimately contains one, the client draws this value into a typed text element rather than into a parser, and a
/// contract that mangled a quotation to defend a renderer that never evaluates anything would be trading a real defect
/// for an imagined one.
/// </para>
/// <para>
/// The length bound is the plan's own rather than a screen's. No block in the catalogue presents a message body — the
/// plan cites one instead — so a text longer than a paragraph is a model writing an essay into a table cell, and
/// refusing it here is cheaper than every renderer deciding separately where to cut. Being a struct,
/// <see langword="default" /> is reachable and is not text: it reports itself through <see cref="IsSpecified" /> and is
/// refused by the converter and by every contract member that requires a value.
/// </para>
/// </remarks>
[JsonConverter(typeof(PresentationTextJsonConverter))]
public readonly record struct PresentationText
{
    /// <summary>The greatest number of characters one text may hold.</summary>
    public const int MaxLength = 4000;

    private readonly string? value;

    private PresentationText(string value) => this.value = value;

    /// <summary>Gets whether this value holds text rather than the unusable struct default.</summary>
    public bool IsSpecified => this.value is not null;

    /// <summary>Gets the text, which is never blank, never markup, and never carries a control character.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than text.</exception>
    public string Value => this.value
        ?? throw new InvalidOperationException("The value is the default of the struct and holds no text.");

    /// <summary>Creates a text a plan may carry.</summary>
    /// <param name="text">The text, which is trimmed of surrounding whitespace before it is checked.</param>
    /// <returns>The validated text.</returns>
    /// <exception cref="ArgumentException">Thrown when the text is blank, longer than <see cref="MaxLength" />, carries a control character, or is markup.</exception>
    public static PresentationText Create(string? text)
    {
        if (!TryCreate(text, out var created, out var refusal))
        {
            throw new ArgumentException(refusal, nameof(text));
        }

        return created;
    }

    /// <summary>Creates a text a plan may carry, reporting whether the value was acceptable instead of throwing.</summary>
    /// <param name="text">The text, which is trimmed of surrounding whitespace before it is checked.</param>
    /// <param name="created">The validated text when the value is acceptable; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the value is text a plan may carry; otherwise <see langword="false" />.</returns>
    public static bool TryCreate(string? text, out PresentationText created) =>
        TryCreate(text, out created, out _);

    /// <inheritdoc />
    public override string ToString() => this.value ?? "(unspecified)";

    private static bool TryCreate(string? text, out PresentationText created, out string refusal)
    {
        created = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            refusal = "A presentation text cannot be blank.";

            return false;
        }

        var trimmed = text.Trim();

        if (trimmed.Length > MaxLength)
        {
            refusal = $"A presentation text holds at most {MaxLength} characters.";

            return false;
        }

        if (trimmed.Any(character => char.IsControl(character) && character is not ('\n' or '\r' or '\t')))
        {
            refusal = "A presentation text cannot carry a control character.";

            return false;
        }

        if (trimmed.StartsWith('<') && trimmed.EndsWith('>'))
        {
            refusal = "A presentation plan carries no markup, and a text that opens and closes as a tag is markup rather than prose.";

            return false;
        }

        created = new PresentationText(trimmed);
        refusal = string.Empty;

        return true;
    }
}

/// <summary>Serializes <see cref="PresentationText" /> as the plain string it holds.</summary>
/// <remarks>
/// The type carries this converter through <see cref="JsonConverterAttribute" />, so a plan read off the wire is
/// validated by the same rules that hold for one composed in process — which is the point of putting the rules on the
/// value rather than on whatever produced it.
/// </remarks>
public sealed class PresentationTextJsonConverter : JsonConverter<PresentationText>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string or holds text a plan may not carry.</exception>
    public override PresentationText Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"A presentation text must be a JSON string, but the token was {reader.TokenType}.");
        }

        PresentationJsonBounds.EnsureCouldHoldAtMost(ref reader, PresentationText.MaxLength, "presentation text");

        return ParseOrThrow(reader.GetString());
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void Write(
        Utf8JsonWriter writer,
        PresentationText value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(SpecifiedOrThrow(value));
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the property name holds text a plan may not carry.</exception>
    public override PresentationText ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        PresentationJsonBounds.EnsureCouldHoldAtMost(ref reader, PresentationText.MaxLength, "presentation text");

        return ParseOrThrow(reader.GetString());
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        PresentationText value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WritePropertyName(SpecifiedOrThrow(value));
    }

    private static PresentationText ParseOrThrow(string? text) =>
        PresentationText.TryCreate(text, out var parsed)
            ? parsed
            : throw new JsonException("The value is not text a presentation plan may carry.");

    private static string SpecifiedOrThrow(PresentationText value) => value.IsSpecified
        ? value.Value
        : throw new JsonException("A presentation text cannot be written from the unspecified default of the struct.");
}
