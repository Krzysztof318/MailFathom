// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;

namespace MailFathom.Application.Mail.Delivery.Drafts;

/// <summary>Takes a message somebody wrote and holds it as a draft, which is the whole of what asking to draft does.</summary>
/// <remarks>
/// <para>
/// It is the sibling of <see cref="Submission.AuthoredMailSubmission" /> and composes the same three steps in the same
/// order: the account a caller named is resolved against the accounts this deployment serves, the people named become
/// addresses, and the addresses and the text become MIME. What replaces the outbox at the end is the draft book, which
/// stores the message instead of queueing it.
/// </para>
/// <para>
/// <b>Nothing here transmits, and nothing here can.</b> The use case holds no delivery session and no factory for one,
/// exactly as the submission does not — and unlike the submission it leaves nothing a delivery pass will ever claim.
/// A draft leaves this deployment only when somebody asks for it to, through the promotion.
/// </para>
/// <para>
/// The message is composed against
/// <see cref="MailDeliveryCapabilities.BeforeAnyServerHasSpoken" /> for the reason a send is, and more plainly: a draft
/// may never be sent at all, so there is no server to ask and the composition is held to what stays correct whatever a
/// server would turn out to say. What that server does decide is asked again where the promotion writes the send.
/// </para>
/// </remarks>
/// <param name="accountCatalog">Says which accounts this deployment serves, and therefore which one a caller may name.</param>
/// <param name="recipientResolver">Turns the people the author named into addresses, asking the contact book for the ones named as somebody.</param>
/// <param name="composer">Builds the MIME, and decides every header this system owns rather than the author.</param>
/// <param name="drafts">Writes the record and the message down together, and brings the drafts folder into step.</param>
/// <param name="authorization">Answers whether the caller that reached this holds the grant that lets it draft.</param>
public sealed class AuthoredMailDrafting(
    IDeploymentMailAccountCatalog accountCatalog,
    NamedRecipientResolver recipientResolver,
    IAuthoredEmailComposer composer,
    MailDraftBook drafts,
    AccessAuthorization authorization)
{
    /// <summary>Holds one message as a draft, or refuses it naming what the caller has to change.</summary>
    /// <param name="request">The draft that was asked for.</param>
    /// <param name="cancellationToken">Cancels the reads and the writes.</param>
    /// <returns>The draft as it stands once the mailbox has been brought into step with it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.MailDraftsWrite" />.</exception>
    /// <exception cref="MailAccountNotAccessibleException">Thrown when the request names an account this deployment does not serve.</exception>
    /// <exception cref="MailDraftRefusedException">Thrown when a recipient names nobody, a field cannot be composed, a bound is exceeded, the account configures no address to send from, or the draft being revised is not one of this account's.</exception>
    public async Task<MailDraftRecord> SaveAsync(
        MailDraftRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        authorization.RequirePermission(MailFathomPermission.MailDraftsWrite);

        var account = accountCatalog.ServedAccounts.FirstOrDefault(served => served.IsNamedBy(request.Account))
            ?? throw new MailAccountNotAccessibleException(request.Account);

        // Ahead of the resolution rather than left to it, because the reads it performs carry what the caller supplied
        // and because a list this long describes a draft no promotion could ever write a record for.
        if (request.Recipients.Count > OutgoingEmailRequest.MaximumRecipientCount)
        {
            throw MailDraftRefusedException.TooManyRecipients();
        }

        var resolution = await recipientResolver.ResolveAsync(request.Recipients, cancellationToken);

        if (resolution.Refusal is { } recipientRefusal)
        {
            throw MailDraftRefusedException.From(recipientRefusal);
        }

        var authored = new AuthoredEmail
        {
            Recipients = resolution.Recipients,
            Subject = request.Subject,
            PlainTextBody = request.PlainTextBody,
            HtmlBody = request.HtmlBody,
        };

        var composition = composer.ComposeDraft(
            account.Id,
            authored,
            MailDeliveryCapabilities.BeforeAnyServerHasSpoken);

        if (composition.Draft is not { } composed)
        {
            throw MailDraftRefusedException.From(composition.Refusal!);
        }

        return await drafts.SaveAsync(
            account.Id,
            request.Author,
            composed,
            request.Revises,
            cancellationToken);
    }
}
