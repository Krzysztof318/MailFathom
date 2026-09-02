// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Configuration;

/// <summary>The persisted configuration document as an editing session receives it.</summary>
/// <param name="Version">The version the document was read at, which the save that follows is judged against.</param>
/// <param name="Document">The sparse document, with every secret-bearing value replaced by the redaction marker.</param>
internal sealed record ConfigurationDocument(
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("document")] string? Document);

/// <summary>The keyed changes one write asks a deployment for.</summary>
/// <param name="Version">The version the changes were composed over.</param>
/// <param name="Changes">The changes, applied together or not at all.</param>
/// <param name="EvenIfShadowed">Whether the deployment should commit a change to a setting a source above the persisted layer supplies.</param>
internal sealed record ConfigurationWriteRequest(
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("changes")] IReadOnlyList<ConfigurationChangeRequest> Changes,
    [property: JsonPropertyName("evenIfShadowed")] bool EvenIfShadowed);

/// <summary>One change a write asks for.</summary>
/// <param name="Path">The colon-delimited configuration path.</param>
/// <param name="Value">The value the setting takes, or <see langword="null" /> to stop the document carrying it.</param>
internal sealed record ConfigurationChangeRequest(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("value")] string? Value);

/// <summary>The document an editing session saved, and the version it was opened over.</summary>
/// <param name="Version">The version the buffer was opened over.</param>
/// <param name="Document">The document as the operator saved it.</param>
/// <param name="EvenIfShadowed">Whether the deployment should commit a change to a setting a source above the persisted layer supplies.</param>
internal sealed record ConfigurationDocumentRequest(
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("document")] string Document,
    [property: JsonPropertyName("evenIfShadowed")] bool EvenIfShadowed);

/// <summary>The path an adoption takes into the persisted layer.</summary>
/// <param name="Version">The version the adoption was previewed over.</param>
/// <param name="Prefix">The colon-delimited path to adopt beneath.</param>
/// <param name="EvenIfShadowed">Whether the deployment should commit a setting a source above the persisted layer supplies.</param>
internal sealed record ConfigurationAdoptionRequest(
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("prefix")] string Prefix,
    [property: JsonPropertyName("evenIfShadowed")] bool EvenIfShadowed);
