// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery.Scheduling;

namespace MailFathom.Application.Mail.Delivery.Scheduling;

/// <summary>What a dispatch pass needs to know about one declaration, and nothing more.</summary>
/// <remarks>
/// <para>
/// A pass runs on the job worker's interval and reads every declaration this deployment holds, so what it reads is
/// read constantly. Three values decide an occasion — which declaration, whose mailbox, and how often — and everything
/// else on a declaration belongs to the occasion that produces a message rather than to the decision that an occasion
/// has come.
/// </para>
/// <para>
/// The recipients are the reason this type exists rather than the declaration itself being read. They are addresses of
/// people other than the mailbox's owner, and loading five hundred declarations' worth of them on every pass would keep
/// personal data moving through a decision that never looks at it. The occasion reads them when it composes, by
/// identifier, which is where they are actually needed.
/// </para>
/// </remarks>
/// <param name="Id">The declaration the occasion belongs to.</param>
/// <param name="AccountId">The account every occurrence is submitted through and sent as.</param>
/// <param name="Schedule">The repetition as it was declared, in the syntax the dispatch mechanism parses.</param>
public sealed record RecurringSendDeclaration(RecurringSendId Id, MailAccountId AccountId, string Schedule);
