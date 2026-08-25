// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Accounts;

/// <summary>What a deployment reports about the signed-in owner's mail accounts.</summary>
/// <param name="SynchronizationEnabled">Whether the deployment refreshes the local copy of these accounts at all.</param>
/// <param name="Accounts">One entry per account the owner owns, empty where they own none.</param>
/// <remarks>
/// <para>
/// The switch is reported beside the accounts rather than on each of them because no per-account value carries it: a
/// copy that last moved a week ago means one thing where the deployment is still trying every few minutes and another
/// where somebody switched synchronization off, and a client that could not tell the two apart would show every
/// account as failing or none of them.
/// </para>
/// <para>
/// An owner who owns no account reads an empty list, which is a state to render rather than a failure. A credential
/// whose grant does not carry reading is refused instead, and reaches a caller as
/// <see cref="DeploymentFailureReason.CredentialRefused" />, so the two are never the same answer.
/// </para>
/// </remarks>
public sealed record DeploymentMailAccounts(
    bool SynchronizationEnabled,
    IReadOnlyList<DeploymentMailAccount> Accounts)
{
    /// <summary>Gets the accounts, reading a document that named none as an owner who owns none.</summary>
    /// <remarks>
    /// A missing member deserializes to <see langword="null" /> rather than to an empty list, and every reader here
    /// wants the same answer for the two: nothing to show. Said once rather than at each reader, so no screen has to
    /// remember which of the two it received.
    /// </remarks>
    public IReadOnlyList<DeploymentMailAccount> Owned => this.Accounts ?? [];
}
