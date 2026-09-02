// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Diagnostics;

/// <summary>The serialization contract the invocation log is written through.</summary>
/// <remarks>
/// Its own context rather than an entry in <see cref="CliJsonContext" />, because the two want opposite formatting.
/// Everything that context serializes is read by a person or sent to a deployment and is indented for it; a log is one
/// record per line, so that <c>tail</c>, <c>grep</c>, and <c>jq</c> each work on it without a parser that spans lines.
/// Source-generated for the same reason every other contract here is: the published binary is trimmed.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    WriteIndented = false)]
[JsonSerializable(typeof(CliInvocationEntry))]
internal sealed partial class CliInvocationLogJsonContext : JsonSerializerContext;
