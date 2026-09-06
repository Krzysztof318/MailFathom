// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.AI.Discovery;

/// <summary>Reads a composing agent's answer without reflection, as everything serialized in this repository is.</summary>
/// <remarks>
/// Separate from the planning context rather than a second serializable type on it, because the two readings happen at
/// opposite ends of a run and neither has any business knowing the other's shape. The options are forgiving in exactly
/// the same way and for the same reason: the text was written by a model, so case and an undeclared field are not what
/// a reading refuses. What the values mean is <see cref="DiscoveryCompositionReading" />'s to decide against the
/// contract's own bounds.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(DiscoveryResultDocument))]
internal sealed partial class DiscoveryResultJsonContext : JsonSerializerContext;
