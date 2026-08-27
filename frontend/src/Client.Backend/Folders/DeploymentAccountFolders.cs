// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Accounts;

namespace MailFathom.Client.Backend.Folders;

/// <summary>One of the owner's accounts and the folders beneath it.</summary>
/// <param name="Account">The account, in the same shape the accounts route publishes it.</param>
/// <param name="Folders">The account's folders, empty where synchronization has reached none.</param>
/// <remarks>
/// The account is the accounts document's own record rather than a copy of its fields, so the two routes cannot come
/// to disagree about what a mailbox is. That is why it is nested rather than flattened: a copy flattened for the
/// convenience of one screen would be five field names to keep in step with another route forever.
/// </remarks>
public sealed record DeploymentAccountFolders(
    DeploymentMailAccount Account,
    IReadOnlyList<DeploymentMailFolder> Folders)
{
    /// <summary>Gets the folders, reading an account the document named none for as one with none.</summary>
    /// <remarks>An account whose folders are absent and one whose folders are an empty list are the same thing to draw, so the difference is answered once here rather than at each reader.</remarks>
    public IReadOnlyList<DeploymentMailFolder> Held => this.Folders ?? [];
}
