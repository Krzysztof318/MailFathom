// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Filing;

/// <summary>States how one attempt to file a copy of an outgoing message ended.</summary>
/// <remarks>
/// None of these is a delivery outcome, and that separation is the point. A message is delivered or it is not; where a
/// copy of it ended up is a second account of the same message, and every member here leaves a completed delivery
/// exactly as completed as it was.
/// </remarks>
public enum OutgoingMailFilingOutcome
{
    /// <summary>The folder accepted the copy.</summary>
    Filed = 0,

    /// <summary>A copy was already in the folder from an earlier attempt, so nothing was appended.</summary>
    AlreadyFiled = 1,

    /// <summary>The account asked for no copy in this place, so nothing was attempted.</summary>
    /// <remarks>
    /// It covers an account that switched the sent copy off and a deployment that mapped no folder to the outbox role,
    /// which are the same thing to read: this deployment does not want a copy here, and that is not a failure.
    /// </remarks>
    NotRequested = 2,

    /// <summary>There is no folder to put the copy into, and the account's folder mapping is what changes that.</summary>
    DestinationUnavailable = 3,

    /// <summary>The append went out and the server's answer never came back, so nobody can say whether the copy is there.</summary>
    /// <remarks>
    /// The one outcome that is never attempted again. A second append is a second message in the owner's folder, and
    /// nothing the folder shows afterwards tells the two apart.
    /// </remarks>
    OutcomeUnknown = 4,

    /// <summary>The attempt failed before anything reached the folder, and may be attempted again on its own.</summary>
    Failed = 5,

    /// <summary>The copy has been taken back out of the folder, or was already gone from it.</summary>
    /// <remarks>
    /// It is its own member rather than <see cref="Filed" /> read backwards, because both endings are counted: a
    /// dashboard reading how many copies this deployment put into somebody's mailbox must not have withdrawals added
    /// into that number.
    /// </remarks>
    Withdrawn = 6,
}
