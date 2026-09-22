// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Access;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Agent.Answering;

/// <summary>Carries out an act a person accepted, exactly as it was proposed.</summary>
/// <remarks>
/// <para>
/// <strong>Nothing here is new capability.</strong> Every act goes through the use case the client's own routes reach —
/// a draft saved, then sent — so the grant each asks for, the recipients it resolves, the governors that may refuse a
/// send, and the outbox it lands in are the ones a person pressing send in the mail screen meets. The Agent reaches what
/// already exists and nothing more.
/// </para>
/// <para>
/// It runs under the principal that accepted, never under the one the run composed under: accepting is the person's own
/// act, so the grant the send needs is theirs at that moment rather than whatever it was when the proposal was written.
/// </para>
/// </remarks>
public sealed class AgentActPerformer : IAgentActPerformer
{
    private readonly AuthoredMailDrafting messageDrafting;
    private readonly AuthoredResponseDrafting responseDrafting;
    private readonly UserMailDrafts drafts;

    /// <summary>Initializes the performer over the use cases every act is carried out through.</summary>
    /// <param name="messageDrafting">Saves a new message as a draft.</param>
    /// <param name="responseDrafting">Saves an answer to a stored message as a draft.</param>
    /// <param name="drafts">Sends a saved draft.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public AgentActPerformer(
        AuthoredMailDrafting messageDrafting,
        AuthoredResponseDrafting responseDrafting,
        UserMailDrafts drafts)
    {
        ArgumentNullException.ThrowIfNull(messageDrafting);
        ArgumentNullException.ThrowIfNull(responseDrafting);
        ArgumentNullException.ThrowIfNull(drafts);

        this.messageDrafting = messageDrafting;
        this.responseDrafting = responseDrafting;
        this.drafts = drafts;
    }

    /// <summary>Gets the grants a person has to hold for an act to be carried out on their acceptance.</summary>
    /// <param name="act">The act.</param>
    /// <returns>Every grant the act's use cases ask for.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="act" /> is <see langword="null" />.</exception>
    /// <remarks>Stated here so an acceptance the grant cannot carry out is refused before it is recorded, rather than recorded and then failed.</remarks>
    public static IReadOnlyList<MailFathomPermission> PermissionsFor(AgentProposedAct act)
    {
        ArgumentNullException.ThrowIfNull(act);

        return act switch
        {
            AgentMessageSending => [MailFathomPermission.MailDraftsWrite, MailFathomPermission.MailSend],
            AgentResponseSending => [MailFathomPermission.MailRead, MailFathomPermission.MailDraftsWrite, MailFathomPermission.MailSend],
            _ => throw new ArgumentException("The act is not one this build declares.", nameof(act)),
        };
    }

    /// <summary>Carries out one accepted act.</summary>
    /// <param name="act">The act, exactly as it was proposed.</param>
    /// <param name="proposal">Names the proposal, which keys the act so a retried acceptance is recognizably the same request.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns><see langword="true" /> when the act was carried out; <see langword="false" /> when this deployment refused it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="act" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A refusal is an outcome rather than a fault: a recipient the governor refuses, an account no longer served, a
    /// message past a bound. Each is what the proposal ends in as failed, and the person is offered the attempt again.
    /// </remarks>
    public async Task<bool> PerformAsync(AgentProposedAct act, string proposal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(act);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposal);

        try
        {
            var draft = await this.SaveAsync(act, OutgoingEmailRequester.Command(proposal), cancellationToken);

            await this.drafts.SendAsync(draft, cancellationToken);

            return true;
        }
        catch (MailFathomException refusal) when (refusal is not PrincipalNotAuthorizedException)
        {
            return false;
        }
    }

    /// <summary>Names one proposal as the key an act carried out on its acceptance is requested under.</summary>
    /// <param name="conversation">The conversation holding the proposal.</param>
    /// <param name="proposedAt">The place the proposal was written at.</param>
    /// <returns>The key.</returns>
    public static string KeyOf(AgentConversationId conversation, long proposedAt) =>
        string.Create(CultureInfo.InvariantCulture, $"agent@{conversation.Value:D}#{proposedAt}");

    private async Task<MailDraftId> SaveAsync(
        AgentProposedAct act,
        OutgoingEmailRequester author,
        CancellationToken cancellationToken)
    {
        var record = act switch
        {
            AgentMessageSending message => await this.messageDrafting.SaveAsync(
                new MailDraftRequest
                {
                    Account = MailAccountSelector.For(message.AccountId),
                    Recipients = RecipientsOf(message.Recipients),
                    Subject = message.Subject.Value,
                    PlainTextBody = message.Body.Value,
                    Author = author,
                },
                cancellationToken),
            AgentResponseSending response => await this.responseDrafting.SaveAsync(
                new MailResponseDraftRequest
                {
                    AnsweredEmailId = response.AnsweredEmailId,
                    Act = response.Act,
                    Recipients = RecipientsOf(response.Recipients),
                    PlainTextBody = response.Body.Value,
                    Author = author,
                },
                cancellationToken),
            _ => throw new ArgumentException("The act is not one this build declares.", nameof(act)),
        };

        return record.Id;
    }

    private static IReadOnlyList<NamedRecipient> RecipientsOf(IReadOnlyList<EmailAddress> recipients) =>
        [.. recipients.Select(static recipient =>
            NamedRecipient.AtAddress(OutgoingRecipientRole.To, recipient.Address, recipient.DisplayName))];
}
