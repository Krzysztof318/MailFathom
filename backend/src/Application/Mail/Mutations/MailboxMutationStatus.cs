// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Mutations;

/// <summary>States what asking for a mutation did, which is not always what it asked for.</summary>
/// <remarks>
/// All but one are outcomes a caller acts on and continues from rather than failures, which is why they are a result
/// rather than exceptions. A change already made, a change nothing will make, a change whose outcome cannot be
/// established, and a change somebody withdrew are each an ordinary answer to asking.
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

    /// <summary>The change was withdrawn before any command went out, so nothing was issued and nothing will be.</summary>
    /// <remarks>
    /// A convergence pass never reads a withdrawn record, so this is reached only where one was withdrawn between that
    /// read and this attempt. It is answered rather than performed for the reason the withdrawal existed: the person
    /// who asked for the change has since said they do not want it.
    /// </remarks>
    Withdrawn = 4,
}
