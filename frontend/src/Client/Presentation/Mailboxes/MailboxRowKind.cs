// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation.Mailboxes;

/// <summary>What one row of the mailbox tree stands for, which is what decides how it is drawn and what it narrows to.</summary>
/// <remarks>
/// The tree is one list rather than five, because a person reads it as one: the unified rows at the top are what make
/// several mailboxes visibly one workspace, and the account rows below them are what keep each mailbox its own thing.
/// A kind rather than a set of flags, because the five are alternatives and no row is two of them.
/// </remarks>
public enum MailboxRowKind
{
    /// <summary>Every mailbox the signed-in person can reach, which is what the tree opens scoped to.</summary>
    Everything = 0,

    /// <summary>One special-use folder taken across every mailbox that has one, such as every inbox at once.</summary>
    /// <remarks>
    /// Offered only where more than one mailbox has that role, because with a single mailbox it would name exactly what
    /// the folder beneath the account already names.
    /// </remarks>
    UnifiedRole = 1,

    /// <summary>One of the owner's mailboxes.</summary>
    Account = 2,

    /// <summary>A level of a mail server's hierarchy that holds folders but is not one, so nothing can be scoped to it.</summary>
    /// <remarks>
    /// A mail server may report a folder several levels deep whose intermediate levels it never advertised as folders
    /// of their own. Drawing the level anyway is what keeps the tree the shape the server has; refusing to select it is
    /// what keeps a scope from naming something no route can be asked about.
    /// </remarks>
    Group = 3,

    /// <summary>One folder of one mailbox.</summary>
    Folder = 4,
}
