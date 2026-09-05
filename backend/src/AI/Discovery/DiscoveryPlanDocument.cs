// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.AI.Discovery;

/// <summary>The shape a planning agent answers in, before anything of it is believed.</summary>
/// <remarks>
/// It stays inside this boundary and never becomes the plan itself. Every field is optional and every value is
/// untrusted: what a model wrote is read into this and then validated into
/// <see cref="Application.Discovery.Planning.DiscoveryRunPlan" />, which is the type the rest of the system
/// works with.
/// </remarks>
internal sealed record DiscoveryPlanDocument
{
    /// <summary>Gets the name of the intent the model read the question as, which may name none this catalogue holds.</summary>
    [JsonPropertyName("intent")]
    public string? Intent { get; init; }

    /// <summary>Gets the number of passages the model judged would answer the question.</summary>
    [JsonPropertyName("sufficientPassages")]
    public int? SufficientPassages { get; init; }

    /// <summary>Gets the lookups the model proposed, in the order it proposed them.</summary>
    [JsonPropertyName("lookups")]
    public IReadOnlyList<DiscoveryLookupDocument>? Lookups { get; init; }
}
