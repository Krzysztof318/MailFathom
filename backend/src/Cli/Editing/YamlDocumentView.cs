// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace MailFathom.Cli.Editing;

/// <summary>Renders a JSON document as YAML for an operator to edit, and reads the edited YAML back as JSON.</summary>
/// <remarks>
/// <para>
/// The two directions agree on the YAML 1.2 core schema, which is what keeps a value's JSON type across the round trip.
/// A plain scalar is read as <c>null</c>, a boolean, or a number only where that schema says it is one, and a JSON
/// string that would read as one of those is rendered quoted — so <c>"true"</c> stays text and <c>on</c> is text,
/// never the boolean YAML 1.1 made of it.
/// </para>
/// <para>
/// What YAML can say and JSON cannot is refused rather than resolved: more than one document, anchors and aliases,
/// tags, a key repeated within one mapping, a key that is not a scalar, and the infinities and not-a-number JSON has no
/// value for. Each refusal names the position, so the operator can find it in the buffer they wrote.
/// </para>
/// <para>
/// Only the event-level parser and emitter are used, and the JSON side is read and written through
/// <see cref="JsonDocument" /> and <see cref="Utf8JsonWriter" />, so nothing here reaches the reflection the trimmed
/// command cannot carry.
/// </para>
/// </remarks>
internal static partial class YamlDocumentView
{
    /// <summary>How deeply mappings and sequences may nest, which is the JSON reader's own default.</summary>
    private const int MaximumDepth = 64;

    /// <summary>Renders a JSON document as YAML.</summary>
    /// <param name="json">The document as the deployment served it.</param>
    /// <returns>The YAML rendering, or the text unchanged where it holds no document at all.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="json" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the deployment served something that is not JSON.</exception>
    internal static string Render(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        using var document = ParseJson(json);
        using StringWriter writer = new(CultureInfo.InvariantCulture);
        var emitter = new Emitter(writer);

        emitter.Emit(new StreamStart());
        emitter.Emit(new DocumentStart());
        EmitNode(emitter, document.RootElement);
        emitter.Emit(new DocumentEnd(isImplicit: true));
        emitter.Emit(new StreamEnd());

        return writer.ToString();
    }

    /// <summary>Reads an edited YAML buffer back as the JSON document it describes.</summary>
    /// <param name="yaml">The buffer the operator saved.</param>
    /// <returns>The JSON document, or <see langword="null" /> where the buffer holds no document — only comments, or nothing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="yaml" /> is <see langword="null" />.</exception>
    /// <exception cref="FormatException">Thrown when the buffer is not valid YAML or says something JSON cannot, naming the position.</exception>
    internal static string? ReadBack(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        var parser = new Parser(new StringReader(yaml));
        ArrayBufferWriter<byte> json = new();

        try
        {
            parser.Consume<StreamStart>();

            if (parser.TryConsume<StreamEnd>(out _))
            {
                return null;
            }

            parser.Consume<DocumentStart>();

            using (Utf8JsonWriter writer = new(json, new JsonWriterOptions { Indented = true }))
            {
                WriteNode(parser, writer, depth: 0);
            }

            parser.Consume<DocumentEnd>();

            if (parser.Current is not StreamEnd)
            {
                throw Refused(parser.Current!, "The buffer holds more than one YAML document, and the deployment holds one");
            }

            return Encoding.UTF8.GetString(json.WrittenSpan);
        }
        catch (YamlException failure)
        {
            throw new FormatException(
                $"The buffer is not valid YAML at line {failure.Start.Line.ToString(CultureInfo.InvariantCulture)}, column {failure.Start.Column.ToString(CultureInfo.InvariantCulture)}: {failure.Message}",
                failure);
        }
    }

