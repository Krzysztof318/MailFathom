// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;

namespace MailFathom.Application.Mail.Delivery.Drafts;

/// <summary>Performs the acts an owner takes on one of their own drafts: keeping it, giving it up, and sending it.</summary>
/// <remarks>
/// <para>
/// It is the owner's counterpart of <see cref="MailDraftBook" /> and <see cref="MailDraftPromotion" />, exactly as
/// <see cref="Tracking.OwnerOutbox" /> is the owner's counterpart of the administrative outbox, and it differs from
/// them in one thing: whose drafts each act may reach. Those two admit an act on the grant alone, which is what a
/// deployment holding one owner needs and not what an owner-facing surface may rely on; this is where an identifier
/// becomes a draft the caller's own owner holds before any of them is asked to act on it.
/// </para>
/// <para>
/// <b>A draft another owner holds answers exactly as one nobody holds.</b> There is no refusal that separates the two
/// cases and no timing that does either, which is the rule every owner-facing read here follows. Writing a draft is
/// already scoped without this, because a save names the account it belongs to and that name is resolved against the
/// accounts the caller's owner owns; what needed scoping is every act that names a draft and nothing else.
/// </para>
/// <para>
/// Each act asks for its own grant rather than for one grant covering all three. Giving a draft up is writing it,
/// keeping one in the owner's folder reaches their mail server, and sending one puts a message in somebody else's
/// mailbox — three different powers, and a credential holding one of them has not been granted the others.
/// </para>
/// </remarks>
/// <param name="accountCatalog">Says which owner the caller is acting for, which is what a draft is scoped against.</param>
/// <param name="drafts">Holds the durable account of every draft.</param>
/// <param name="book">Performs the two acts on the draft itself, in the one place each is decided.</param>
/// <param name="promotion">Turns a draft into an ordinary send, in the one place that is decided.</param>
/// <param name="authorization">Answers whether the caller that reached this holds the grant the act it asked for needs.</param>
public sealed class OwnerMailDrafts(
    ICallerMailAccountCatalog accountCatalog,
    IMailDraftStore drafts,
    MailDraftBook book,
    MailDraftPromotion promotion,
    AccessAuthorization authorization)
{
    /// <summary>Gives up one of this owner's drafts, and takes the copies of it back out of their folder.</summary>
    /// <param name="draftId">The draft to give up.</param>
    /// <param name="cancellationToken">Cancels the reads and the writes.</param>
    /// <returns>What became of the copies the mailbox held.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.MailDraftsWrite" />, or is acting for no owner.</exception>
    /// <exception cref="MailDraftRefusedException">Thrown when this owner holds no draft under that identifier, or the draft has already been promoted to a send.</exception>
    public async Task<MailDraftFilingResult> DiscardAsync(
        MailDraftId draftId,
        CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailDraftsWrite);

        await this.RequireOwnAsync(draftId, cancellationToken);

        return await book.DiscardAsync(draftId, cancellationToken);
    }

    /// <summary>Queues one of this owner's drafts for delivery, which is the only act here that reaches anybody else.</summary>
    /// <param name="draftId">The draft to send.</param>
    /// <param name="cancellationToken">Cancels the reads and the write.</param>
    /// <returns>The durable record the message was written down as.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.MailSend" />, or is acting for no owner.</exception>
    /// <exception cref="MailDraftRefusedException">Thrown when this owner holds no draft under that identifier, when the draft names nobody to send it to, or when the stored message exceeds what this deployment sends.</exception>
    /// <exception cref="Governance.OutgoingMailRefusedException">Thrown when a recipient, a ceiling, or the account's own posture refuses the send.</exception>
    public async Task<OutgoingEmailRecord> SendAsync(
        MailDraftId draftId,
        CancellationToken cancellationToken)
    {
        authorization.RequirePermission(MailFathomPermission.MailSend);

        await this.RequireOwnAsync(draftId, cancellationToken);

        return await promotion.PromoteAsync(draftId, cancellationToken);
    }

    /// <summary>Requires that the identifier names a draft the caller's own owner holds.</summary>
    /// <remarks>
    /// The owner is compared against the draft's own recorded owner rather than against the accounts listing, so an
    /// account withdrawn from the record since the draft was written still resolves to the person who wrote it. What
    /// state the draft is in is not judged here at all: each act has its own answer to a draft already given up or
    /// already promoted, and this settles only whose draft it is.
    /// </remarks>
    private async Task RequireOwnAsync(MailDraftId draftId, CancellationToken cancellationToken)
    {
        if (await drafts.FindAsync(draftId, cancellationToken) is not { } draft
            || draft.Account.Owner != accountCatalog.Owner)
        {
            throw MailDraftRefusedException.NotFound();
        }
    }
}
