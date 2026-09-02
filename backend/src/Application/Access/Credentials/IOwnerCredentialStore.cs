// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Credentials;

/// <summary>Where the credentials that resolve a request to one owner are kept.</summary>
/// <remarks>
/// <para>
/// The credentials live in relational columns of their own rather than in an owner's settings document or in an
/// endpoint's configuration section, and this port is what states that as a contract instead of as an implementation
/// detail. A lookup has to be resolved by an index on every authenticated request, an enabled state has to be flipped
/// without rewriting a document, a grant has to be read beside the owner it belongs to, and stored material must never
/// sit in a value something projects, exports, or renders as configuration — none of which a JSONB record or a
/// configuration file can promise.
/// </para>
/// <para>
/// One contract for all four methods, because what differs between them is what a lookup holds and what is judged
/// against the material, neither of which is a question about where a credential is kept. A method that resolved
/// through a port of its own would be a second table, a second index, a second ceiling, and a second administrative
/// vocabulary for one concept.
/// </para>
/// <para>
/// Every write names the owner beside the credential, including the ones that could have been keyed by the credential
/// alone. That is deliberate: an administrator acts on a credential belonging to a selected owner, so an identifier
/// copied from the wrong listing answers <see cref="OwnerCredentialWriteOutcome.UnknownCredential" /> rather than
/// rotating a credential out from under somebody else.
/// </para>
/// <para>
/// Nothing here takes or returns a plaintext password or a minted key. What crosses this boundary is the stored
/// representation the method's own hasher or reader produced, which is what keeps the material's lifetime bounded to
/// the call that made it.
/// </para>
/// </remarks>
public interface IOwnerCredentialStore
{
    /// <summary>Resolves the one credential a method and a lookup name, whether or not it is enabled.</summary>
    /// <param name="method">How the credential was presented.</param>
    /// <param name="lookup">The value the presented credential resolves by.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The credential, or <see langword="null" /> when no row carries that lookup for that method.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="method" /> or <paramref name="lookup" /> is the unspecified struct default.</exception>
    /// <remarks>
    /// One indexed read of the method and the canonical lookup together. A disabled credential is returned rather than
    /// filtered away, so the caller spends the same work on every outcome and the refusal it composes cannot be timed
    /// apart from the refusal an unknown lookup produces.
    /// </remarks>
    Task<ResolvedOwnerCredential?> FindAsync(
        OwnerCredentialMethod method,
        OwnerCredentialLookup lookup,
        CancellationToken cancellationToken);

    /// <summary>Reads the credentials one owner holds, of every method.</summary>
    /// <param name="owner">The owner whose credentials are being listed.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The credentials, oldest first, at most <see cref="OwnerCredential.MaximumListedPerOwner" /> of them, empty when the owner holds none or does not exist.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    /// <remarks>Bounded by that ceiling rather than by what an administrator happened to provision, and never read across owners. There is no cursor past it, for the reason the ceiling states.</remarks>
    Task<IReadOnlyList<OwnerCredential>> ReadForOwnerAsync(MailOwnerId owner, CancellationToken cancellationToken);

    /// <summary>Provisions a new credential for one owner.</summary>
    /// <param name="credentialId">The identifier the new credential is to carry.</param>
    /// <param name="owner">The owner the credential authenticates.</param>
    /// <param name="method">How the credential will be presented.</param>
    /// <param name="lookup">The value it is resolved by.</param>
    /// <param name="material">The stored representation of what is judged, or <see langword="null" /> for a method that keeps none.</param>
    /// <param name="permissions">What a request this credential admits may do.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, or why it did nothing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="permissions" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody, <paramref name="method" /> or <paramref name="lookup" /> is the unspecified struct default, <paramref name="credentialId" /> is the empty identifier, or <paramref name="material" /> disagrees with what <paramref name="method" /> stores.</exception>
    Task<OwnerCredentialWriteOutcome> CreateAsync(
        Guid credentialId,
        MailOwnerId owner,
        OwnerCredentialMethod method,
        OwnerCredentialLookup lookup,
        string? material,
        IReadOnlyList<MailFathomPermission> permissions,
        CancellationToken cancellationToken);

