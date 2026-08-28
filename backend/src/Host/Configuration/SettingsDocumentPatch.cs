// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MailFathom.Application.Configuration;

namespace MailFathom.Host.Configuration;

/// <summary>Applies a write's changes to a copy of the persisted configuration document.</summary>
/// <remarks>
/// <para>
/// A configuration path is a sequence of property names and nothing else, so applying a change is walking that
/// sequence and setting or removing the property at the end of it. Every value is written as a JSON string, because a
/// configuration value <em>is</em> a string to every provider in the pipeline: the binder is what turns
/// <c>"30"</c> into a number and <c>"true"</c> into a flag, and writing a JSON number instead would make the persisted
/// layer the one source whose values arrive already typed.
/// </para>
/// <para>
/// Two normalizations happen on the way, and both preserve what the document contributes rather than changing it. A
/// property is matched ignoring case, because that is how every provider in the pipeline compares keys and a second
/// spelling of one setting is a document the layer refuses outright. And an array met along the path becomes an object
/// keyed by position, which flattens to exactly the keys the array did — the shape an operator is told to write for
/// the reason <c>docs/operations/configuration-sources.md</c> gives, since the parser renumbers a JSON array's
/// elements from <c>0</c> and an index-keyed object survives an edit beside it.
/// </para>
/// <para>
/// A scalar met where the path continues is replaced by an object, because JSON cannot hold a value and children under
/// one name and the write asked for the children. Nothing here judges the result: whether the document that comes out
/// is configuration the deployment can bind is decided against the composed configuration, which is where a setting
/// means something.
/// </para>
/// </remarks>
internal static class SettingsDocumentPatch
{
    /// <summary>Produces the document a write would persist.</summary>
    /// <param name="json">The document as it stands.</param>
    /// <param name="edits">The changes, applied in the order given.</param>
    /// <returns>The candidate document.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="FormatException">Thrown when the document as it stands is not a JSON object.</exception>
    /// <exception cref="JsonException">Thrown when the document as it stands is not JSON at all, which for a <c>jsonb</c> column means one nested deeper than the reader's maximum.</exception>
    public static string Apply(string json, IReadOnlyList<ConfigurationEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(edits);

        if (JsonNode.Parse(json) is not JsonObject document)
        {
            throw new FormatException(
                "The persisted configuration document is not a JSON object of configuration keys, so there is nothing for a write to change.");
        }

        foreach (var edit in edits)
        {
            var segments = edit.Path.Split(':');

            if (edit.RemovesTheSetting)
            {
                Remove(document, segments);
            }
            else
            {
                Set(document, segments, edit.Value!);
            }
        }

        return document.ToJsonString();
    }

    private static void Set(JsonObject document, string[] segments, string value)
    {
        var parent = document;

        foreach (var segment in segments[..^1])
        {
            parent = DescendInto(parent, segment);
        }

        parent[ExistingNameOf(parent, segments[^1])] = JsonValue.Create(value);
    }

    /// <summary>Drops the setting and every enclosing object the drop left holding nothing.</summary>
    /// <remarks>
    /// Pruning is what makes a removal the reverse of the write that added the setting: an object left behind holding
    /// no properties contributes no configuration key, but it does leave the document describing a section the
    /// deployment no longer persists, and the next operator to read the row would take it for one.
    /// </remarks>
    private static void Remove(JsonObject document, string[] segments)
    {
        var enclosing = new List<(JsonObject Parent, string Name)>();
        var parent = document;

        foreach (var segment in segments)
        {
            var name = ExistingNameOf(parent, segment);

            enclosing.Add((parent, name));

            if (parent[name] is JsonArray array)
            {
                parent[name] = KeyedByPosition(array);
            }

            if (parent[name] is not JsonObject child)
            {
                break;
            }

            parent = child;
        }

        if (enclosing.Count != segments.Length)
        {
            return;
        }

        foreach (var (holder, name) in Enumerable.Reverse(enclosing))
        {
            if (!holder.Remove(name))
            {
                return;
            }

            if (holder.Count > 0)
            {
                return;
            }
        }
    }

    /// <summary>Reaches the object a path segment names, making one where the document holds something else.</summary>
    private static JsonObject DescendInto(JsonObject parent, string segment)
    {
        var name = ExistingNameOf(parent, segment);

        if (parent[name] is JsonObject existing)
        {
            return existing;
        }

        var replacement = parent[name] is JsonArray array ? KeyedByPosition(array) : new JsonObject();

        parent[name] = replacement;

        return replacement;
    }

    /// <summary>Rewrites an array as the object of the same keys, so a later edit addresses a position rather than moving one.</summary>
    private static JsonObject KeyedByPosition(JsonArray array)
    {
        var keyed = new JsonObject();

        foreach (var (index, element) in array.Index())
        {
            keyed[index.ToString(CultureInfo.InvariantCulture)] = element?.DeepClone();
        }

        return keyed;
    }

    /// <summary>Finds how the document already spells a segment, so one setting never acquires a second spelling.</summary>
    private static string ExistingNameOf(JsonObject parent, string segment) => parent
        .Select(property => property.Key)
        .FirstOrDefault(key => key.Equals(segment, StringComparison.OrdinalIgnoreCase))
        ?? segment;
}
