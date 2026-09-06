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

    /// <summary>Gets the instant mail must have been received at or after.</summary>
    [JsonPropertyName("receivedOnOrAfter")]
    public DateTimeOffset? ReceivedOnOrAfter { get; init; }

    /// <summary>Gets the instant mail must have been received before.</summary>
    [JsonPropertyName("receivedBefore")]
    public DateTimeOffset? ReceivedBefore { get; init; }

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
