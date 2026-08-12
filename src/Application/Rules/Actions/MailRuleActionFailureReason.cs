// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Rules.Actions;

/// <summary>Why one planned action produced no request against the mailbox.</summary>
/// <remarks>
/// Every member describes something that was true when the rule set was read and has stopped being true since. The
/// action fails visibly rather than choosing an alternative, because every alternative would be MailFathom deciding
/// where somebody's mail goes.
/// </remarks>
public enum MailRuleActionFailureReason
{
    /// <summary>The destination alias names no folder this account currently has bound.</summary>
    /// <remarks>
    /// The alias was declared as a mapped folder when the rule set was read, and discovery has not bound it since — the
    /// server no longer advertises the folder, or has never been asked for it. Filing into the nearest folder whose name
    /// looks right is precisely what this refuses.
    /// </remarks>
    DestinationFolderUnresolved = 0,

    /// <summary>The account the email belongs to is no longer one the configuration declares.</summary>
    /// <remarks>
    /// A reload can withdraw an account while a pass over its mail is running. What the account decided about its own
    /// deletions is then unknown, and a change asked for on behalf of a mailbox the deployment has stopped declaring is
    /// one nobody is currently asking for.
    /// </remarks>
    AccountNoLongerConfigured = 1,

    /// <summary>The account no longer permits a rule to make this change to its mailbox.</summary>
    /// <remarks>
    /// The rule set was refused if it declared an action the account did not permit, but the two sections reload
    /// independently: narrowing what an account permits leaves a rule set nobody edited in force, and an operator who
    /// has just withdrawn permission to delete is not asking for one more deletion first.
    /// </remarks>
    ActionNoLongerPermitted = 2,
}
