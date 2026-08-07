// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Mutations;

/// <summary>States what asking for a mutation did, which is not always what it asked for.</summary>
/// <remarks>
/// Three of the four are outcomes a caller acts on and continues from rather than failures, which is why they are a
/// result rather than exceptions. A change already made, a change nothing will make, and a change whose outcome cannot
/// be established are each an ordinary answer to asking twice.
/// </remarks>
public enum MailboxMutationStatus
{
    /// <summary>This call made the change.</summary>
    Performed = 0,

    /// <summary>The change had already been made for this request, and nothing was issued.</summary>
    /// <remarks>This is what the idempotency identity buys: asking again is answered from the record instead of from the mail server.</remarks>
    AlreadyPerformed = 1,

    /// <summary>A command that must never be issued twice went out and its answer never came back, so nothing was issued.</summary>
    /// <remarks>The record stays where it is and stays visible. Resolving it needs the destination folder to be looked at, which is not something to guess at from here.</remarks>
    OutcomeUnknown = 2,

    /// <summary>The mutation reached its terminal failed stage and will not be attempted again.</summary>
    Abandoned = 3,
}
