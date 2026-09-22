// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Agent.Conversations;

/// <summary>The act a proposal stands for, stated exactly enough to be carried out without asking a model again.</summary>
/// <remarks>
/// <para>
/// <strong>What is accepted is what is carried out.</strong> A block is what a person reads, and it leaves out what a
/// person does not need to see — the account a message goes out from, the message a reply answers. The act is recorded
/// beside the block when the proposal is written, so accepting it performs this record rather than anything derived
/// again afterwards: a model asked a second time could propose something else, and the person approved the first.
/// </para>
/// <para>
/// The hierarchy is closed by a private protected constructor, so the acts declared beside it are the whole of what the
/// Agent may propose, and a reader that handles those handles every proposal this build writes. Each is mail-derived and
/// sensitive exactly as the rest of the conversation is.
/// </para>
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(AgentMessageSending), AgentMessageSending.Kind)]
[JsonDerivedType(typeof(AgentResponseSending), AgentResponseSending.Kind)]
[JsonDerivedType(typeof(AgentEventScheduling), AgentEventScheduling.Kind)]
[JsonDerivedType(typeof(AgentTaskRecording), AgentTaskRecording.Kind)]
public abstract record AgentProposedAct
{
    private protected AgentProposedAct()
    {
    }
}

/// <summary>Send a new message from one of the person's accounts.</summary>
/// <param name="Account">The account the message goes out from, by its configured identifier.</param>
/// <param name="Recipients">Who it is addressed to.</param>
/// <param name="Subject">Its subject.</param>
/// <param name="Body">Its body, as plain text.</param>
/// <remarks>The account is carried as the identifier's text because a stored document is read back by a serializer that cannot construct the identifier's own type.</remarks>
public sealed record AgentMessageSending(
    string Account,
    IReadOnlyList<EmailAddress> Recipients,
    PresentationText Subject,
    PresentationText Body)
    : AgentProposedAct
{
    /// <summary>The value the type discriminator carries.</summary>
    public const string Kind = "sendMessage";

    /// <summary>Gets the account the message goes out from.</summary>
    [JsonIgnore]
    public MailAccountId AccountId => MailAccountId.Create(this.Account);
}

/// <summary>Send an answer to a message the person holds: a reply, a reply to everybody, or a forward.</summary>
/// <param name="AnsweredEmailId">The message being answered.</param>
/// <param name="Act">How it is answered.</param>
/// <param name="Recipients">Who the answer is addressed to beyond what the act itself addresses, and the whole of it for a forward.</param>
/// <param name="Body">What the person says, as plain text; the quotation of the answered message is composed when it is sent.</param>
public sealed record AgentResponseSending(
    StoredEmailId AnsweredEmailId,
    AuthoredResponseAct Act,
    IReadOnlyList<EmailAddress> Recipients,
    PresentationText Body)
    : AgentProposedAct
{
    /// <summary>The value the type discriminator carries.</summary>
    public const string Kind = "sendResponse";
}

/// <summary>Put a date on the person's own calendar.</summary>
/// <param name="Title">What the event is called.</param>
/// <param name="Start">When it begins.</param>
/// <param name="End">When it ends, or <see langword="null" /> to state no end.</param>
/// <param name="IsAllDay">Whether it is stated as a day rather than as a clock time.</param>
/// <param name="SourceMessage">The message it was read out of, or <see langword="null" /> where it names none.</param>
/// <remarks>It announces nothing: a lead is measured back from the person's own due day, and the offset that day runs in is theirs to state rather than a run's to guess.</remarks>
public sealed record AgentEventScheduling(
    PresentationText Title,
    DateTimeOffset Start,
    DateTimeOffset? End,
    bool IsAllDay,
    StoredEmailId? SourceMessage)
    : AgentProposedAct
{
    /// <summary>The value the type discriminator carries.</summary>
    public const string Kind = "scheduleEvent";
}

/// <summary>Write a task onto the person's own list as one they owe.</summary>
/// <param name="Title">The line the list is drawn with.</param>
/// <param name="DueOn">The day it is due on, or <see langword="null" /> to owe it by no day.</param>
/// <param name="SourceMessage">The message it was read out of, or <see langword="null" /> where it names none.</param>
/// <remarks>It announces nothing, for the reason <see cref="AgentEventScheduling" /> does not.</remarks>
public sealed record AgentTaskRecording(
    PresentationText Title,
    DateOnly? DueOn,
    StoredEmailId? SourceMessage)
    : AgentProposedAct
{
    /// <summary>The value the type discriminator carries.</summary>
    public const string Kind = "recordTask";
}
