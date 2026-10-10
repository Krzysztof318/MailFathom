// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Policies;

/// <summary>What one write to a settings policy produced.</summary>
/// <param name="Committed">Whether the policy moved to a new version.</param>
/// <param name="Version">The version now in force, whether the write committed, was refused, or changed nothing.</param>
/// <param name="Code">The five-digit code naming why the write was refused, and nothing where nothing refused it.</param>
/// <param name="Messages">One sentence per reason the write was refused or changed nothing, and on a commit one per thing the deployment said beside accepting it — empty where it said none.</param>
/// <remarks>A refusal arrives as a named outcome with a success status for the reason a configuration write's does: each one is something the operator acts on and continues from, and each carries the version the next attempt is composed over.</remarks>
internal sealed record SettingsPolicyWriteAnswer(
    [property: JsonPropertyName("committed")] bool Committed,
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("code")] int? Code,
    [property: JsonPropertyName("messages")] IReadOnlyList<string>? Messages)
{
    /// <summary>States what the deployment said about a write that did not commit.</summary>
    /// <returns>One sentence per reason, or a single sentence where the deployment gave none.</returns>
    internal IReadOnlyList<string> DescribeRefusal() => this.Messages is { Count: > 0 } stated
        ? stated
        : ["The deployment did not commit the write and said nothing this command could act on."];
}
