// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MailFathom.Cli.Credentials;

namespace MailFathom.Cli.Commands.Configuration;

/// <summary>The file an editing session opens, and what changed underneath one that was refused.</summary>
/// <remarks>
/// <para>
/// The buffer is created readable by its owner alone, like the credential store beside it. Its contents are the
/// deployment's configuration with every secret redacted, which is not material — but it is a complete description of
/// what a deployment does, and a temporary directory on a shared machine is not where that belongs to everybody.
/// </para>
/// <para>
/// The path walk here is for reading a document to a person and for nothing else. What a saved buffer *means* is
/// decided by the deployment, which flattens it with the same JSON configuration provider that reads the row at
/// startup, so a disagreement between that and this costs a sentence of a report rather than a wrong write.
/// </para>
/// </remarks>
internal static class SettingsBuffer
{
    /// <summary>Writes the document an editing session opens, readable by its owner alone.</summary>
    /// <param name="path">Where to write it.</param>
    /// <param name="document">The document, as the deployment handed it over.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="IOException">Thrown when the file cannot be written.</exception>
    internal static void Write(string path, string document)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(document);

        using var buffer = OwnerOnlyStorage.OpenForWriting(path);
        using StreamWriter writer = new(buffer);

        writer.Write(document);
    }

    /// <summary>Names the settings that differ between the document a buffer was opened over and the one now in force.</summary>
    /// <param name="opened">The document the editing session started from.</param>
    /// <param name="inForce">The document the deployment now holds.</param>
    /// <returns>The paths that were added, removed, or given a different value, ordered as they read.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// What the other writer did, in the terms the refused operator re-applies their own change in. A path whose value
    /// merely moved between two redaction markers cannot be told apart from one that did not move at all, which is
    /// stated where the answer is reported rather than guessed at here.
    /// </remarks>
    internal static IReadOnlyList<string> MovedBetween(string opened, string inForce)
    {
        ArgumentNullException.ThrowIfNull(opened);
        ArgumentNullException.ThrowIfNull(inForce);

        var before = Flatten(opened);
        var after = Flatten(inForce);

        return
        [
            .. before
                .Where(setting => !after.TryGetValue(setting.Key, out var held)
                    || !string.Equals(held, setting.Value, StringComparison.Ordinal))
                .Select(setting => setting.Key)
                .Concat(after.Keys.Where(path => !before.ContainsKey(path)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>Reads a document as the colon-delimited settings it describes.</summary>
    /// <remarks>A document that is not JSON describes no settings, which is the answer here rather than a failure: nothing is written from this reading, and the deployment is what refuses a document it cannot read.</remarks>
    private static Dictionary<string, string> Flatten(string json)
    {
        Dictionary<string, string> settings = new(StringComparer.OrdinalIgnoreCase);

        try
        {
            Describe(JsonNode.Parse(json), prefix: string.Empty, settings);
        }
        catch (JsonException)
        {
            return settings;
        }

        return settings;
    }

    private static void Describe(JsonNode? node, string prefix, Dictionary<string, string> settings)
    {
        switch (node)
        {
            case JsonObject properties:
                foreach (var property in properties)
                {
                    Describe(property.Value, Beneath(prefix, property.Key), settings);
                }

                break;

            case JsonArray elements:
                foreach (var (element, position) in elements.Select((element, position) => (element, position)))
                {
                    Describe(element, Beneath(prefix, position.ToString(CultureInfo.InvariantCulture)), settings);
                }

                break;

            default:
                if (prefix.Length > 0)
                {
                    settings[prefix] = node?.ToString() ?? string.Empty;
                }

                break;
        }
    }

    private static string Beneath(string prefix, string segment) =>
        prefix.Length == 0 ? segment : $"{prefix}:{segment}";
}
