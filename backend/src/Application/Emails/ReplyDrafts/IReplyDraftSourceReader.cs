// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.ReplyDrafts;

/// <summary>Reads everything one drafting is written from, out of the mail this deployment already holds.</summary>
/// <remarks>
/// <para>
/// The port is read-only and joins no transaction, per
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0001-application-owned-repositories-for-persistence-ports.md">ADR 0001</see>.
/// </para>
/// <para>
/// One read rather than two, and that is the control rather than a convenience: the style is derived from the sent mail
/// of the account the answered message belongs to, and asking for it separately would make the account a caller states
/// rather than one the answered message decides. A reply drafted in one person's manner out of another account's mail
/// is the failure this shape rules out by construction.
/// </para>
/// </remarks>
public interface IReplyDraftSourceReader
{
    /// <summary>Reads the conversation of one message the scope admits, with the manner its own account writes in.</summary>
    /// <param name="answeredEmailId">The message being answered, whose conversation and account decide everything read here.</param>
    /// <param name="scope">The accounts and folders configuration admits.</param>
    /// <param name="bounds">How much of the conversation and how much of the account's sent mail travels.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// What the drafting is written from, or <see langword="null" /> where this scope holds no such message — which is
    /// what a caller answers exactly as it answers a message nobody holds, so nothing here reports that somebody
    /// else's correspondence exists.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> or <paramref name="bounds" /> is <see langword="null" />.</exception>
    Task<ReplyDraftSources?> ReadSourcesAsync(
        StoredEmailId answeredEmailId,
        MailboxScope scope,
        ReplyDraftBounds bounds,
        CancellationToken cancellationToken);
}
