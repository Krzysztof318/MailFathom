// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using MailFathom.Application.Configuration;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Host.Configuration.Administration;

/// <summary>Decides which configuration values leave this process with their content, and what stands in for the rest.</summary>
/// <remarks>
/// <para>
/// The promise is that no reading discloses what a write would have refused, and it is kept by applying the write's
/// own two rules rather than by deciding a second time what a secret is. A setting announces that it bears a secret
/// through its own name, which is what <see cref="SecretPropertyNaming" /> states for the bound options graph and
/// what the configuration writer refuses material by; and a setting the persisted layer is itself reached through is
/// named by <see cref="BootstrapOnlySettings" />, which is what makes it unwritable. A reading enumerates every
/// composed key rather than the ones MailFathom named, so the second rule is not redundant with the first:
/// <c>ConnectionStrings:mailfathom</c> carries a database credential under a key no name rule recognizes.
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

    /// <summary>Reports whether a configuration path names a setting this process will not disclose.</summary>
    /// <param name="path">The colon-delimited configuration path.</param>
    /// <returns><see langword="true" /> when the value at the path is redacted wherever it is read back.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Two rules, and the second is what makes the promise above this class true rather than nearly true. The name
    /// rule comes first: the last segment decides, because that is the property name the setting binds to, so a path
    /// ending in <c>SecretReference</c> bears one and the <c>Name</c> and <c>Lifetime</c> beside it are the non-secret
    /// handles that exist so a secret can be discussed without being read.
    /// </para>
    /// <para>
    /// The bootstrap rule covers what the name rule structurally cannot. A reading enumerates the whole composed
    /// configuration, and the settings the layer is itself reached through are named by the framework and by the
    /// operator's orchestrator rather than by MailFathom — <c>ConnectionStrings:mailfathom</c> is the worked case, a
    /// key whose last segment is the database's name and whose value is the credential a deployment following
    /// <c>docs/operations/secret-rotation.md</c> writes inline. No name rule reaches it, and it is exactly the path
    /// <see cref="BootstrapOnlySettings" /> makes unwritable — so redacting what a write refuses is what stops the
    /// weaker of the two permissions this surface publishes from yielding the credential the stronger one was
    /// separated out to protect. The two settings it also covers that carry no material are redacted with them rather
    /// than picked out: a rule stated as *what a write refuses* is one a reader can check against a list, and one
    /// stated as *the credential-bearing half of what a write refuses* is a second list to keep true.
    /// </para>
    /// </remarks>
    internal static bool Redacts(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return SecretPropertyNaming.NamesASecret(path.Split(':')[^1])
            || (!string.IsNullOrWhiteSpace(path) && BootstrapOnlySettings.TryFindCovering(path, out _));
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
    /// <see cref="Redacts" /> decides each leaf, exactly as it does for a single setting, and the walk carries the
    /// colon-delimited path down so that it can: the second of that method's two rules is stated over a whole path
    /// rather than over a property name, and a walk deciding by the property name alone would hand back
    /// <c>ConnectionStrings:mailfathom</c> from a row somebody wrote by hand while the keyed reading beside it
    /// redacted the same value. The walk reaches a value wherever it sits — an array element's property included,
    /// since a mail account's secret block is written inside one, and the position becomes a path segment there for
    /// the same reason the configuration binder makes it one. What is left is the shape of the document unchanged: an
    /// operator editing it sees which settings bear secrets and where, and what they never see is the material or the
    /// path to it.
    /// </remarks>
    internal static string ApplyToDocument(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        if (JsonNode.Parse(json) is not JsonObject document)
        {
            throw new FormatException(
                "The persisted configuration document is not a JSON object of configuration keys, so there is nothing to read back.");
        }

        RedactWithin(document, prefix: string.Empty);

        return document.ToJsonString(BufferFormat);
    }

    /// <summary>Replaces every secret-bearing value the object holds, descending into what it nests.</summary>
    private static void RedactWithin(JsonObject document, string prefix)
    {
        foreach (var property in document.ToList())
        {
            var path = Beneath(prefix, property.Key);

            switch (property.Value)
            {
                case JsonObject nested:
                    RedactWithin(nested, path);
                    break;

                case JsonArray elements:
                    RedactWithin(elements, path);
                    break;

                // Left exactly as it is, whatever the path says. A null leaf contributes no configuration key — the
                // flattening a save is differenced against drops a null value — so there is nothing there to withhold,
                // and marking it would open a buffer whose marker stands for no setting: the save would then be refused
                // for naming a path the document carries nothing at, including a save of the buffer unchanged, with no
                // action the refusal names that would clear it.
                case null:
                    break;

                default:
                    if (Redacts(path))
                    {
                        document[property.Key] = JsonValue.Create(Marker);
                    }

                    break;
            }
        }
    }

    /// <summary>Replaces every secret-bearing value the array's elements hold.</summary>
    /// <remarks>
    /// A position announces nothing, so the name rule never makes an element a secret by itself and what carries one
    /// under that rule is a property of an element. The second rule is not about a name: it matches a path prefix-wise,
    /// so <c>Persistence:Password:0</c> and <c>ConnectionStrings:mailfathom:0</c> are settings a write is refused at
    /// and are therefore settings a reading withholds. A hand-edited row reaches this walk from the database rather
    /// than through the layer, which is what makes an array at such a path something the document can actually hold.
    /// </remarks>
    private static void RedactWithin(JsonArray elements, string prefix)
    {
        foreach (var (position, element) in elements.ToList().Index())
        {
            var path = Beneath(prefix, position.ToString(CultureInfo.InvariantCulture));

            switch (element)
            {
                case JsonObject nested:
                    RedactWithin(nested, path);
                    break;

                case JsonArray nested:
                    RedactWithin(nested, path);
                    break;

                // Left exactly as it is, for the reason the object walk states: a null leaf is no configuration key,
                // so a marker there would stand for no setting and the save of an unchanged buffer would be refused.
                case null:
                    break;

                default:
                    if (Redacts(path))
                    {
                        elements[position] = JsonValue.Create(Marker);
                    }

                    break;
            }
        }
    }

    private static string Beneath(string prefix, string segment) =>
        prefix.Length == 0 ? segment : $"{prefix}:{segment}";
}
