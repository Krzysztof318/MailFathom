// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Rules.History;

/// <summary>Which of a pass's two walks reached the email, which is the whole of an execution's identity beyond its rule.</summary>
/// <remarks>
/// The two are worth telling apart because they answer different questions. Mail is evaluated on arrival exactly once,
/// so an execution recorded there is what a rule did to a message as it came in; an execution recorded for a requested
/// run is what the rules concluded when somebody asked for the mailbox to be walked again, and the run it belongs to is
/// named beside it.
/// </remarks>
public enum MailRuleExecutionTrigger
{
    /// <summary>The email was reached by the arrival walk, which evaluates mail no pass has evaluated before.</summary>
    Arrival = 0,

    /// <summary>The email was reached by a whole-mailbox run somebody asked for.</summary>
    RequestedRun = 1,
}
