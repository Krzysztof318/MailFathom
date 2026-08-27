// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Folders;

/// <summary>What a deployment reports about the signed-in owner's mailboxes and every folder in them.</summary>
/// <param name="SynchronizationEnabled">Whether the deployment refreshes the local copy of these accounts at all.</param>
/// <param name="Accounts">One entry per account the owner owns, each with its folders, empty where they own none.</param>
/// <remarks>
/// <para>
/// The tree arrives as one document because it is one thing on screen. A client that read the folders here and the
/// mailbox names from the accounts route would be composing one picture out of two answers, the second already stale
/// relative to the first.
/// </para>
/// <para>
/// The switch is reported beside the accounts rather than on each folder because no per-folder value carries it: a
/// folder that last moved a week ago means one thing where the deployment is still trying every few minutes and
/// another where somebody switched synchronization off.
/// </para>
/// </remarks>
public sealed record DeploymentMailFolders(
    bool SynchronizationEnabled,
    IReadOnlyList<DeploymentAccountFolders> Accounts)
{
    /// <summary>Gets the accounts, reading a document that named none as an owner who owns none.</summary>
    /// <remarks>
    /// A missing member deserializes to <see langword="null" /> rather than to an empty list, and every reader here
    /// wants the same answer for the two: nothing to draw. Said once rather than at each reader, so no screen has to
    /// remember which of the two it received.
    /// </remarks>
    public IReadOnlyList<DeploymentAccountFolders> Owned => this.Accounts ?? [];
}
