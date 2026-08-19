// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Outbox;

/// <summary>One recorded send, with what each of its recipients was told.</summary>
/// <remarks>
/// It is the one outbox reading that names people, and it exists because a decision about a send whose outcome nobody
/// knows cannot be taken without knowing who may already have received it. The message itself is not here: no subject,
/// no body, and no raw MIME is served on this surface at all.
/// </remarks>
/// <param name="OutgoingEmail">The identifier a decision names it by.</param>
/// <param name="Account">The account the message is sent from.</param>
/// <param name="Stage">How far along its submission sequence it has durably reached.</param>
/// <param name="Origin">What asked for the send.</param>
/// <param name="Requester">The identity the send is idempotent under.</param>
/// <param name="AttemptCount">How many attempts have been handed out for it.</param>
/// <param name="MimeByteLength">How many bytes of MIME the deployment stores for the message.</param>
/// <param name="RecordedAt">When the send was written down.</param>
/// <param name="StageChangedAt">When it last moved between stages.</param>
/// <param name="AvailableAt">The instant from which it may be attempted again.</param>
/// <param name="LastFailureCode">The code identifying what the last attempt ended in, absent where the deployment records none.</param>
/// <param name="LastReplyCode">The reply code the server answered with, absent where it answered none.</param>
/// <param name="Recipients">Who the message is offered to, and what each of them was told.</param>
internal sealed record OutboxSend(
    [property: JsonPropertyName("outgoingEmail")] Guid OutgoingEmail,
    [property: JsonPropertyName("account")] string? Account,
    [property: JsonPropertyName("stage")] string? Stage,
    [property: JsonPropertyName("origin")] string? Origin,
    [property: JsonPropertyName("requester")] string? Requester,
    [property: JsonPropertyName("attemptCount")] int AttemptCount,
    [property: JsonPropertyName("mimeByteLength")] long MimeByteLength,
    [property: JsonPropertyName("recordedAt")] DateTimeOffset RecordedAt,
    [property: JsonPropertyName("stageChangedAt")] DateTimeOffset StageChangedAt,
    [property: JsonPropertyName("availableAt")] DateTimeOffset AvailableAt,
    [property: JsonPropertyName("lastFailureCode")] int? LastFailureCode,
    [property: JsonPropertyName("lastReplyCode")] int? LastReplyCode,
    [property: JsonPropertyName("recipients")] IReadOnlyList<OutboxRecipientReading> Recipients);

/// <summary>One person a message is offered to, and what the server said about them.</summary>
/// <param name="Address">The address the envelope names.</param>
/// <param name="Role">Whether the address is on the message as a recipient, a copy, or a blind copy.</param>
/// <param name="Status">What the last attempt settled about it.</param>
/// <param name="LastReplyCode">The reply code the server answered for this address, absent where it answered none.</param>
/// <param name="AnsweredAt">When that answer was recorded, absent where none was.</param>
internal sealed record OutboxRecipientReading(
    [property: JsonPropertyName("address")] string? Address,
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("lastReplyCode")] int? LastReplyCode,
    [property: JsonPropertyName("answeredAt")] DateTimeOffset? AnsweredAt);
