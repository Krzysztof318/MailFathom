// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>Why an act on an account's folders was refused, whichever copy of its mailbox is the truth.</summary>
/// <remarks>
/// One enumeration for both storage modes rather than one per mode, because the client surface that publishes these
/// is one surface: a caller branching on a refusal must not have to know which set of names it is reading, and most of
/// the refusals — a name a sibling already carries, a folder playing a protected role, a hierarchy too deep — are the
/// same rule stated against a different store. The members a mail server produces are the tail of the list.
/// </remarks>
public enum MailFolderActRefusal
{
    /// <summary>The caller holds no mail account by that identifier.</summary>
    AccountMissing = 0,

    /// <summary>The account's mailbox is being restored to its source, which is the one phase allowing no act on either side.</summary>
    /// <remarks>The name is older than the rule it now states: it once refused every mirrored account too, and issue 1999 left it naming the restore alone.</remarks>
    AccountNotHeld = 1,

    /// <summary>The account has no live folder by that identifier.</summary>
    FolderMissing = 2,

    /// <summary>The account has no live folder by the parent identifier named.</summary>
    ParentMissing = 3,

    /// <summary>The folder plays a protected role and cannot be renamed, moved, or deleted.</summary>
    ProtectedRole = 4,

    /// <summary>The name is empty, too long, or carries a control character, a format character, or the hierarchy delimiter.</summary>
    NameInvalid = 5,

    /// <summary>The name is the inbox's, which no other folder may carry at the top of the hierarchy.</summary>
    InboxNameAtTopLevel = 6,

    /// <summary>A sibling already carries the name, compared without regard to case.</summary>
    NameTaken = 7,

    /// <summary>The folder would be moved beneath itself.</summary>
    NestedInItself = 8,

    /// <summary>The act would place a folder deeper than the hierarchy allows.</summary>
    TooDeep = 9,

    /// <summary>The account already holds as many folders as a hierarchy allows.</summary>
    TooManyFolders = 10,

    /// <summary>The mail server answered and refused to carry out the act.</summary>
    ServerRefused = 11,

    /// <summary>The mail server did not answer within the budget the account's writes are bounded by.</summary>
    /// <remarks>Nothing was changed anywhere: the server is asked before MailFathom's own declaration is written, so an unanswered act leaves both sides as they were.</remarks>
    ServerUnavailable = 12,

    /// <summary>The account already has a folder for the role a creation named, and one account has at most one folder per role.</summary>
    RoleAlreadyPlayed = 13,

    /// <summary>The folder is declared by the deployment's own configuration rather than by the account's record, so this surface does not change it.</summary>
    /// <remarks>What an operator wrote is theirs. A person whose folder this refuses asks whoever administers the deployment, which is the same answer they get for every other setting an operator fixed.</remarks>
    NotDeclaredByTheAccount = 14,

    /// <summary>The act reached the account's mail server and MailFathom could not record it.</summary>
    /// <remarks>
    /// The one refusal that leaves the two sides disagreeing, which is why it is reported rather than retried into
    /// silence: the folder is as the person asked for it on the server, and MailFathom goes on describing the folder it
    /// knew until the act is asked for again.
    /// </remarks>
    NotRecorded = 15,
}
