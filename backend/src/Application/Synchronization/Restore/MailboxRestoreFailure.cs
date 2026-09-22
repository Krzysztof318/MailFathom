// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Synchronization.Restore;

/// <summary>Sorts what stopped one message going back onto its source into the kinds an operator acts differently on.</summary>
/// <remarks>
/// The counts are the only view of a restoring account's progress, so a source that was away, one that refused the
/// credential, and a message whose payload MailFathom can no longer serve have to be told apart in them. Cancellation
/// is not among them: a pass the host stopped waiting for is a shutdown rather than a failure.
/// </remarks>
public enum MailboxRestoreFailure
{
    /// <summary>The mail server did not answer within its configured resilience budget.</summary>
    SourceUnavailable = 0,

    /// <summary>The mail server refused the credential the account reaches it with.</summary>
    SourceRefusedTheCredential = 1,

    /// <summary>The source advertises no folder at the path the message's mapping names, and the mapping permits creating none.</summary>
    FolderMissing = 2,

    /// <summary>The stored payload could not be served, so there were no bytes to append.</summary>
    ContentUnreadable = 3,

    /// <summary>The account has bound no folder under the alias the message goes back through.</summary>
    FolderUnresolved = 4,

    /// <summary>Anything else the attempt ended in, which the next ordinary run attempts again.</summary>
    SomethingElse = 5,

    /// <summary>MailFathom holds a keyword on the message that no authored change may name.</summary>
    /// <remarks>
    /// The stored form keeps whatever a server reported, and an authored one has to be an IMAP atom — so a keyword
    /// MailFathom observed can be one it may not write back. The message's other state is written down and its
    /// keywords are not, which leaves the labels in MailFathom and off the source; correcting the keyword on the
    /// message is what an operator does about it.
    /// </remarks>
    KeywordsUnwritable = 6,
}
