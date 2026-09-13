// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access;

/// <summary>Gives a user the row the mail graph hangs on, and keeps the label on it the one they were recorded under.</summary>
/// <remarks>
/// <para>
/// The envelope is all this writes: the row exists because <c>mailbox_accounts.UserId</c> is a foreign key and the
/// integrity of the mail graph is relational, and the document column stays the empty object it was provisioned with
/// until a write to the user's record fills it.
/// </para>
/// <para>
/// Both operations are idempotent, because a start runs them on every restart against a roster that ordinarily has not
/// changed. Provisioning a user the deployment already holds writes nothing, and relabelling one already carrying the
/// label writes nothing.
/// </para>
/// </remarks>
public interface IMailUserProvisioning
{
    /// <summary>Records a user this deployment did not hold, under the identifier they are declared with.</summary>
    /// <param name="user">The identity the user is declared under, which every mail account of theirs will name.</param>
    /// <param name="displayName">The label the user is told apart by.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the deployment holds this user once the write has run, <see langword="false" /> when the label belongs to somebody else.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody, or <paramref name="displayName" /> is <see langword="null" />, empty, or white space.</exception>
    /// <remarks>
    /// The answer is what the deployment holds afterwards rather than whether this call was the one that wrote it, so
    /// a replica that lost the race to an identical declaration is told the same thing as the one that won. False is
    /// the one outcome a caller has to act on: the label is unique across the deployment, so it says another user has
    /// taken it and this user has no row — which is a start that would otherwise serve mail against a missing one.
    /// </remarks>
    Task<bool> ProvisionAsync(MailUserId user, string displayName, CancellationToken cancellationToken);

    /// <summary>Puts the label a declaration now carries onto the row of a user this deployment already holds.</summary>
    /// <param name="user">The user whose row is relabelled.</param>
    /// <param name="displayName">The label the user is now declared under.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when the row carries the label once the write has run, <see langword="false" /> when the label belongs to somebody else.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody, or <paramref name="displayName" /> is <see langword="null" />, empty, or white space.</exception>
    /// <remarks>
    /// <para>
    /// A label is what an administrator reads a roster by rather than anything an account hangs on, so an administrator
    /// renaming a user over the endpoint is the whole of what renames them, and the new label lasts — no configuration
    /// source names a user, so no start puts an earlier one back. The identifier is the opposite case and is refused
    /// rather than followed, because changing it would orphan every mail account and every stored message recorded
    /// under the old one.
    /// </para>
    /// <para>
    /// The answer is read the way <see cref="ProvisionAsync" />'s is, and for the same race: a label taken between a
    /// roster being read and this statement reaching the table is a refusal a caller states, never a unique-violation
    /// sentence raised out of a start or returned to an operator as a failure.
    /// </para>
    /// </remarks>
    Task<bool> RelabelAsync(MailUserId user, string displayName, CancellationToken cancellationToken);

    /// <summary>Turns either of a user's two endpoint switches on or off, leaving a switch the caller did not name where it is.</summary>
    /// <param name="user">The user whose switches are written.</param>
    /// <param name="mcpEndpoint">Whether the user is served on the MCP endpoint from now on, or <see langword="null" /> to leave it.</param>
    /// <param name="clientEndpoint">Whether the user is served on the client endpoint from now on, or <see langword="null" /> to leave it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The switches the row carries once the write has run, or <see langword="null" /> when this deployment holds no such user.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <remarks>
    /// One statement, and nothing ends a session or a token here: every request re-reads the switch beside the
    /// credential or the session it presents, so a user switched off is refused on their next request on every replica,
    /// and one switched on again is served again by whatever they still hold.
    /// </remarks>
    Task<MailUserEndpointAccess?> SetEndpointAccessAsync(
        MailUserId user,
        bool? mcpEndpoint,
        bool? clientEndpoint,
        CancellationToken cancellationToken);
}
