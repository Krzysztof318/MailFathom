// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Mutations;

/// <summary>Bounds how many times one recorded mutation may be attempted before it is given up on.</summary>
public sealed class MailboxMutationOptions
{
    /// <summary>Gets or sets how many attempts one mutation may spend before it reaches its terminal failed stage.</summary>
    /// <remarks>
    /// <para>
    /// The bound exists so a change that cannot be made becomes visible instead of pending forever. A mail server that
    /// is refusing, a destination folder somebody deleted, or a message another client already removed are all failures
    /// that will fail identically tomorrow, and repeating them costs a login and a round trip each time while hiding the
    /// problem behind an operation that always looks busy.
    /// </para>
    /// <para>
    /// It counts attempts of the whole mutation rather than retries inside one. The account's resilience pipeline
    /// already bounds a single command, so this is the outer bound over separate runs, each of which may be minutes or
    /// days apart. The count survives a crash because it is written before the attempt, which is what makes an attempt
    /// that kills the process count against it.
    /// </para>
    /// </remarks>
    public int MaximumAttempts { get; set; } = 5;
}
