// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.ThreadStates;

/// <summary>Reads the state that was derived about one conversation.</summary>
/// <remarks>
/// <para>
/// The port is read-only and joins no transaction, per
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0001-application-owned-repositories-for-persistence-ports.md">ADR 0001</see>.
/// </para>
/// <para>
/// A read beside the conversation rather than a join onto it, for the reason a message's marks are one: the state lives
/// in tables of its own, it is sparse — a deployment with the derivation off has none at all — and reaching it by
/// identity keeps the thread query the bounded projection it was.
/// </para>
/// </remarks>
public interface IStoredThreadStateReader
{
    /// <summary>Reads the state of one conversation the scope admits.</summary>
    /// <param name="threadId">The conversation, which may be one a merge has since folded into another.</param>
    /// <param name="scope">The accounts and folders configuration admits.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>
    /// The state, or <see langword="null" /> where no derivation has reached the conversation — which is what a client
    /// draws as not derived yet rather than as nothing to say. A conversation the scope admits no message of also reads
    /// as nothing, so a state is never published for an exchange this caller may not see.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    Task<EmailThreadState?> ReadStateAsync(
        EmailThreadId threadId,
        MailboxScope scope,
        CancellationToken cancellationToken);
}
