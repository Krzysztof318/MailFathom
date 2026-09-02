// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Spam.Actions;

/// <summary>Says whether what a verdict asks of the mailbox is written down or only worked out.</summary>
/// <remarks>
/// <para>
/// The whole decision is taken either way: the switches are read, the destination is resolved, and every reason to leave
/// a message alone is reached in the same order. What the posture governs is the last step alone, which is whether a
/// durable mutation record is opened — so a dry run and a run that acts answer the same question about the same message
/// and differ only in whether anything reaches the mail server.
/// </para>
/// <para>
/// <see cref="DryRun" /> is the zero value deliberately. With filing switched on, acting over a whole mailbox is the
/// largest single thing this feature can do to somebody's mail, so a value nobody set must never be the one that does
/// it.
/// </para>
/// </remarks>
public enum SpamActionPosture
{
    /// <summary>Work out what the switches ask for and write none of it down.</summary>
    DryRun = 0,

    /// <summary>Open the durable record of each change, for the account's convergence pass to carry out.</summary>
    Acting = 1,
}
