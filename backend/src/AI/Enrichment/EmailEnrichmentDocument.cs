// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.AI.Enrichment;

/// <summary>The shape an enrichment agent answers in, before anything of it is believed.</summary>
/// <remarks>
/// It stays inside this boundary and never becomes a mark. Every field is optional and every value is untrusted: what a
/// model wrote is read into this and then validated into <see cref="Application.Emails.Enrichment.EmailEnrichmentMark" />,
/// which is the type the rest of the system works with.
/// </remarks>
internal sealed record EmailEnrichmentDocument
{
    /// <summary>Gets what the model said the message is about.</summary>
    [JsonPropertyName("sense")]
    public EmailEnrichmentMarkDocument? Sense { get; init; }

    /// <summary>Gets what the model said makes the message matter.</summary>
    [JsonPropertyName("significance")]
    public EmailEnrichmentMarkDocument? Significance { get; init; }

    /// <summary>Gets the commitment the model said the message contains.</summary>
    [JsonPropertyName("commitment")]
    public EmailEnrichmentMarkDocument? Commitment { get; init; }
}

/// <summary>One reading as the model wrote it, with nothing about it yet established.</summary>
/// <remarks>
/// The passages are the numbers the turn gave them rather than identifiers, which is why they are integers here: a
/// number outside the range the turn published names no passage and the reading falls away with it.
/// </remarks>
internal sealed record EmailEnrichmentMarkDocument
{
    /// <summary>Gets the reading itself.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>Gets why the model says it.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    /// <summary>Gets the turn's own numbers for the passages the reading rests on.</summary>
    [JsonPropertyName("passages")]
    public IReadOnlyList<int>? Passages { get; init; }

    /// <summary>Gets when the commitment falls due, as the model wrote it.</summary>
    [JsonPropertyName("dueAt")]
    public string? DueAt { get; init; }
}
