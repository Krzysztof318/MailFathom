// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Outbox;

/// <summary>What a deployment is asked when one send is to be withdrawn.</summary>
/// <param name="OutgoingEmail">The identifier the outbox reading reports for the send.</param>
internal sealed record OutboxCancellationRequest(
    [property: JsonPropertyName("outgoingEmail")] Guid OutgoingEmail);

/// <summary>What a deployment is asked when one send is to be offered again.</summary>
/// <param name="OutgoingEmail">The identifier the outbox reading reports for the send.</param>
/// <param name="RefusalRestated">Whether the operator has restated a permanent refusal, which is what a refused send needs before it is offered again.</param>
internal sealed record OutboxRequeueRequest(
    [property: JsonPropertyName("outgoingEmail")] Guid OutgoingEmail,
    [property: JsonPropertyName("refusalRestated")] bool RefusalRestated);

/// <summary>What became of a send the operator decided about.</summary>
/// <param name="OutgoingEmail">The send the decision named.</param>
/// <param name="Outcome">What happened, as the deployment names it.</param>
internal sealed record OutboxDecision(
    [property: JsonPropertyName("outgoingEmail")] Guid OutgoingEmail,
    [property: JsonPropertyName("outcome")] string? Outcome)
{
    /// <summary>The outcome a deployment reports when the decision took effect.</summary>
    internal const string AcceptedOutcome = "Accepted";

    /// <summary>The outcome a deployment reports when it holds no send with the identifier named.</summary>
    internal const string RecordUnknownOutcome = "RecordUnknown";

    /// <summary>The outcome a deployment reports when a delivery attempt holds the send right now.</summary>
    internal const string AttemptUnderWayOutcome = "AttemptUnderWay";

    /// <summary>The outcome a deployment reports when a permanent refusal stands and was not restated.</summary>
    internal const string RefusalNotRestatedOutcome = "RefusalNotRestated";

    /// <summary>Gets whether the decision was the one that took effect.</summary>
    internal bool WasAccepted => string.Equals(this.Outcome, AcceptedOutcome, StringComparison.Ordinal);

    /// <summary>States what a decision that did not take effect means, in terms of what the operator does next.</summary>
    /// <param name="requeueFlag">The option that restates a refusal, named so a refused send says which word to add.</param>
    /// <returns>The sentence to print.</returns>
    /// <remarks>
    /// Every refusal is ordinary rather than exceptional, which is why none of them is a failure of the deployment. A
    /// send it does not hold is most often an identifier from another deployment; a send an attempt holds is a race the
    /// operator waits out; a stage that does not allow the decision is what a listing a few minutes old produces; and a
    /// refusal that was not restated is the one the operator answers by saying so.
    /// </remarks>
    internal string DescribeRefusal(string? requeueFlag = null) => this.Outcome switch
    {
        RecordUnknownOutcome =>
            $"The deployment holds no queued message {this.OutgoingEmail:D}. Read the outbox again: the identifier may belong to another deployment.",
        AttemptUnderWayOutcome =>
            $"A delivery attempt is holding message {this.OutgoingEmail:D} right now, so nothing was changed. Its lease is what frees it; read the outbox again in a moment and decide then.",
        RefusalNotRestatedOutcome when requeueFlag is { Length: > 0 } flag =>
            $"Message {this.OutgoingEmail:D} was permanently refused, so nothing offers it again on its own. Repeat the command with {flag} to say that you mean it.",
        RefusalNotRestatedOutcome =>
            $"Message {this.OutgoingEmail:D} was permanently refused, so nothing offers it again on its own.",
        _ =>
            $"Message {this.OutgoingEmail:D} has moved past the point this decision applies at, so nothing was changed. Read the outbox again to see where it stands.",
    };
}
