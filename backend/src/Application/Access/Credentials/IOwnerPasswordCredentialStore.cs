// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Credentials;

/// <summary>Where an owner's username-and-password credentials are kept.</summary>
/// <remarks>
/// <para>
/// The credentials live in relational columns of their own rather than in the owner's settings document, and this port
/// is what states that as a contract instead of as an implementation detail. A username has to be resolved by an index
/// on every authenticated request, an enabled state has to be flipped without rewriting a document, and a hash must
/// never sit in a column something projects, exports, or renders as configuration — none of which a JSONB record can
/// promise.
/// </para>
/// <para>
/// Every write names the owner beside the credential, including the three that could have been keyed by the credential
/// alone. That is deliberate: an administrator acts on a credential belonging to a selected owner, so an identifier
/// copied from the wrong listing answers <see cref="OwnerCredentialWriteOutcome.UnknownCredential" /> rather than
/// rotating a password out from under somebody else.
/// </para>
/// <para>
/// Nothing here takes or returns a plaintext password. What crosses this boundary is the stored representation
/// <see cref="IPasswordHasher" /> produced, which is what keeps the material's lifetime bounded to the call that hashed
/// it.
/// </para>
/// </remarks>
public interface IOwnerPasswordCredentialStore
{
    /// <summary>Resolves the one credential a username names, whether or not it is enabled.</summary>
    /// <param name="username">The canonical username presented.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The credential, or <see langword="null" /> when no row carries that username.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="username" /> is the unspecified struct default.</exception>
    /// <remarks>
    /// One indexed read of the canonical column. A disabled credential is returned rather than filtered away, so the
    /// caller spends the same work on every outcome and the refusal it composes cannot be timed apart from the refusal
    /// an unknown username produces.
    /// </remarks>
    Task<ResolvedOwnerPasswordCredential?> FindByUsernameAsync(
        OwnerCredentialUsername username,
        CancellationToken cancellationToken);

    /// <summary>Reads the credentials one owner holds.</summary>
    /// <param name="owner">The owner whose credentials are being listed.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The credentials, oldest first, at most <see cref="OwnerPasswordCredential.MaximumListedPerOwner" /> of them, empty when the owner holds none or does not exist.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    /// <remarks>Bounded by that ceiling rather than by what an administrator happened to provision, and never read across owners. There is no cursor past it, for the reason the ceiling states.</remarks>
    Task<IReadOnlyList<OwnerPasswordCredential>> ReadForOwnerAsync(
        MailOwnerId owner,
        CancellationToken cancellationToken);

    /// <summary>Provisions a new credential for one owner.</summary>
    /// <param name="credentialId">The identifier the new credential is to carry.</param>
    /// <param name="owner">The owner the credential authenticates.</param>
    /// <param name="username">The canonical username it is resolved by.</param>
    /// <param name="passwordHash">The stored representation of the password.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, or why it did nothing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="passwordHash" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody, <paramref name="username" /> is the unspecified struct default, or <paramref name="credentialId" /> is the empty identifier.</exception>
    Task<OwnerCredentialWriteOutcome> CreateAsync(
        Guid credentialId,
        MailOwnerId owner,
        OwnerCredentialUsername username,
        string passwordHash,
        CancellationToken cancellationToken);

    /// <summary>Replaces the stored password of one credential.</summary>
    /// <param name="owner">The owner the credential belongs to.</param>
    /// <param name="credentialId">The credential being written.</param>
    /// <param name="passwordHash">The stored representation of the new password.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, or why it did nothing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="passwordHash" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody or <paramref name="credentialId" /> is the empty identifier.</exception>
    /// <remarks>
    /// One statement, so the old hash is never absent and never present beside the new one: a request arriving while
    /// the replacement commits is judged against exactly one of the two, and the previous password stops working the
    /// moment the transaction does. The instant the credential reports its password as having changed at moves with it.
    /// </remarks>
    Task<OwnerCredentialWriteOutcome> ReplacePasswordAsync(
        MailOwnerId owner,
        Guid credentialId,
        string passwordHash,
        CancellationToken cancellationToken);

    /// <summary>Rewrites the stored record of a password that has not changed.</summary>
    /// <param name="owner">The owner the credential belongs to.</param>
    /// <param name="credentialId">The credential being written.</param>
    /// <param name="passwordHash">The stronger representation of the password already in use.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, or why it did nothing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="passwordHash" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody or <paramref name="credentialId" /> is the empty identifier.</exception>
    /// <remarks>
    /// The one operation an authenticated request performs on a credential, and it is separate from
    /// <see cref="ReplacePasswordAsync" /> for one reason: the password did not change, so the instant the credential
    /// reports its password as having changed at must not move. Reporting every owner who signed in after the work
    /// parameters rose as having just chosen a new password would make that column useless for the question it exists
    /// to answer.
    /// </remarks>
    Task<OwnerCredentialWriteOutcome> RewritePasswordHashAsync(
        MailOwnerId owner,
        Guid credentialId,
        string passwordHash,
        CancellationToken cancellationToken);

    /// <summary>Turns one credential on or off without changing its password.</summary>
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

    /// <summary>Removes one credential and the username it held.</summary>
    /// <param name="owner">The owner the credential belongs to.</param>
    /// <param name="credentialId">The credential being removed.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, or why it did nothing.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody or <paramref name="credentialId" /> is the empty identifier.</exception>
    /// <remarks>Deleting frees the username for another credential, which is what separates it from disabling one: a disabled credential still holds its name.</remarks>
    Task<OwnerCredentialWriteOutcome> DeleteAsync(
        MailOwnerId owner,
        Guid credentialId,
        CancellationToken cancellationToken);
}
