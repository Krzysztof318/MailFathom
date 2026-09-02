// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Authoring;

/// <summary>Names which answer to a stored email is being authored.</summary>
/// <remarks>
/// The three are separate acts rather than one act with options, because each addresses a different set of people and
/// two of them differ only in that. A default that quietly became the other is the failure this enumeration exists to
/// prevent: answering everybody when one person was meant publishes the reply to a list, and answering one person when
/// everybody was meant drops the rest of the conversation without saying so.
/// </remarks>
public enum AuthoredResponseAct
{
    /// <summary>An answer to whoever the message asked for answers to, which is one mailbox.</summary>
    Reply = 0,

    /// <summary>An answer to everybody the message was between, less the mailboxes the sending account owns.</summary>
    ReplyToAll = 1,

    /// <summary>A copy of the message, its files included, sent to people the original never named.</summary>
    Forward = 2,
}
