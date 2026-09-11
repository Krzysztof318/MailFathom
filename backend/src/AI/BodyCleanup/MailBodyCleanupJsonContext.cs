// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.AI.BodyCleanup;

/// <summary>Reads a body-cleanup agent's answer without reflection, as everything serialized in this repository is.</summary>
/// <remarks>
/// <para>
/// Forgiving about case, a comment, and a trailing comma, because the text was written by a model rather than by a
/// program — and about nothing else.
/// </para>
/// <para>
/// <b>A member nothing here declares is refused rather than ignored</b>, which is the one place this disagrees with every
/// other answer read in this product. Elsewhere an undeclared member is a later build's and is skipped; here it is the
/// one observable sign that a model answered with something other than a partition of block numbers — a block's text
/// beside its range, a reason, a rewritten line — and this rendering's whole promise is that no word of it came from a
/// model. So the answer is discarded and the reader is shown the uncleaned message.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(MailBodyCleanupDocument))]
internal sealed partial class MailBodyCleanupJsonContext : JsonSerializerContext;
