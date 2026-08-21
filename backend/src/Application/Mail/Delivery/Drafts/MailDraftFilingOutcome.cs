// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Drafts;

/// <summary>States what one attempt to bring the mailbox's copy of a draft up to date actually did.</summary>
/// <remarks>
/// It is what a pass reports and a counter is broken down by, so every ending an attempt can have is a member here
/// rather than a failure a caller has to catch. A draft is worth nothing to anybody if a failure to file it ends the
/// pass that was settling the drafts beside it.
/// </remarks>
public enum MailDraftFilingOutcome
{
    /// <summary>The draft owed the mail server nothing.</summary>
    AlreadySettled = 0,

    /// <summary>The current revision was appended to the drafts folder.</summary>
    Filed = 1,

    /// <summary>The copy a revision replaced was taken back out, leaving one copy of the draft in the folder.</summary>
    Replaced = 2,

    /// <summary>Every copy of a discarded draft was settled and the record was removed.</summary>
    Discarded = 3,

    /// <summary>The account maps no folder playing the drafts role, so there is nowhere to put a copy.</summary>
    DestinationUnavailable = 4,

    /// <summary>The tracked copy stopped being one MailFathom may touch, and was left as the owner's.</summary>
    Diverged = 5,

    /// <summary>An append went out and the server's answer to it never came back, so nothing more is attempted.</summary>
    OutcomeUnknown = 6,

    /// <summary>The attempt ended in a failure the next pass may repeat, and the mail server was not reached past it.</summary>
    Failed = 7,
}
