// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Folders;

/// <summary>Every mailbox the owner has and every folder in it, as one tree a screen draws without asking again.</summary>
/// <param name="SynchronizationEnabled">Whether the deployment refreshes the local copy of these accounts at all.</param>
/// <param name="Accounts">The owner's accounts, ordered as the catalog orders them, empty when they own none.</param>
/// <remarks>
/// The switch is the deployment's rather than the owner's, and it is answered beside the accounts for the reason every
/// other reading of this kind answers it: a folder that last moved a week ago is a different fact on a deployment that
/// is trying every few minutes and on one that stopped trying, and no per-folder value says which.
/// </remarks>
public sealed record MailFolderDirectory(
    bool SynchronizationEnabled,
    IReadOnlyList<MailAccountFolders> Accounts);
