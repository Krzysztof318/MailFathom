// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Configuration;

/// <summary>What a deployment reports its settings say, and where each value is decided.</summary>
/// <param name="Version">The persisted configuration version the deployment composed its settings over.</param>
/// <param name="Settings">One entry per setting the reading covered, ordered by path.</param>
/// <remarks>
/// The version is what the write that follows is composed over, which is why every command that changes a setting
/// reads before it writes rather than asking for the version on its own. A number fetched apart from the values it
/// describes is the lost update the deployment's version guard exists to refuse.
/// </remarks>
internal sealed record ConfigurationReading(
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("settings")] IReadOnlyList<EffectiveSettingRecord>? Settings);

/// <summary>One setting as the deployment reads it.</summary>
/// <param name="Path">The colon-delimited configuration path.</param>
/// <param name="Value">The value the deployment reads, or the redaction marker where the setting bears a secret.</param>
/// <param name="Source">The deployment's own name for the layer that supplied the value.</param>
/// <param name="Origin">What identifies that source within its kind — a file's path — and nothing for a layer that has one instance.</param>
/// <param name="Redacted">Whether the value is the marker rather than what the deployment reads.</param>
internal sealed record EffectiveSettingRecord(
    [property: JsonPropertyName("path")] string? Path,
    [property: JsonPropertyName("value")] string? Value,
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("origin")] string? Origin,
    [property: JsonPropertyName("redacted")] bool Redacted)
{
    /// <summary>Describes where the value came from, in the words an operator repairs it with.</summary>
    /// <returns>The source, with the file it came from where the source has more than one.</returns>
    internal string DescribeSource() => this.Origin is { Length: > 0 } origin
        ? $"{this.Source} ({origin})"
        : this.Source ?? "unreported";
}
