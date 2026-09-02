// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Outbox;

/// <summary>One page of what a deployment has been asked to send.</summary>
/// <param name="Sends">The sends, ordered by when each one was written down, newest first.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end.</param>
internal sealed record OutboxPage(
    [property: JsonPropertyName("sends")] IReadOnlyList<OutboxEntryReading> Sends,
    [property: JsonPropertyName("nextCursor")] string? NextCursor);

/// <summary>One recorded send as the listing names it, which is deliberately without its recipients.</summary>
/// <param name="OutgoingEmail">The identifier a decision names it by.</param>
/// <param name="Account">The account the message is sent from.</param>
/// <param name="Stage">How far along its submission sequence it has durably reached.</param>
/// <param name="Origin">What asked for the send.</param>
/// <param name="AttemptCount">How many attempts have been handed out for it.</param>
/// <param name="MimeByteLength">How many bytes of MIME the deployment stores for the message.</param>
/// <param name="RecordedAt">When the send was written down.</param>
/// <param name="StageChangedAt">When it last moved between stages.</param>
/// <param name="AvailableAt">The instant from which it may be attempted again.</param>
/// <param name="LastFailureCode">The code identifying what the last attempt ended in, absent where the deployment records none.</param>
/// <param name="LastReplyCode">The reply code the server answered with, absent where it answered none.</param>
internal sealed record OutboxEntryReading(
    [property: JsonPropertyName("outgoingEmail")] Guid OutgoingEmail,
    [property: JsonPropertyName("account")] string? Account,
    [property: JsonPropertyName("stage")] string? Stage,
    [property: JsonPropertyName("origin")] string? Origin,
    [property: JsonPropertyName("attemptCount")] int AttemptCount,
    [property: JsonPropertyName("mimeByteLength")] long MimeByteLength,
    [property: JsonPropertyName("recordedAt")] DateTimeOffset RecordedAt,
    [property: JsonPropertyName("stageChangedAt")] DateTimeOffset StageChangedAt,
    [property: JsonPropertyName("availableAt")] DateTimeOffset AvailableAt,
    [property: JsonPropertyName("lastFailureCode")] int? LastFailureCode,
    [property: JsonPropertyName("lastReplyCode")] int? LastReplyCode)
{
    /// <summary>Gets whether this is the send that waits for a person rather than for another attempt.</summary>
    internal bool HasUnknownOutcome => OutboxReading.StandsAtUnknownOutcome(this.Stage);

    /// <summary>Describes what the last attempt ended in, as the codes an operator looks up.</summary>
    /// <returns>The failure code and the reply code, or a word saying the deployment recorded neither.</returns>
    internal string DescribeFailure() =>
        OutboxReading.DescribeFailure(this.LastFailureCode, this.LastReplyCode);
}
