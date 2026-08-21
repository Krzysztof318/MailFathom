// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Mcp.Tools.Results;

namespace MailFathom.Mcp.Tools.Drafts;

/// <summary>Writes one draft a caller described, through whichever use case the shape it described names.</summary>
/// <remarks>
/// <para>
/// A draft is the one authored act that comes in two shapes: a message of its own, and an answer to mail this
/// deployment already holds. The sending surface publishes those as three tools, because a send is irreversible and
/// each of the three is worth annotating and describing separately. A draft is not, so it is one tool per act — write
/// one, edit one, delete one, send one — and this is where the shape a caller described is read and routed to the use
/// case that owns it.
/// </para>
/// <para>
/// It is shared by <c>save_draft</c> and <c>update_draft</c> rather than written twice, because the two differ in one
/// argument and would otherwise come to differ in more: a revision re-authors from the answered email rather than from
/// the previous revision, so an edit that took a different route from the save that preceded it could quietly turn an
/// answer into a message of its own.
/// </para>
/// <para>
/// <b>Nothing here transmits.</b> Both use cases end at the draft book, which holds the message and brings the owner's
/// own drafts folder into step with it. What sends a draft is the promotion, and that asks for the sending grant.
/// </para>
/// </remarks>
/// <param name="drafting">Writes a draft of a message of its own.</param>
/// <param name="answering">Writes a draft of a reply, a reply to all, or a forward.</param>
internal sealed class DraftedMailWriting(AuthoredMailDrafting drafting, AuthoredResponseDrafting answering)
{
    /// <summary>Writes the draft the fields describe, as a new one or as the next version of one that exists.</summary>
    /// <param name="fields">What the caller wrote.</param>
    /// <param name="revises">The draft this replaces, or <see langword="null" /> to write a new one.</param>
    /// <param name="cancellationToken">Cancels the reads and the writes.</param>
    /// <returns>The draft as it stands once the mailbox has been brought into step with it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="fields" /> is <see langword="null" />.</exception>
    /// <exception cref="MailDraftRefusedException">Thrown when the fields describe no draft this system writes, a recipient names nobody, a field cannot be composed, a bound is exceeded, or the draft being revised is not one this account holds.</exception>
    public Task<MailDraftRecord> SaveAsync(
        DraftedMailFields fields,
        MailDraftId? revises,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fields);

        return (fields.AnsweredEmailId, fields.Answers) switch
        {
            (not null, { } answer) => this.SaveAnswerAsync(fields, answer, revises, cancellationToken),
            (null, null) => this.SaveOwnMessageAsync(fields, revises, cancellationToken),

            // One of the pair without the other names no answer: three answers to one stored email reach three
            // different sets of people, and an answer to nothing is a message of its own written the wrong way round.
            _ => throw MailDraftRefusedException.AnsweredEmailAndAnswerDisagree(),
        };
    }

    /// <summary>Writes a draft of a message that answers nothing, which states its own account and subject.</summary>
    private Task<MailDraftRecord> SaveOwnMessageAsync(
        DraftedMailFields fields,
        MailDraftId? revises,
        CancellationToken cancellationToken)
    {
        if (fields.Account is null || fields.Subject is null)
        {
            throw MailDraftRefusedException.MessageNotStated();
        }

        var request = new MailDraftRequest
        {
            Account = NamedAccount(fields.Account),
            Recipients = AuthoredMailArguments.NamedRecipients(
                fields.To,
                fields.Cc,
                fields.Bcc,
                MailDraftRefusedException.TooManyRecipients,
                MailDraftRefusedException.From),
            Subject = fields.Subject,
            PlainTextBody = fields.PlainTextBody,
            HtmlBody = fields.HtmlBody,
            Author = Author(),
            Revises = revises,
        };

        return drafting.SaveAsync(request, cancellationToken);
    }

    /// <summary>Writes a draft of an answer to a stored email, which reads its account and subject from that email.</summary>
    private Task<MailDraftRecord> SaveAnswerAsync(
        DraftedMailFields fields,
        DraftedAnswer answer,
        MailDraftId? revises,
        CancellationToken cancellationToken)
    {
        if (fields.Account is not null || fields.Subject is not null)
        {
            throw MailDraftRefusedException.AnsweredDraftStatesItsOwnMessage();
        }

        var request = new MailResponseDraftRequest
        {
            AnsweredEmailId = AuthoredMailArguments.AnsweredEmail(fields.AnsweredEmailId!),
            Act = AuthoredAct(answer),
            PlainTextBody = fields.PlainTextBody,
            HtmlBody = fields.HtmlBody,
            Recipients = AuthoredMailArguments.NamedRecipients(
                fields.To,
                fields.Cc,
                fields.Bcc,
                MailDraftRefusedException.TooManyRecipients,
                MailDraftRefusedException.From),
            Author = Author(),
            Revises = revises,
        };

        return answering.SaveAsync(request, cancellationToken);
    }

    /// <summary>Reads the account the caller's text names.</summary>
    /// <exception cref="MailDraftRefusedException">Thrown when the text is not one an account could be named by.</exception>
    private static MailAccountSelector NamedAccount(string account) =>
        AuthoredMailArguments.CouldNameAnAccount(account)
            ? MailAccountSelector.Create(account)
            : throw MailDraftRefusedException.AccountNotNamed();

    /// <summary>Reads the authored act the protocol value names.</summary>
    /// <remarks>
    /// Written out rather than cast, so a value added to either enumeration has to be given a counterpart before it can
    /// reach a use case. The refusal is the coded one this surface publishes rather than an argument failure, for the
    /// reason the sending tools give: the SDK's schema binding refuses an unknown name before this is reached, and what
    /// remains is a numeric value outside the set, which is the caller's own input.
    /// </remarks>
    /// <exception cref="MailDraftRefusedException">Thrown when the protocol value names no answer this system declares.</exception>
    private static AuthoredResponseAct AuthoredAct(DraftedAnswer answer) => answer switch
    {
        DraftedAnswer.SenderOnly => AuthoredResponseAct.Reply,
        DraftedAnswer.Everyone => AuthoredResponseAct.ReplyToAll,
        DraftedAnswer.Forward => AuthoredResponseAct.Forward,
        _ => throw MailDraftRefusedException.AnswerUnknown(),
    };

    /// <summary>Names the act writing this draft down, which is provenance rather than an identity to compare.</summary>
    /// <remarks>
    /// A draft carries no idempotency key and takes none from a caller, because asking twice for a draft is two drafts:
    /// the second costs an owner a deletion rather than a recipient a second message, which is exactly the reason the
    /// tool is advertised as one that is not idempotent. So the identity is minted per call and says what it truly is
    /// — one act, distinct from every other — instead of asking a caller for a value nothing here would compare.
    /// </remarks>
    private static OutgoingEmailRequester Author() =>
        OutgoingEmailRequester.Command(Guid.NewGuid().ToString());
}
