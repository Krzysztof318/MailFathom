// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Transmission;

/// <summary>States how one submission ended, as the three answers a caller acts on differently.</summary>
/// <remarks>
/// <para>
/// A server that refused is not a failure of the attempt: it answered, and what it said decides whether the message is
/// ever offered again. That is why a refusal is an outcome here rather than an exception — an exception is reserved for
/// a server that did not answer at all, which is the case a caller cannot distinguish from a delivery that landed.
/// </para>
/// <para>
/// The two refusals are separated at the source rather than by a caller re-reading a reply code, so nothing above the
/// adapter has to know that RFC 5321 gives the 4yz class to a temporary rejection or that an RFC 3463 enhanced code may
/// contradict it.
/// </para>
/// </remarks>
public enum MailTransmissionOutcome
{
    /// <summary>The server acknowledged the message for every address it had accepted, and nothing more is owed.</summary>
    Accepted = 0,

    /// <summary>The server refused for now, so the same message offered again later may be taken.</summary>
    RefusedTemporarily = 1,

    /// <summary>The server refused for good, so offering the same message again receives the same answer.</summary>
    RefusedPermanently = 2,
}
