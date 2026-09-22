// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Access;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Calendar;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Tasks;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Tasks;

namespace MailFathom.Application.Agent.Answering;

/// <summary>Carries out an act a person accepted, exactly as it was proposed.</summary>
/// <remarks>
/// <para>
/// <strong>Nothing here is new capability.</strong> Every act goes through the use case the client's own routes reach —
/// a draft saved and then sent, an event put on the person's own calendar, a task written onto their own list — so the
/// grant each asks for, the recipients it resolves, the governors that may refuse a send, and the outbox it lands in are
/// the ones a person pressing send in the mail screen meets, and an event or a task arrives asserted exactly as one they
/// typed themselves. The Agent reaches what already exists and nothing more.
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
    private readonly OwnCalendar calendar;
    private readonly OwnTasks tasks;

    /// <summary>Initializes the performer over the use cases every act is carried out through.</summary>
    /// <param name="messageDrafting">Saves a new message as a draft.</param>
    /// <param name="responseDrafting">Saves an answer to a stored message as a draft.</param>
    /// <param name="drafts">Sends a saved draft.</param>
    /// <param name="calendar">Puts an event on the person's own calendar.</param>
    /// <param name="tasks">Writes a task onto the person's own list.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public AgentActPerformer(
        AuthoredMailDrafting messageDrafting,
        AuthoredResponseDrafting responseDrafting,
        UserMailDrafts drafts,
        OwnCalendar calendar,
        OwnTasks tasks)
    {
        ArgumentNullException.ThrowIfNull(messageDrafting);
        ArgumentNullException.ThrowIfNull(responseDrafting);
        ArgumentNullException.ThrowIfNull(drafts);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(tasks);

        this.messageDrafting = messageDrafting;
        this.responseDrafting = responseDrafting;
        this.drafts = drafts;
        this.calendar = calendar;
        this.tasks = tasks;
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
            AgentEventScheduling or AgentTaskRecording => [MailFathomPermission.MailRead],
            _ => throw new ArgumentException("The act is not one this build declares.", nameof(act)),
        };
    }

    /// <summary>Carries out one accepted act.</summary>
    /// <param name="act">The act, exactly as it was proposed.</param>
    /// <param name="proposal">Names the proposal, which keys a sent act so a retried acceptance is recognizably the same request.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns><see langword="true" /> when the act was carried out; <see langword="false" /> when this deployment refused it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="act" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A refusal is an outcome rather than a fault: a recipient the governor refuses, an account no longer served, a
    /// message past a bound, a calendar that would not take the span. Each is what the proposal ends in as failed, and
    /// the person is offered the attempt again.
    /// </remarks>
    public async Task<bool> PerformAsync(AgentProposedAct act, string proposal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(act);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposal);

        try
        {
            return act switch
            {
                AgentMessageSending or AgentResponseSending => await this.SendAsync(act, proposal, cancellationToken),
                AgentEventScheduling scheduling => await this.ScheduleAsync(scheduling, cancellationToken),
                AgentTaskRecording recording => await this.RecordAsync(recording, cancellationToken),
                _ => throw new ArgumentException("The act is not one this build declares.", nameof(act)),
            };
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

    private async Task<bool> SendAsync(AgentProposedAct act, string proposal, CancellationToken cancellationToken)
    {
        var draft = await this.SaveAsync(act, OutgoingEmailRequester.Command(proposal), cancellationToken);

        await this.drafts.SendAsync(draft, cancellationToken);

        return true;
    }

    /// <summary>Puts the proposed date on the calendar, announcing nothing, exactly as the person's own client does.</summary>
    /// <remarks>Every refusal the calendar answers with is the act not being carried out, which is what the proposal then ends as.</remarks>
    private async Task<bool> ScheduleAsync(AgentEventScheduling scheduling, CancellationToken cancellationToken)
    {
        var written = await this.calendar.CreateAsync(
            scheduling.Title.Value,
            scheduling.Start,
            scheduling.End,
            scheduling.IsAllDay,
            reminders: [],
            scheduling.SourceMessage,
            cancellationToken);

        return written.Outcome is CalendarEventWriteOutcome.Written;
    }

    /// <summary>Writes the proposed task onto the person's list as one they owe, announcing nothing.</summary>
    private async Task<bool> RecordAsync(AgentTaskRecording recording, CancellationToken cancellationToken)
    {
        await this.tasks.RecordAsync(
            recording.Title.Value,
            recording.DueOn,
            TaskAnnouncement.Silent,
            recording.SourceMessage,
            cancellationToken);

        return true;
    }

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
