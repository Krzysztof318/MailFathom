// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using YamlDotNet.Core;

namespace MailFathom.Host.Configuration.Provisioning;

/// <summary>Flattens a YAML configuration document into colon-delimited keys, refusing what YAML can say that JSON cannot.</summary>
/// <remarks>
/// <para>
/// The result is the one the framework's JSON provider gives for the equivalent JSON document: mappings compose by key,
/// sequences become indexed keys from <c>0</c>, an empty mapping is a key without a value, an empty sequence is a key
/// with an empty value, and a key already present fails the read. That equivalence is the contract, so nothing past
/// the read has to know which format a setting came from.
/// </para>
/// <para>
/// A scalar is handed on as the text written. YAML 1.1 reads <c>no</c> and <c>on</c> as booleans and <c>0x10</c> as a
/// number, and a parser applying those rules would change a setting to something its author never typed; the binder is
/// what decides what a text means, exactly as it does for a JSON string. The one exception is a plain null —
/// <c>null</c>, <c>Null</c>, <c>NULL</c>, <c>~</c>, or nothing at all — which is read as JSON's <c>null</c>, because a
/// key written with no value would otherwise bind as the empty string.
/// </para>
/// <para>
/// Everything that would let a file mean something different from what it appears to say is refused, naming the
/// position: more than one document, a root that is not a mapping, a key repeated within one mapping, a key that is not
/// a scalar, anchors and aliases, and tags. Each of those is a place where the value a reviewer reads in the diff is not
/// the value the host would bind.
/// </para>
/// </remarks>
internal static class YamlConfigurationDocument
{
    /// <summary>How deeply mappings and sequences may nest, which is the JSON reader's own default.</summary>
    private const int MaximumDepth = 64;

    /// <summary>Flattens a YAML document into configuration keys.</summary>
    /// <param name="stream">The document; read to its end and disposed.</param>
    /// <returns>The keys, compared without regard to case, which is empty for a file holding no document at all.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stream" /> is <see langword="null" />.</exception>
    /// <exception cref="FormatException">Thrown when the document is not valid YAML or uses a feature this reader refuses, naming the position.</exception>
    /// <remarks>
    /// A file holding only comments is a file with nothing in it rather than a malformed one, which is what an operator
    /// who commented out every setting during a rollout has written.
    /// </remarks>
    public static Dictionary<string, string?> Flatten(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new StreamReader(stream);
        var parser = new Parser(reader);
        Dictionary<string, string?> data = new(StringComparer.OrdinalIgnoreCase);

        try
        {
            parser.Consume<YamlDotNet.Core.Events.StreamStart>();

            if (parser.TryConsume<YamlDotNet.Core.Events.StreamEnd>(out _))
            {
                return data;
            }

            parser.Consume<YamlDotNet.Core.Events.DocumentStart>();

            if (parser.Current is not YamlDotNet.Core.Events.MappingStart)
            {
                throw Refused(parser.Current!, "The document's root is not a mapping of settings");
            }

            VisitNode(parser, data, []);
            parser.Consume<YamlDotNet.Core.Events.DocumentEnd>();

            if (parser.Current is not YamlDotNet.Core.Events.StreamEnd)
            {
                throw Refused(parser.Current!, "The file holds more than one YAML document, and a configuration file is one");
            }

            return data;
        }
        catch (YamlException failure)
        {
            throw new FormatException(
                $"The file is not valid YAML at line {failure.Start.Line.ToString(CultureInfo.InvariantCulture)}, column {failure.Start.Column.ToString(CultureInfo.InvariantCulture)}: {failure.Message}",
                failure);
        }
    }

    private static void VisitNode(IParser parser, Dictionary<string, string?> data, List<string> path)
    {
        var current = parser.Current!;

        if (current is YamlDotNet.Core.Events.AnchorAlias)
        {
            throw Refused(current, "An alias is refused, because a value defined elsewhere in the file is not the value a reviewer reads where it is used");
        }

        var node = (YamlDotNet.Core.Events.NodeEvent)current;

        if (!node.Anchor.IsEmpty)
        {
            throw Refused(current, "An anchor is refused, because it exists only to be read through an alias");
        }

        if (!node.Tag.IsEmpty)
        {
            throw Refused(current, "A tag is refused, because it changes how a value is read without that showing in the value");
        }

        if (path.Count > MaximumDepth)
        {
            throw Refused(current, $"The document nests deeper than {MaximumDepth.ToString(CultureInfo.InvariantCulture)} levels");
        }

        switch (node)
        {
            case YamlDotNet.Core.Events.MappingStart:
                VisitMapping(parser, data, path);
                break;
            case YamlDotNet.Core.Events.SequenceStart:
                VisitSequence(parser, data, path);
                break;
            default:
                var scalar = parser.Consume<YamlDotNet.Core.Events.Scalar>();
                var key = ConfigurationPath.Combine(path);

                if (!data.TryAdd(key, ValueOf(scalar)))
                {
                    throw Refused(scalar, $"The key '{key}' is written more than once");
                }

                break;
        }
    }

    private static void VisitMapping(IParser parser, Dictionary<string, string?> data, List<string> path)
    {
        parser.Consume<YamlDotNet.Core.Events.MappingStart>();
        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);

        while (!parser.TryConsume<YamlDotNet.Core.Events.MappingEnd>(out _))
        {
            if (parser.Current is not YamlDotNet.Core.Events.Scalar key || !key.Anchor.IsEmpty || !key.Tag.IsEmpty)
            {
                throw Refused(parser.Current!, "A mapping key must be a plain or quoted text, without an anchor, an alias, or a tag");
            }

            if (!keys.Add(key.Value))
            {
                throw Refused(key, $"The key '{key.Value}' is written more than once in the same mapping");
            }

            parser.MoveNext();
            path.Add(key.Value);
            VisitNode(parser, data, path);
            path.RemoveAt(path.Count - 1);
        }

        if (keys.Count == 0 && path.Count > 0)
        {
            data[ConfigurationPath.Combine(path)] = null;
        }
    }

    private static void VisitSequence(IParser parser, Dictionary<string, string?> data, List<string> path)
    {
        parser.Consume<YamlDotNet.Core.Events.SequenceStart>();
        var index = 0;

        while (!parser.TryConsume<YamlDotNet.Core.Events.SequenceEnd>(out _))
        {
            path.Add(index.ToString(CultureInfo.InvariantCulture));
            VisitNode(parser, data, path);
            path.RemoveAt(path.Count - 1);
            index++;
        }

        if (index == 0)
        {
            data[ConfigurationPath.Combine(path)] = string.Empty;
        }
    }

    private static string? ValueOf(YamlDotNet.Core.Events.Scalar scalar) =>
        scalar.Style == ScalarStyle.Plain && scalar.Value is "" or "~" or "null" or "Null" or "NULL"
            ? null
            : scalar.Value;

    private static FormatException Refused(YamlDotNet.Core.Events.ParsingEvent at, string reason) =>
        new($"{reason}, at line {at.Start.Line.ToString(CultureInfo.InvariantCulture)}, column {at.Start.Column.ToString(CultureInfo.InvariantCulture)}.");
}
