// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.AI.ContactRelationships;

/// <summary>Reads a relationship agent's answer without reflection, as everything serialized in this repository is.</summary>
/// <remarks>
/// The options are deliberately forgiving about case and about a field nobody declared, because the text being read was
/// written by a model rather than by a program. What they are not forgiving about is what the values mean: that is
/// <see cref="ContactRelationshipReading" />'s to decide against the card's own bounds.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(ContactRelationshipDocument))]
internal sealed partial class ContactRelationshipJsonContext : JsonSerializerContext;
