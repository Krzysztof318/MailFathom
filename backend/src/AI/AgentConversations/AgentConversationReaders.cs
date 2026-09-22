// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Calendar;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Tasks;

namespace MailFathom.AI.AgentConversations;

/// <summary>The use cases the Agent's tools read and propose through, gathered so the agent takes one collaborator for them.</summary>
/// <param name="ScopeResolver">Resolves the mail the person reads.</param>
/// <param name="KnowledgeSearch">Searches that mail.</param>
/// <param name="ContentReader">Reads a message or a conversation.</param>
/// <param name="StateBrowser">Reads the derivation already made about a conversation.</param>
/// <param name="Calendar">Reads the person's calendar.</param>
/// <param name="Tasks">Reads the person's tasks.</param>
/// <param name="ResponseAuthoring">Resolves who an answer goes to, without saving it.</param>
/// <param name="Authorization">Decides which tools the person's grant allows.</param>
internal sealed record AgentConversationReaders(
    MailboxScopeResolver ScopeResolver,
    IEmailKnowledgeSearch KnowledgeSearch,
    EmailContentReader ContentReader,
    MailThreadStateBrowser StateBrowser,
    OwnCalendar Calendar,
    OwnTasks Tasks,
    StoredEmailResponseAuthoring ResponseAuthoring,
    AccessAuthorization Authorization);
