// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Delivery.Screening;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery.Drafts;

namespace MailFathom.Application.Mail.Delivery.Drafts;

/// <summary>Answers which drafts the owner in hand is writing, and resolves one of them by identity.</summary>
/// <remarks>
/// <para>
/// It exists because a person composing mail needs their drafts back after closing the window, and because the acts
/// beneath it — revising a draft, giving one up, keeping one on the server, attaching a file to one — name a draft by
/// an identifier that says nothing about whose it is. <see cref="MailDraftBook" /> admits those acts on the grant
/// alone, which is what a deployment holding one owner needs and not what an owner-facing surface may rely on, so this
/// is where an identifier becomes a draft the caller's own owner holds.
/// </para>
/// <para>
/// <b>A draft another owner holds answers exactly as one nobody holds.</b> There is no listing that crosses owners, no
/// refusal that separates the two cases, and no timing that does either, which is the same rule every owner-facing
/// read here follows.
/// </para>
/// <para>
/// It is published under <see cref="MailFathomPermission.MailDraftsWrite" /> rather than under
/// <see cref="MailFathomPermission.MailRead" />, which is the same choice the outgoing reader makes: what a draft says
/// is what this caller was allowed to compose, and a credential granted to read a mailbox has not been granted to read
/// the outgoing correspondence being prepared in it.
/// </para>
/// <para>
/// The answer is bounded and carries no cursor. Drafts are what one person has open at a time rather than a corpus, so
/// a bound a screen cannot exhaust is the whole of what a listing needs — and a cursor into somebody's composition
/// would be a walk over their unsent mail rather than a page of it.
/// </para>
/// </remarks>
/// <param name="accountCatalog">Says which accounts the caller's owner owns, and therefore which one a caller may name.</param>
/// <param name="drafts">Holds the durable account of every draft.</param>
/// <param name="contentStore">Holds the composed MIME each revision is.</param>
/// <param name="text">Reads back what a composed message says, so an author gets their own words to go on editing.</param>
/// <param name="authorization">Answers whether the caller that reached this holds the grant that lets it draft.</param>
public sealed class MailDraftDirectory(
    ICallerMailAccountCatalog accountCatalog,
    IMailDraftStore drafts,
    IEmailContentStore contentStore,
    IOutgoingMailTextReader text,
    AccessAuthorization authorization)
{
    /// <summary>The greatest number of drafts one reading answers with.</summary>
    /// <remarks>
    /// An owner standing at it has more drafts open than any composing screen shows, which is a state to report rather
    /// than to page through: what resolves it is finishing or giving up what is already written.
    /// </remarks>
    public const int MaximumCount = 200;

    /// <summary>Reads the drafts the caller's owner is writing, newest edit first.</summary>
    /// <param name="account">The account to narrow to, or <see langword="null" /> for every account this owner owns.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The drafts, at most <see cref="MaximumCount" /> of them, empty where the owner is writing none.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.MailDraftsWrite" />, or is acting for no owner.</exception>
    /// <exception cref="MailAccountNotAccessibleException">Thrown when <paramref name="account" /> names an account the caller's owner does not own, which includes every account this deployment does not serve.</exception>
    public async Task<IReadOnlyList<MailDraftRecord>> ReadAsync(
        MailAccountSelector? account,
        CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailDraftsWrite);

        var narrowed = account is { } named
            ? (accountCatalog.OwnedAccounts.FirstOrDefault(owned => owned.IsNamedBy(named))
                ?? throw new MailAccountNotAccessibleException(named)).Id
            : (MailAccountId?)null;

        return await drafts.ReadForOwnerAsync(
            accountCatalog.Owner,
            narrowed,
            MaximumCount,
            cancellationToken);
    }

    /// <summary>Reads one draft of the caller's own owner, or answers that they hold none under that identifier.</summary>
    /// <param name="draftId">The draft to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The draft, or <see langword="null" /> when this owner holds none under that identifier.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.MailDraftsWrite" />, or is acting for no owner.</exception>
    /// <remarks>
    /// Every act an owner-facing surface takes on a draft passes through this first, which is what keeps an identifier
    /// from reaching a book that acts on whatever it is handed. The owner is compared against the draft's own recorded
    /// owner rather than against the accounts listing, so an account withdrawn from the record since the draft was
    /// written still resolves to the person who wrote it.
    /// </remarks>
    public async Task<MailDraftRecord?> FindAsync(MailDraftId draftId, CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailDraftsWrite);

        var draft = await drafts.FindAsync(draftId, cancellationToken);

        return draft is not null && draft.Account.Owner == accountCatalog.Owner ? draft : null;
    }

    /// <summary>Reads one of the caller's own drafts back as the words its author wrote, so editing can go on.</summary>
    /// <param name="draftId">The draft to open.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The draft and what its stored message says, or <see langword="null" /> when this owner holds no such draft.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.MailDraftsWrite" />, or is acting for no owner.</exception>
    /// <remarks>
    /// <para>
    /// The one reading here that loads a message, and it is asked for one draft by identity because that is what
    /// opening a draft is. The listing beside it loads none, which is why the subject and the recipients live on the
    /// row: a screen showing what somebody is writing costs one query, and the bytes are read when they open one.
    /// </para>
    /// <para>
    /// The words come out of the stored message rather than out of a second copy of them, so what an author sees is
    /// exactly what would be sent. A draft whose message is missing answers as one this owner does not hold: the record
    /// and its message are written in one transaction, so a record without one describes a draft nothing could send.
    /// </para>
    /// </remarks>
    public async Task<MailDraftReading?> ReadComposedAsync(
        MailDraftId draftId,
        CancellationToken cancellationToken)
    {
        if (await this.FindAsync(draftId, cancellationToken) is not { } draft)
        {
            return null;
        }

        var content = await contentStore.FindMailDraftContentAsync(draftId, cancellationToken);

        if (content is null || content.RawMime.IsEmpty)
        {
            return null;
        }

        return new MailDraftReading(draft, await text.ReadAsync(content.RawMime, cancellationToken));
    }
}
