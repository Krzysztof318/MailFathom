// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.AI.ReplyDrafts;

/// <summary>The shape a reply-drafting agent answers in, before anything of it is believed.</summary>
/// <remarks>
/// It stays inside this boundary and never becomes a draft. Every field is optional and every value is untrusted: what
/// a model wrote is read into this and then validated into
/// <see cref="Application.Emails.ReplyDrafts.ReplyDraft" />, which is the type the rest of the system works with.
/// </remarks>
internal sealed record ReplyDraftDocument
{
    /// <summary>Gets the reply the model wrote.</summary>
    [JsonPropertyName("body")]
    public string? Body { get; init; }

    /// <summary>Gets what the model said the reply asserts, with the messages it said back each one.</summary>
    [JsonPropertyName("claims")]
    public IReadOnlyList<ReplyDraftClaimDocument?>? Claims { get; init; }

    /// <summary>Gets the turn's own numbers for the people the model proposes the reply reaches.</summary>
    [JsonPropertyName("recipients")]
    public IReadOnlyList<int>? Recipients { get; init; }
}

/// <summary>One assertion as the model wrote it, with nothing about it yet established.</summary>
/// <remarks>
/// The messages are the numbers the turn gave them rather than identifiers, which is why they are integers here. An
/// absent array and an empty one mean the same thing and are the answer the instruction asks for where the
/// correspondence backs nothing: the claim survives and is marked, because it is already in the body either way.
/// </remarks>
internal sealed record ReplyDraftClaimDocument
{
    /// <summary>Gets the assertion itself.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>Gets the turn's own numbers for the messages the model said state it.</summary>
    [JsonPropertyName("messages")]
    public IReadOnlyList<int>? Messages { get; init; }
}
