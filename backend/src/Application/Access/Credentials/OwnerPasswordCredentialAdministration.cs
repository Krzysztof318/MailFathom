// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Credentials;

/// <summary>What an administrator does to the username-and-password credentials one owner holds.</summary>
/// <remarks>
/// <para>
/// Every act here names the owner it is for, and that is the whole of what the user story asks: credentials are
/// provisioned by whoever administers the deployment rather than by whoever holds one, so an owner cannot mint a
/// credential for another owner because an owner cannot mint one at all. The administrative principal acts for nobody's
/// mail — <see cref="AccessAuthorization.RequireOwner" /> refuses it — so the owner is stated in the request and
/// checked against the records this deployment holds instead of being carried on the principal.
/// </para>
/// <para>
/// Reading and writing are separately granted, because they are separately dangerous: a listing says which credentials
/// exist and whose they are, which is <see cref="MailFathomPermission.AdminRead" />, while creating, rotating,
/// disabling, and deleting one decides who can read a person's mail, which is
/// <see cref="MailFathomPermission.AdminCredentialsWrite" /> — the same grant that already bounds placing a mailbox
/// owner's long-lived credential, and for the same reason.
/// </para>
/// <para>
/// Plaintext reaches this type as a span and leaves it as a hash. It is read once, inside the synchronous call that
/// hashes it, and nothing awaited afterwards has it, so nothing this type holds outlives the call that was handed the
/// span. What it does not do is erase the buffer behind that span, and neither boundary that supplies one can: a
/// password arrives at the HTTP boundary as a deserialized <see langword="string" /> and at the terminal boundary as
/// one too, and a <see langword="string" /> cannot be wiped. The bound worth stating is therefore the reading rather
/// than the memory — the plaintext is read inside one synchronous call and is never copied, retained, awaited over, or
/// written down here. Nothing here logs it, returns it, or puts it in a refusal — a password this deployment refused is
/// described by the rule it broke and never by what was written.
/// </para>
/// <para>
/// The policy is checked here as well as at the boundary, which is the second enforcement the layering rule asks for.
/// Reaching it is a defect in an entrypoint rather than an operator's mistake, so it raises rather than answering: a
/// boundary that means to report the rule calls <see cref="OwnerPasswordPolicy.FindRefusal" /> and answers with the
/// sentence, and one that did not check has found a way in that this type has to close rather than describe.
/// </para>
/// </remarks>
public sealed class OwnerPasswordCredentialAdministration
{
    private readonly AccessAuthorization authorization;
    private readonly IMailOwnerDirectory owners;
    private readonly IOwnerPasswordCredentialStore credentials;
    private readonly IPasswordHasher passwordHasher;
    private readonly IOwnerCredentialAuditor auditor;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the administration over one deployment's credential records.</summary>
    /// <param name="authorization">What each act is admitted by.</param>
    /// <param name="owners">Where the roster an administrator selects an owner from is read.</param>
    /// <param name="credentials">Where the credentials are kept.</param>
    /// <param name="passwordHasher">What turns a password into the record that is stored.</param>
    /// <param name="auditor">Where a change to who can reach an owner's mail is written down.</param>
    /// <param name="timeProvider">The clock a record is stamped with.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public OwnerPasswordCredentialAdministration(
        AccessAuthorization authorization,
        IMailOwnerDirectory owners,
        IOwnerPasswordCredentialStore credentials,
        IPasswordHasher passwordHasher,
        IOwnerCredentialAuditor auditor,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(owners);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(passwordHasher);
        ArgumentNullException.ThrowIfNull(auditor);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.authorization = authorization;
        this.owners = owners;
        this.credentials = credentials;
        this.passwordHasher = passwordHasher;
        this.auditor = auditor;
        this.timeProvider = timeProvider;
    }

    /// <summary>Reads the owners this deployment holds records for.</summary>
    /// <param name="limit">The greatest number of owners to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The owners, in a stable order, and no more than <paramref name="limit" /> of them.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is not positive.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminRead" />.</exception>
    /// <remarks>
    /// The roster is where an administrator gets the identifier every other act here names, so it is published beside
    /// them and under the same grant rather than left to the port. Which owners a deployment holds is what a listing of
    /// their credentials would otherwise be a way of discovering, and an entrypoint reaching the port directly would be
    /// an entrypoint that never asked.
    /// </remarks>
    public Task<IReadOnlyList<MailOwnerId>> ReadOwnersAsync(int limit, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminRead);

        return this.owners.ReadOwnersAsync(limit, cancellationToken);
    }

    /// <summary>Reads the credentials one owner holds.</summary>
    /// <param name="owner">The owner being asked about.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The credentials, oldest first, empty when the owner holds none.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminRead" />.</exception>
    /// <remarks>An owner this deployment holds no record for is answered with an empty listing rather than a refusal, because "which credentials does this owner hold" has the same answer either way and telling the two apart would report which owner identifiers exist.</remarks>
    public Task<IReadOnlyList<OwnerPasswordCredential>> ReadCredentialsAsync(
        MailOwnerId owner,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminRead);

        return this.credentials.ReadForOwnerAsync(owner, cancellationToken);
    }

    /// <summary>Provisions a credential an owner can sign in with.</summary>
    /// <param name="owner">The owner the credential authenticates.</param>
    /// <param name="username">The canonical username it will be resolved by.</param>
    /// <param name="password">The plaintext, read within this call and never retained.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, or why it did nothing.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody, <paramref name="username" /> is the unspecified struct default, or the password breaks <see cref="OwnerPasswordPolicy" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminCredentialsWrite" />.</exception>
    /// <remarks>
    /// The identifier is minted here rather than supplied, so nothing outside this deployment decides what a credential
    /// is called, and it is reported back because every later act on the credential names it. It is minted from the
    /// injected clock rather than from the ambient one: a version-7 identifier embeds the instant it was made, and a
    /// listing orders by the stamped instant and then by the identifier, so two clocks would order a credential against
    /// itself.
    /// </remarks>
    public async Task<OwnerCredentialProvisioning> ProvisionAsync(
        MailOwnerId owner,
        OwnerCredentialUsername username,
        ReadOnlyMemory<char> password,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        var passwordHash = this.HashOrThrow(password.Span);
        var provisionedAt = this.timeProvider.GetUtcNow();
        var credentialId = Guid.CreateVersion7(provisionedAt);

        var outcome = await this.credentials.CreateAsync(
            credentialId,
            owner,
            username,
            passwordHash,
            cancellationToken);

        await this.RecordAsync(outcome, OwnerCredentialAct.Provisioned, credentialId, owner, cancellationToken);

        return new OwnerCredentialProvisioning(outcome, credentialId);
    }

    /// <summary>Replaces one credential's password, which stops the previous one working at that instant.</summary>
    /// <param name="owner">The owner the credential belongs to.</param>
    /// <param name="credentialId">The credential being rotated.</param>
    /// <param name="password">The new plaintext, read within this call and never retained.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, or why it did nothing.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody, <paramref name="credentialId" /> is the empty identifier, or the password breaks <see cref="OwnerPasswordPolicy" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminCredentialsWrite" />.</exception>
    public async Task<OwnerCredentialWriteOutcome> RotatePasswordAsync(
        MailOwnerId owner,
        Guid credentialId,
        ReadOnlyMemory<char> password,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        var passwordHash = this.HashOrThrow(password.Span);

        var outcome = await this.credentials.ReplacePasswordAsync(
            owner,
            credentialId,
            passwordHash,
            cancellationToken);

        await this.RecordAsync(outcome, OwnerCredentialAct.PasswordRotated, credentialId, owner, cancellationToken);

        return outcome;
    }

    /// <summary>Turns one credential on or off while it keeps its username and its password.</summary>
    /// <param name="owner">The owner the credential belongs to.</param>
    /// <param name="credentialId">The credential being written.</param>
    /// <param name="enabled">Whether it should authenticate requests.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, or why it did nothing.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody or <paramref name="credentialId" /> is the empty identifier.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminCredentialsWrite" />.</exception>
    /// <remarks>Disabling is the reversible half of revoking: the credential stops working immediately and its username stays claimed, so nothing else can be provisioned under the name somebody is still using.</remarks>
    public async Task<OwnerCredentialWriteOutcome> SetEnabledAsync(
        MailOwnerId owner,
        Guid credentialId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        var outcome = await this.credentials.SetEnabledAsync(owner, credentialId, enabled, cancellationToken);

        await this.RecordAsync(
            outcome,
            enabled ? OwnerCredentialAct.Enabled : OwnerCredentialAct.Disabled,
            credentialId,
            owner,
            cancellationToken);

        return outcome;
    }

    /// <summary>Removes one credential and frees the username it held.</summary>
    /// <param name="owner">The owner the credential belongs to.</param>
    /// <param name="credentialId">The credential being removed.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, or why it did nothing.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody or <paramref name="credentialId" /> is the empty identifier.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminCredentialsWrite" />.</exception>
    public async Task<OwnerCredentialWriteOutcome> DeleteAsync(
        MailOwnerId owner,
        Guid credentialId,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        var outcome = await this.credentials.DeleteAsync(owner, credentialId, cancellationToken);

        await this.RecordAsync(outcome, OwnerCredentialAct.Deleted, credentialId, owner, cancellationToken);

        return outcome;
    }

    /// <summary>Turns a password into the record to store, refusing one this deployment does not accept.</summary>
    /// <exception cref="ArgumentException">Thrown when the password breaks the policy, which is an entrypoint that did not check rather than an operator's mistake.</exception>
    private string HashOrThrow(ReadOnlySpan<char> password)
    {
        if (OwnerPasswordPolicy.FindRefusal(password) is { } refusal)
        {
            throw new ArgumentException(
                $"The password was not checked against the policy before it reached the use case. {refusal}",
                nameof(password));
        }

        return this.passwordHasher.Hash(password);
    }

    /// <summary>Writes down an act that actually changed something.</summary>
    /// <remarks>
    /// An act that changed nothing is not recorded, because a record of a mistyped identifier is a record of an attempt
    /// rather than of a change and would leave a trail in which "this credential was deleted" and "somebody typed this
    /// identifier" read alike. The record is written after the change commits, so nothing is claimed that the store
    /// refused.
    /// </remarks>
    private Task RecordAsync(
        OwnerCredentialWriteOutcome outcome,
        OwnerCredentialAct act,
        Guid credentialId,
        MailOwnerId owner,
        CancellationToken cancellationToken) =>
        outcome == OwnerCredentialWriteOutcome.Written
            ? this.auditor.RecordCredentialChangeAsync(
                new OwnerCredentialChange(
                    act,
                    credentialId,
                    owner,

                    // Every act above required a permission, which only an admitted caller holds, so the identity is
                    // there. The fallback is the honest name for the one principal that could reach a use case without
                    // one rather than a guess about who acted.
                    this.authorization.PrincipalIdentity ?? AuthorizedPrincipal.ProcessIdentityName,
                    this.timeProvider.GetUtcNow()),
                cancellationToken)
            : Task.CompletedTask;
}

/// <summary>What provisioning a credential did, and the identifier the new one carries.</summary>
/// <param name="Outcome">What the act did, or why it did nothing.</param>
/// <param name="CredentialId">The identifier the credential was minted with, which is meaningful only when the act was performed.</param>
/// <remarks>The identifier travels beside the outcome because it is the one thing a caller cannot have known in advance and the one thing every later act on the credential names.</remarks>
public sealed record OwnerCredentialProvisioning(OwnerCredentialWriteOutcome Outcome, Guid CredentialId);
