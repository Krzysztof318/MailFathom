// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.AI.Discovery;

/// <summary>One proposed lookup as the model wrote it, mirroring the filters retrieval already understands.</summary>
internal sealed record DiscoveryLookupDocument
{
    /// <summary>Gets the words the mail itself is expected to carry.</summary>
    [JsonPropertyName("queryText")]
    public string? QueryText { get; init; }

    /// <summary>Gets the address mail must have been sent from.</summary>
    [JsonPropertyName("senderAddress")]
    public string? SenderAddress { get; init; }

    /// <summary>Gets the address mail must have been addressed to.</summary>
    [JsonPropertyName("recipientAddress")]
    public string? RecipientAddress { get; init; }

    /// <summary>Gets the text a subject must contain.</summary>
    [JsonPropertyName("subjectFragment")]
    public string? SubjectFragment { get; init; }

    /// <summary>Gets the wall clock mail must have been received at or after, as the model wrote it.</summary>
    /// <remarks>
    /// Carried as text rather than as a <see cref="DateTimeOffset" /> so that nothing supplies a zone on the model's
    /// behalf. What it writes is a wall clock against the anchor its turn stated, and only the reading knows whose
    /// wall clock that is; a binder given the same characters would answer an instant belonging to nobody in this
    /// deployment. <see cref="Orchestration.AnchoredInstant" /> is where it becomes one.
    /// </remarks>
    [JsonPropertyName("receivedOnOrAfter")]
    public string? ReceivedOnOrAfter { get; init; }

    /// <summary>Gets the wall clock mail must have been received before, as the model wrote it.</summary>
    /// <remarks>Read exactly as <see cref="ReceivedOnOrAfter" /> is, and for the same reason.</remarks>
    [JsonPropertyName("receivedBefore")]
    public string? ReceivedBefore { get; init; }

    /// <summary>Gets the read state mail must carry.</summary>
    [JsonPropertyName("isRemotelySeen")]
    public bool? IsRemotelySeen { get; init; }

    /// <summary>Gets the flagged state mail must carry.</summary>
    [JsonPropertyName("isRemotelyFlagged")]
    public bool? IsRemotelyFlagged { get; init; }

    /// <summary>Gets the keyword mail must carry.</summary>
    [JsonPropertyName("keyword")]
    public string? Keyword { get; init; }

    /// <summary>Gets whether mail must carry attachments.</summary>
    [JsonPropertyName("hasAttachments")]
    public bool? HasAttachments { get; init; }
}
