// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Outbox;

/// <summary>States how one delivery attempt ended, as the outcomes an operator and a pass act on differently.</summary>
/// <remarks>
/// The set separates the endings by what happens next rather than by what went wrong. A send that will be attempted
/// again, a send nothing will attempt again, and a send whose fate nobody knows are three different things to whoever
/// is waiting for the message, and collapsing any two of them would hide the one case that needs a person.
/// </remarks>
public enum MailOutboxDeliveryOutcome
{
    /// <summary>The server acknowledged the message, and every address it had accepted is settled.</summary>
    Sent = 0,

    /// <summary>Nothing offers the message again, and the reason is on the record.</summary>
    /// <remarks>It covers a permanent refusal and a send that spent every attempt it was allowed.</remarks>
    Refused = 1,

    /// <summary>Nothing was transmitted, and the send is claimable again once its backoff has passed.</summary>
    Deferred = 2,

    /// <summary>The message went out and the server's answer never came back, so the send waits for a person.</summary>
    OutcomeUnknown = 3,

    /// <summary>The host stopped before anything was transmitted, so the send was given back holding no attempt.</summary>
    ReleasedForShutdown = 4,

    /// <summary>The record had moved on to a later attempt, so this one recorded nothing about it.</summary>
    LeaseLost = 5,
}
