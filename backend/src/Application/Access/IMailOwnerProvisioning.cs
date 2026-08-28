// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access;

/// <summary>Gives an owner the row the mail graph hangs on, and keeps the label on it the one they are declared under.</summary>
/// <remarks>
/// <para>
/// The envelope is all this writes. An owner a file declares is served from that declaration, so nothing here copies
/// the declaration into their document: the row exists because <c>mailbox_accounts.OwnerId</c> is a foreign key and the
/// integrity of the mail graph is relational, and the document column stays the empty object it was provisioned with
/// until an explicit adoption fills it.
/// </para>
/// <para>
/// Both operations are idempotent, because a start runs them on every restart against a roster that ordinarily has not
/// changed. Provisioning an owner the deployment already holds writes nothing, and relabelling one already carrying the
/// label writes nothing.
/// </para>
/// </remarks>
public interface IMailOwnerProvisioning
{
    /// <summary>Records an owner this deployment did not hold, under the identifier they are declared with.</summary>
    /// <param name="owner">The identity the owner is declared under, which every mail account of theirs will name.</param>
    /// <param name="displayName">The label the owner is told apart by.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the deployment holds this owner once the write has run, <see langword="false" /> when the label belongs to somebody else.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody, or <paramref name="displayName" /> is <see langword="null" />, empty, or white space.</exception>
    /// <remarks>
    /// The answer is what the deployment holds afterwards rather than whether this call was the one that wrote it, so
    /// a replica that lost the race to an identical declaration is told the same thing as the one that won. False is
    /// the one outcome a caller has to act on: the label is unique across the deployment, so it says another owner has
    /// taken it and this owner has no row — which is a start that would otherwise serve mail against a missing one.
    /// </remarks>
    Task<bool> ProvisionAsync(MailOwnerId owner, string displayName, CancellationToken cancellationToken);

    /// <summary>Puts the label a declaration now carries onto the row of an owner this deployment already holds.</summary>
    /// <param name="owner">The owner whose row is relabelled.</param>
    /// <param name="displayName">The label the owner is now declared under.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the row carries the label once the write has run, <see langword="false" /> when the label belongs to somebody else.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody, or <paramref name="displayName" /> is <see langword="null" />, empty, or white space.</exception>
    /// <remarks>
    /// <para>
    /// A label is what an administrator reads a roster by rather than anything an account hangs on, so a file that
    /// renames an owner renames them, and so does an administrator over the endpoint. The identifier is the opposite
    /// case and is refused rather than followed, because changing it would orphan every mail account and every stored
    /// message recorded under the old one.
    /// </para>
    /// <para>
    /// The answer is read the way <see cref="ProvisionAsync" />'s is, and for the same race: a label taken between a
    /// roster being read and this statement reaching the table is a refusal a caller states, never a unique-violation
    /// sentence raised out of a start or returned to an operator as a failure.
    /// </para>
    /// </remarks>
    Task<bool> RelabelAsync(MailOwnerId owner, string displayName, CancellationToken cancellationToken);
}