    /// <summary>Replaces what one credential is presented as, leaving its owner, its identifier, and its grant where they are.</summary>
    /// <param name="owner">The owner the credential belongs to.</param>
    /// <param name="credentialId">The credential being written.</param>
    /// <param name="method">The method the credential must already carry, so a rotation cannot land on a credential of another kind.</param>
    /// <param name="lookup">The value it is resolved by from now on, which is unchanged for a password and new for a key.</param>
    /// <param name="material">The stored representation of the new material, or <see langword="null" /> for a method that keeps none.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, or why it did nothing.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody, <paramref name="credentialId" /> is the empty identifier, or <paramref name="method" /> or <paramref name="lookup" /> is the unspecified struct default.</exception>
    /// <remarks>
    /// One statement, so the old material is never absent and never present beside the new one: a request arriving
    /// while the replacement commits is judged against exactly one of the two, and the previous credential stops
    /// working the moment the transaction does. The instant the credential reports its material as having changed at
    /// moves with it.
    /// </remarks>
    Task<OwnerCredentialWriteOutcome> ReplaceMaterialAsync(
        MailOwnerId owner,
        Guid credentialId,
        OwnerCredentialMethod method,
        OwnerCredentialLookup lookup,
        string? material,
        CancellationToken cancellationToken);

    /// <summary>Rewrites the stored record of material that has not changed.</summary>
    /// <param name="owner">The owner the credential belongs to.</param>
    /// <param name="credentialId">The credential being written.</param>
    /// <param name="verifiedMaterial">The record the request actually verified against, which the write is conditioned on.</param>
    /// <param name="material">The stronger representation of the material already in use.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, or why it did nothing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="verifiedMaterial" /> or <paramref name="material" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody or <paramref name="credentialId" /> is the empty identifier.</exception>
    /// <remarks>
    /// <para>
    /// The one operation an authenticated request performs on a credential, and it is separate from
    /// <see cref="ReplaceMaterialAsync" /> for one reason: the material did not change, so the instant the credential
    /// reports its material as having changed at must not move. Reporting every owner who signed in after the work
    /// parameters rose as having just chosen a new password would make that column useless for the question it exists
    /// to answer.
    /// </para>
    /// <para>
    /// <strong>It writes only over the record it verified against.</strong> The request that reaches here read the
    /// stored record, spent a deliberately slow derivation verifying it, and spent another producing the replacement,
    /// and an administrator rotating a leaked credential can commit inside that window — which is the case rotation
    /// exists for. A rehash that named the credential alone would put the superseded material back and stop the
    /// replacement working, silently, on a path that never fails the request. Naming
    /// <paramref name="verifiedMaterial" /> makes the write lose that race instead, answering
    /// <see cref="OwnerCredentialWriteOutcome.UnknownCredential" /> so the rehash is dropped and the rotation stands.
    /// </para>
    /// </remarks>
    Task<OwnerCredentialWriteOutcome> RewriteMaterialAsync(
        MailOwnerId owner,
        Guid credentialId,
        string verifiedMaterial,
        string material,
        CancellationToken cancellationToken);

    /// <summary>Turns one credential on or off without changing what it is presented as.</summary>
    /// <param name="owner">The owner the credential belongs to.</param>
    /// <param name="credentialId">The credential being written.</param>
    /// <param name="enabled">Whether it should authenticate requests.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, or why it did nothing.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody or <paramref name="credentialId" /> is the empty identifier.</exception>
    Task<OwnerCredentialWriteOutcome> SetEnabledAsync(
        MailOwnerId owner,
        Guid credentialId,
        bool enabled,
        CancellationToken cancellationToken);

    /// <summary>Removes one credential and the lookup it held.</summary>
    /// <param name="owner">The owner the credential belongs to.</param>
    /// <param name="credentialId">The credential being removed.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, or why it did nothing.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody or <paramref name="credentialId" /> is the empty identifier.</exception>
    /// <remarks>Deleting frees the lookup for another credential, which is what separates it from disabling one: a disabled credential still holds its name.</remarks>
    Task<OwnerCredentialWriteOutcome> DeleteAsync(
        MailOwnerId owner,
        Guid credentialId,
        CancellationToken cancellationToken);
}
