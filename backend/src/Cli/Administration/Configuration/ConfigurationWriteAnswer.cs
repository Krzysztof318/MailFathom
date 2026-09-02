// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Configuration;

/// <summary>What one write to a deployment's persisted configuration produced.</summary>
/// <param name="Committed">Whether the deployment's configuration moved to a new version.</param>
/// <param name="Version">The version now in force, whether the write committed, was refused, or changed nothing.</param>
/// <param name="Code">The five-digit code naming why the write was refused, and nothing where nothing refused it.</param>
/// <param name="Messages">One sentence per reason the write was refused or changed nothing, and empty on a commit.</param>
/// <param name="Changes">What each named setting read as before the write and reads as now, and empty unless it committed.</param>
/// <remarks>
/// A refusal arrives as a named outcome with a success status rather than as an error, because each one is something
/// the operator acts on and continues from: correcting a value the deployment will not bind, changing a setting
/// somewhere else because a source above the layer supplies it, or reading the configuration again because somebody
/// else committed first. Each of them carries the version the next attempt is composed over.
/// </remarks>
internal sealed record ConfigurationWriteAnswer(
    [property: JsonPropertyName("committed")] bool Committed,
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("code")] int? Code,
    [property: JsonPropertyName("messages")] IReadOnlyList<string>? Messages,
    [property: JsonPropertyName("changes")] IReadOnlyList<SettingChangeRecord>? Changes)
{
    /// <summary>The deployment's code for a write composed over a version the document has already passed.</summary>
    /// <remarks>
    /// Named here because one command acts on it rather than only reporting it: an editing session refused for it goes
    /// back to the deployment to find out what moved, which is the one thing an operator cannot work out from the
    /// buffer in front of them.
    /// </remarks>
    internal const int VersionSuperseded = 12008;

    /// <summary>The deployment's code for a write to a setting a source above the persisted layer supplies.</summary>
    /// <remarks>Named here because the refusal has a flag that answers it, and the command that offers the flag is what tells the operator so.</remarks>
    internal const int WriteShadowed = 12013;

    /// <summary>States what the deployment said about a write that did not commit.</summary>
    /// <returns>One sentence per reason, or a single sentence where the deployment gave none.</returns>
    internal IReadOnlyList<string> DescribeRefusal() => this.Messages is { Count: > 0 } stated
        ? stated
        : ["The deployment did not commit the write and said nothing this command could act on."];
}

/// <summary>What one setting read as before a committed write and what it reads as after it.</summary>
/// <param name="Path">The colon-delimited configuration path the write named.</param>
/// <param name="Before">What the deployment read at the path before the commit, or nothing where no source supplied it.</param>
/// <param name="After">What the deployment reads at the path now, or nothing where nothing supplies it.</param>
internal sealed record SettingChangeRecord(
    [property: JsonPropertyName("path")] string? Path,
    [property: JsonPropertyName("before")] EffectiveSettingRecord? Before,
    [property: JsonPropertyName("after")] EffectiveSettingRecord? After)
{
    /// <summary>Describes one of the two readings, or that nothing supplied the setting.</summary>
    /// <param name="reading">The reading to describe.</param>
    /// <returns>The value with the layer that supplied it, or a sentence saying nothing did.</returns>
    internal static string Describe(EffectiveSettingRecord? reading) => reading is null
        ? "not set by any source"
        : $"{reading.Value} (from {reading.DescribeSource()})";
}