    /// <summary>Reports whether two JSON documents describe the same values, however each is laid out.</summary>
    /// <param name="first">One document.</param>
    /// <param name="second">The other document.</param>
    /// <returns><see langword="true" /> when both parse and are equal as JSON values.</returns>
    /// <remarks>
    /// A YAML buffer saved with nothing but a comment or a layout changed is a buffer saved unchanged: the committed
    /// document is JSON, and it would be the one the deployment already holds.
    /// </remarks>
    internal static bool DescribeTheSameDocument(string first, string second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        try
        {
            using var firstDocument = JsonDocument.Parse(first);
            using var secondDocument = JsonDocument.Parse(second);

            return JsonElement.DeepEquals(firstDocument.RootElement, secondDocument.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static JsonDocument ParseJson(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException failure)
        {
            throw new CliFailure("The deployment served a document that is not JSON, so it cannot be shown as YAML.", failure);
        }
    }

    private static void EmitNode(IEmitter emitter, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                emitter.Emit(new MappingStart(AnchorName.Empty, TagName.Empty, isImplicit: true, MappingStyle.Any));

                foreach (var property in element.EnumerateObject())
                {
                    emitter.Emit(TextScalar(property.Name, allowBlock: false));
                    EmitNode(emitter, property.Value);
                }

                emitter.Emit(new MappingEnd());
                break;
            case JsonValueKind.Array:
                emitter.Emit(new SequenceStart(AnchorName.Empty, TagName.Empty, isImplicit: true, SequenceStyle.Any));

                foreach (var item in element.EnumerateArray())
                {
                    EmitNode(emitter, item);
                }

                emitter.Emit(new SequenceEnd());
                break;
            case JsonValueKind.String:
                emitter.Emit(TextScalar(element.GetString()!, allowBlock: true));
                break;
            default:
                emitter.Emit(PlainScalar(element.GetRawText()));
                break;
        }
    }

    /// <summary>Builds the scalar a JSON string is rendered as, quoted wherever plain text would read as another type.</summary>
    /// <remarks>
    /// A multi-line value is asked for as a literal block, which is what an operator reads most easily; the emitter
    /// falls back to a quoted form where a literal block cannot carry the text exactly.
    /// </remarks>
    private static Scalar TextScalar(string text, bool allowBlock)
    {
        var style = ResolvesAsAnotherType(text) ? ScalarStyle.DoubleQuoted
            : allowBlock && text.Contains('\n', StringComparison.Ordinal) ? ScalarStyle.Literal
            : ScalarStyle.Any;

        return new Scalar(
            AnchorName.Empty,
            TagName.Empty,
            text,
            style,
            isPlainImplicit: style == ScalarStyle.Any,
            isQuotedImplicit: true);
    }

    private static Scalar PlainScalar(string text) =>
        new(AnchorName.Empty, TagName.Empty, text, ScalarStyle.Plain, isPlainImplicit: true, isQuotedImplicit: false);

    private static bool ResolvesAsAnotherType(string text) =>
        IsCoreNull(text) || IsCoreBoolean(text) || CoreInteger().IsMatch(text) || CoreFloat().IsMatch(text) || IsCoreSpecialFloat(text);

    private static void WriteNode(IParser parser, Utf8JsonWriter writer, int depth)
    {
        var current = parser.Current!;

        if (current is AnchorAlias)
        {
            throw Refused(current, "An alias is refused, because JSON has no way to say a value is defined elsewhere");
        }

        var node = (NodeEvent)current;

        if (!node.Anchor.IsEmpty)
        {
            throw Refused(current, "An anchor is refused, because it exists only to be read through an alias");
        }

        if (!node.Tag.IsEmpty)
        {
            throw Refused(current, "A tag is refused, because it changes how a value is read without that showing in the value");
        }

        if (depth > MaximumDepth)
        {
            throw Refused(current, $"The buffer nests deeper than {MaximumDepth.ToString(CultureInfo.InvariantCulture)} levels");
        }

        switch (node)
        {
            case MappingStart:
                WriteMapping(parser, writer, depth);
                break;
            case SequenceStart:
                WriteSequence(parser, writer, depth);
                break;
            default:
                WriteScalar(parser.Consume<Scalar>(), writer);
                break;
        }
    }

    private static void WriteMapping(IParser parser, Utf8JsonWriter writer, int depth)
    {
        parser.Consume<MappingStart>();
        writer.WriteStartObject();
        HashSet<string> keys = new(StringComparer.Ordinal);

        while (!parser.TryConsume<MappingEnd>(out _))
        {
            if (parser.Current is not Scalar key || !key.Anchor.IsEmpty || !key.Tag.IsEmpty)
            {
                throw Refused(parser.Current!, "A mapping key must be a plain or quoted text, without an anchor, an alias, or a tag");
            }

            if (!keys.Add(key.Value))
            {
                throw Refused(key, $"The key '{key.Value}' is written more than once in the same mapping");
            }

            parser.MoveNext();
            writer.WritePropertyName(key.Value);
            WriteNode(parser, writer, depth + 1);
        }

        writer.WriteEndObject();
    }

    private static void WriteSequence(IParser parser, Utf8JsonWriter writer, int depth)
    {
        parser.Consume<SequenceStart>();
        writer.WriteStartArray();

        while (!parser.TryConsume<SequenceEnd>(out _))
        {
            WriteNode(parser, writer, depth + 1);
        }

        writer.WriteEndArray();
    }

    /// <summary>Writes a scalar as the JSON value the YAML 1.2 core schema resolves it to.</summary>
    /// <remarks>Only a plain scalar is resolved; a quoted or block scalar is text whatever it says.</remarks>
    private static void WriteScalar(Scalar scalar, Utf8JsonWriter writer)
    {
        var text = scalar.Value;

        if (scalar.Style != ScalarStyle.Plain)
        {
            writer.WriteStringValue(text);
        }
        else if (IsCoreNull(text))
        {
            writer.WriteNullValue();
        }
        else if (IsCoreBoolean(text))
        {
            writer.WriteBooleanValue(text[0] is 't' or 'T');
        }
        else if (CoreInteger().IsMatch(text))
        {
            writer.WriteRawValue(IntegerAsJson(text));
        }
        else if (CoreFloat().IsMatch(text))
        {
            writer.WriteRawValue(FloatAsJson(text));
        }
        else if (IsCoreSpecialFloat(text))
        {
            throw Refused(scalar, $"The value '{text}' is a YAML number JSON has no value for; quote it if it is meant as text");
        }
        else
        {
            writer.WriteStringValue(text);
        }
    }

    private static string IntegerAsJson(string text)
    {
        var value = text.StartsWith("0o", StringComparison.Ordinal)
            ? text[2..].Aggregate(BigInteger.Zero, (total, digit) => (total * 8) + (digit - '0'))
            : text.StartsWith("0x", StringComparison.Ordinal)
                ? BigInteger.Parse("0" + text[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)
                : BigInteger.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Spells a core-schema float the way JSON's grammar accepts it, keeping every significant digit the operator wrote.</summary>
    /// <remarks>YAML accepts <c>+1.5</c>, <c>.5</c>, <c>1.</c>, and <c>01.5</c>, and JSON accepts none of the four as written.</remarks>
    private static string FloatAsJson(string text)
    {
        var sign = text.StartsWith('-') ? "-" : string.Empty;
        var magnitude = text.TrimStart('+', '-');
        var exponentAt = magnitude.IndexOfAny(['e', 'E']);
        var mantissa = exponentAt < 0 ? magnitude : magnitude[..exponentAt];
        var exponent = exponentAt < 0 ? string.Empty : magnitude[exponentAt..];
        var pointAt = mantissa.IndexOf('.', StringComparison.Ordinal);
        var integral = (pointAt < 0 ? mantissa : mantissa[..pointAt]).TrimStart('0');
        var fraction = pointAt < 0 ? string.Empty : mantissa[(pointAt + 1)..];

        return $"{sign}{(integral.Length == 0 ? "0" : integral)}{(fraction.Length == 0 ? string.Empty : "." + fraction)}{exponent}";
    }

    private static bool IsCoreNull(string text) => text is "" or "~" or "null" or "Null" or "NULL";

    private static bool IsCoreBoolean(string text) => text is "true" or "True" or "TRUE" or "false" or "False" or "FALSE";

    private static bool IsCoreSpecialFloat(string text) =>
        text is ".inf" or ".Inf" or ".INF" or "+.inf" or "+.Inf" or "+.INF" or "-.inf" or "-.Inf" or "-.INF" or ".nan" or ".NaN" or ".NAN";

    [GeneratedRegex("^(?:[-+]?[0-9]+|0o[0-7]+|0x[0-9a-fA-F]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex CoreInteger();

    [GeneratedRegex(@"^[-+]?(?:\.[0-9]+|[0-9]+(?:\.[0-9]*)?)(?:[eE][-+]?[0-9]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex CoreFloat();

    private static FormatException Refused(ParsingEvent at, string reason) =>
        new($"{reason}, at line {at.Start.Line.ToString(CultureInfo.InvariantCulture)}, column {at.Start.Column.ToString(CultureInfo.InvariantCulture)}.");
}
