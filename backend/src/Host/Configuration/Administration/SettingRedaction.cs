// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.Configuration.Administration;

/// <summary>Decides which configuration values leave this process with their content, and what stands in for the rest.</summary>
/// <remarks>
/// <para>
/// A setting announces that it bears a secret through its own name, which is the rule
/// <see cref="SecretPropertyNaming" /> already states for the bound options graph and the rule the configuration
/// writer refuses material by. Reading applies the same one, so nothing here decides a second time what a secret is
/// and no reading can disclose what a write would have refused.
/// </para>
/// <para>
/// It redacts the reference rather than only material, and deliberately. Under the default interpretation a
/// secret-bearing value is a <c>&lt;scheme&gt;:&lt;target&gt;</c> reference and discloses a path rather than a
/// credential — but a deployment that chose an inline interpretation has values that are the credential, and a row
/// somebody wrote by hand carries whatever they wrote. One rule that covers both is the only one that cannot be wrong
/// about which deployment it is running in.
/// </para>
/// </remarks>
internal static class SettingRedaction
{
    /// <summary>What stands in for a secret-bearing value everywhere one is read back.</summary>
    /// <remarks>
    /// It carries no colon, so it is not a reference to any scheme, which is what makes it safe wherever it is read
    /// back and typed again. A value left at the marker is not a difference, so a buffer saved with one still standing
    /// leaves that setting exactly as it was; a keyed write that names the marker at a secret-bearing path reaches the
    /// writer instead and is refused there as material, rather than persisting a reference that looks deliberate. Its
    /// characters are chosen for the same reason the document below is written with a relaxed encoder: a marker
    /// spelled with angle brackets is escaped by every JSON writer that defends against HTML, so it reaches an editor
    /// as an escape sequence rather than as the word an operator has to recognize.
    /// </remarks>
    internal const string Marker = "(redacted)";

    /// <summary>How a document is written for somebody to read and edit.</summary>
    /// <remarks>
    /// The relaxed encoder is what makes it editable rather than only readable. The default one escapes every character
    /// HTML would give a meaning to, so an endpoint address carrying a query string reaches the buffer with every
    /// <c>&amp;</c> re-spelled as a numeric escape — still valid JSON and still the same value, and still a document
    /// an operator would reasonably correct by hand into something they did not mean. Nothing here is written into a
    /// page, so the escaping protects against nothing and costs legibility of the one artifact a person edits.
    /// </remarks>
    private static readonly JsonSerializerOptions BufferFormat = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Reports whether a configuration path names a setting that bears a secret.</summary>
    /// <param name="path">The colon-delimited configuration path.</param>
    /// <returns><see langword="true" /> when the value at the path is redacted wherever it is read back.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The last segment decides, because that is the property name the setting binds to: a path ending in
    /// <c>SecretReference</c> bears one and the <c>Name</c> and <c>Lifetime</c> beside it are the non-secret handles
    /// that exist so a secret can be discussed without being read.
    /// </remarks>
    internal static bool Redacts(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return SecretPropertyNaming.NamesASecret(path.Split(':')[^1]);
    }

    /// <summary>Reports the value as it may leave this process.</summary>
    /// <param name="path">The colon-delimited configuration path.</param>
    /// <param name="value">The value the deployment reads.</param>
    /// <returns>The value, or <see cref="Marker" /> where the path bears a secret.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static string Apply(string path, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return Redacts(path) ? Marker : value;
    }

    /// <summary>Reports a whole persisted document as it may leave this process.</summary>
    /// <param name="json">The document as the row holds it.</param>
    /// <returns>The document with every secret-bearing value replaced by <see cref="Marker" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="json" /> is <see langword="null" />.</exception>
    /// <exception cref="FormatException">Thrown when the document is not a JSON object.</exception>
    /// <exception cref="JsonException">Thrown when the document is not JSON at all.</exception>
    /// <remarks>
    /// The property name decides, exactly as it does for a single setting, and the walk reaches a value wherever it
    /// sits — an array element's property included, since a mail account's secret block is written inside one. What is
    /// left is the shape of the document unchanged: an operator editing it sees which settings bear secrets and where,
    /// and what they never see is the material or the path to it.
    /// </remarks>
    internal static string ApplyToDocument(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        if (JsonNode.Parse(json) is not JsonObject document)
        {
            throw new FormatException(
                "The persisted configuration document is not a JSON object of configuration keys, so there is nothing to read back.");
        }

        RedactWithin(document);

        return document.ToJsonString(BufferFormat);
    }

    /// <summary>Replaces every secret-bearing value the object holds, descending into what it nests.</summary>
    private static void RedactWithin(JsonObject document)
    {
        foreach (var property in document.ToList())
        {
            switch (property.Value)
            {
                case JsonObject nested:
                    RedactWithin(nested);
                    break;

                case JsonArray elements:
                    RedactWithin(elements);
                    break;

                default:
                    if (SecretPropertyNaming.NamesASecret(property.Key))
                    {
                        document[property.Key] = JsonValue.Create(Marker);
                    }

                    break;
            }
        }
    }

    /// <summary>Replaces every secret-bearing value the array's elements hold.</summary>
    /// <remarks>An element is never itself a secret-bearing setting, because a secret is announced by a property name and a position has none; what carries one is a property of an element.</remarks>
    private static void RedactWithin(JsonArray elements)
    {
        foreach (var element in elements)
        {
            switch (element)
            {
                case JsonObject nested:
                    RedactWithin(nested);
                    break;

                case JsonArray nested:
                    RedactWithin(nested);
                    break;

                default:
                    break;
            }
        }
    }
}
