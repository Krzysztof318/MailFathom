// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MailFathom.PublicSurfaces.UnitTests;

/// <summary>Writes a JSON surface in the one form two builds compare byte for byte.</summary>
/// <remarks>
/// Shared by the two surfaces rendered as JSON, because the question both answer is the same one: neither reflection,
/// nor a schema generator, nor a dictionary the framework filled promises an ordering, so a rendering that took the
/// order it was handed would differ between two runs of an unchanged tree. Sorting every object's keys removes that
/// without removing anything a reader of the surface would notice — a JSON object is unordered by definition, so two
/// renderings differ only where the surface does.
/// </remarks>
internal static class CanonicalJson
{
    /// <summary>How a record is written: indented so a diff is line by line, and unescaped so the prose stays readable.</summary>
    /// <remarks>
    /// The relaxed encoder is safe here for the reason it is unsafe on a response: this string is written to a file the
    /// repository holds and is never served to a client or embedded in a document. Escaping every apostrophe would make
    /// a description unreadable in the one place it exists to be read.
    /// </remarks>
    private static JsonSerializerOptions RenderingOptions { get; } = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Renders a node in the canonical form, ready to be held against a golden file.</summary>
    /// <param name="node">The surface as this run produced it.</param>
    /// <returns>The canonical text.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="node" /> is <see langword="null" />.</exception>
    /// <remarks>The rewrite below answers null only for a null input, which this one is not.</remarks>
    public static string Render(JsonNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return Canonical(node)!.ToJsonString(RenderingOptions);
    }

    /// <summary>Rewrites a node with every object's keys in ordinal order, so two renderings differ only where the surface does.</summary>
    /// <param name="node">The node to rewrite.</param>
    /// <returns>A copy of the node with its objects ordered.</returns>
    /// <remarks>
    /// Array order is left alone. A schema's <c>required</c> list, an enumeration's values, and the order parameters
    /// are declared in are sequences their producer wrote in a stable order, and sorting them would hide a reordering
    /// that a client reading positionally would see.
    /// </remarks>
    public static JsonNode? Canonical(JsonNode? node) => node switch
    {
        JsonObject properties => new JsonObject(properties
            .OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => KeyValuePair.Create(property.Key, Canonical(property.Value?.DeepClone())))),
        JsonArray items => new JsonArray([.. items.Select(item => Canonical(item?.DeepClone()))]),
        _ => node?.DeepClone(),
    };
}
