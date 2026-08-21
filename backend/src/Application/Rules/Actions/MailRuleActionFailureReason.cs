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
    /// <summary>The destination names a folder this account mirrors and nothing has bound yet.</summary>
    /// <remarks>
    /// The alias was declared as a mapped folder when the rule set was read, and no run of the folder has recorded a
    /// binding for it since. The next run of that folder is what supplies one. Filing into the nearest folder whose
    /// name looks right is precisely what this refuses.
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

    /// <summary>No mapping of the account answers to the name the rule filed into.</summary>
    /// <remarks>
    /// The rule set is refused when it is read if it names a folder the account does not map, so this is the reload
    /// case: a mapping was withdrawn while a pass over that account's mail was running. Mapping the folder is what makes
    /// it reachable again, and mirroring it is not part of that.
    /// </remarks>
    DestinationFolderUnmapped = 3,

    /// <summary>The account's server advertises no folder the destination's mapping names.</summary>
    /// <remarks>
    /// The mapping is there and the folder is not: somebody deleted or renamed it, the configured path was never right,
    /// or a folder the mapping asked to have created could not be created. Nothing falls back to the configured path and
    /// nothing searches for a folder whose name looks close, because either would file somebody's mail somewhere they
    /// did not name.
    /// </remarks>
    DestinationFolderNotAdvertised = 4,

    /// <summary>Several advertised folders carry the role the destination's mapping names.</summary>
    /// <remarks>
    /// Which of them was meant is the operator's to state, by mapping the role to one folder or by naming the folder's
    /// path outright. Picking the first of several would let a reordered server response change where mail is filed.
    /// </remarks>
    DestinationFolderAmbiguous = 5,
}
