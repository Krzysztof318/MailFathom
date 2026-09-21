// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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

    /// <summary>No mail server holds the email any longer, so there is no occurrence a change could be issued against.</summary>
    /// <remarks>
    /// The email is still stored and was still evaluated; what is missing is the remote UID every change recorded here
    /// is carried to the server under. Guessing where the message went would be MailFathom deciding which message a
    /// command reaches.
    /// </remarks>
    EmailNotOnMailServer = 6,

    /// <summary>The account is held, and the change is one a held account cannot commit to stored state yet.</summary>
    /// <remarks>
    /// Nothing produces this any longer. A held copy was the one change it named, and it now commits as a second stored
    /// message like every other copy. The member stays because the history already written names it by this text, and a
    /// run whose reason no longer resolves is a run that cannot be read at all.
    /// </remarks>
    ActionNotAvailableOnHeldAccount = 7,

    /// <summary>The account is held, and it no longer stores the email the rule matched.</summary>
    /// <remarks>
    /// The email was erased between the pass reading it and the change being committed — a person emptying the trash, or
    /// a folder deletion's erasure pass. There is no mail server to point at on a held account, so this is its own
    /// reason rather than <see cref="EmailNotOnMailServer" />.
    /// </remarks>
    EmailNoLongerStored = 8,

    /// <summary>The account is held, and no local folder corresponds to the destination the rule names.</summary>
    /// <remarks>
    /// A held account files into the local folder a protected role names, or into the one the destination's source folder
    /// created. That source folder has delivered nothing locally, or its local folder was deleted into the trash. No
    /// folder run will supply one on a held account, so the remedy is the rule's destination rather than waiting, which is
    /// why this is not <see cref="DestinationFolderUnresolved" />.
    /// </remarks>
    LocalDestinationFolderMissing = 9,

    /// <summary>The account is held, and it stores no payload for the email the rule asked to copy.</summary>
    /// <remarks>
    /// A copy is a second stored message carrying the copied message's own content, so it is the one action that needs
    /// the payload rather than only what is recorded about the email. The payload is absent while a storage ceiling has
    /// deferred it or a read has found it unusable, which is what the operator acts on: raising the ceiling or repairing
    /// the content is the remedy, and every other action on the same email still applied.
    /// </remarks>
    EmailContentNotStored = 10,
}
