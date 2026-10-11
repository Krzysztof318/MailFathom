// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Nodes;

namespace MailFathom.Host.Configuration.Policies;

/// <summary>Reads and replaces one property of a settings document by its path, as the configuration binder would name it.</summary>
/// <remarks>
/// A path is the record's own key names joined by colons and compared without regard to case, because that is how the
/// binder reads the same document: a record somebody wrote by hand as <c>language</c> states the property a policy
/// names <c>Language</c>. A property is read and replaced whole — a list is one node, never an entry of one — which is
/// what lets a layer of a resolution replace the layer beneath it rather than merge with it.
/// </remarks>
internal static class SettingsDocumentTree
{
    /// <summary>Finds what a document states for a path.</summary>
    /// <param name="document">The document.</param>
    /// <param name="path">The property's path.</param>
    /// <returns>The node, or <see langword="null" /> where the document states nothing for the path: no such key, a JSON <c>null</c>, or text holding only white space.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path" /> is <see langword="null" />, empty, or white space.</exception>
    internal static JsonNode? Find(JsonObject document, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        JsonNode? current = document;

        foreach (var segment in path.Split(':'))
        {
            if (current is not JsonObject enclosing || NameIn(enclosing, segment) is not { } name)
            {
                return null;
            }

            current = enclosing[name];
        }

        return current is JsonValue value && value.TryGetValue<string>(out var text) && string.IsNullOrWhiteSpace(text)
            ? null
            : current;
    }

    /// <summary>Replaces what a document states for a path, creating the blocks on the way to it.</summary>
    /// <param name="document">The document, which is changed.</param>
    /// <param name="path">The property's path.</param>
    /// <param name="value">What the document states afterwards, or <see langword="null" /> to state nothing.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="document" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="path" /> is <see langword="null" />, empty, or white space.</exception>
    /// <remarks>
    /// Stating nothing takes the blocks it leaves empty with it, so a document that had a value written into a block
    /// of its own and taken out again is the document it was before, rather than one carrying an empty block that
    /// reads as a change.
    /// </remarks>
    internal static void Replace(JsonObject document, string path, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var segments = path.Split(':');
        var enclosing = document;
        var entered = new List<(JsonObject Parent, string Name)>(segments.Length - 1);

        foreach (var segment in segments[..^1])
        {
            var name = NameIn(enclosing, segment) ?? segment;

            if (enclosing[name] is not JsonObject block)
            {
                if (value is null)
                {
                    return;
                }

                block = [];
                enclosing[name] = block;
            }

            entered.Add((enclosing, name));
            enclosing = block;
        }

        var written = NameIn(enclosing, segments[^1]) ?? segments[^1];

        if (value is not null)
        {
            enclosing[written] = value.DeepClone();

            return;
        }

        enclosing.Remove(written);

        for (var depth = entered.Count - 1; depth >= 0 && entered[depth].Parent[entered[depth].Name] is JsonObject { Count: 0 }; depth--)
        {
            entered[depth].Parent.Remove(entered[depth].Name);
        }
    }

    /// <summary>Reports whether two nodes state one value.</summary>
    /// <param name="first">One statement, or <see langword="null" /> for none.</param>
    /// <param name="second">The other, or <see langword="null" /> for none.</param>
    /// <returns><see langword="true" /> when both state nothing or both state the same value.</returns>
    /// <remarks>
    /// One value is compared as the binder reads it rather than as JSON types it: a record's keyed writes hold every
    /// value as text, so <c>"true"</c> on a record and <c>true</c> in a policy are one statement, and a written name
    /// such as a language is read without regard to case. Anything else — a list, a block — is compared as JSON.
    /// </remarks>
    internal static bool StateTheSame(JsonNode? first, JsonNode? second)
    {
        if (first is null || second is null)
        {
            return first is null && second is null;
        }

        return first is JsonValue one && second is JsonValue other
            ? string.Equals(TextOf(one), TextOf(other), StringComparison.OrdinalIgnoreCase)
            : JsonNode.DeepEquals(first, second);
    }

    private static string TextOf(JsonValue value) =>
        value.TryGetValue<string>(out var text) ? text : value.ToJsonString();

    private static string? NameIn(JsonObject enclosing, string segment) => enclosing
        .Select(member => member.Key)
        .FirstOrDefault(name => name.Equals(segment, StringComparison.OrdinalIgnoreCase));
}
