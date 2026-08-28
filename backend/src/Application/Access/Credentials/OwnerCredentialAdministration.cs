// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Credentials;

/// <summary>What an administrator does to the credentials one owner is admitted by.</summary>
/// <remarks>
/// <para>
/// Every act here names the owner it is for, and that is the whole of what the user story asks: credentials are
/// provisioned by whoever administers the deployment rather than by whoever holds one, so an owner cannot mint a
/// credential for another owner because an owner cannot mint one at all. The administrative principal acts for nobody's
/// mail — <see cref="AccessAuthorization.RequireOwner" /> refuses it — so the owner is stated in the request and
/// checked against the records this deployment holds instead of being carried on the principal.
/// </para>
/// <para>
/// One type for all four methods, because provisioning differs only in what is drawn or read before the row is written
/// and every rule after that point is one rule: which owner, which grant, which ceiling, and what is written down
/// afterwards. A method with an administration of its own would be a second place for each of those to be decided.
/// </para>
/// <para>
/// Reading and writing are separately granted, because they are separately dangerous: a listing says which credentials
/// exist and whose they are, which is <see cref="MailFathomPermission.AdminRead" />, while creating, rotating,
/// disabling, and deleting one decides who can read a person's mail, which is
/// <see cref="MailFathomPermission.AdminCredentialsWrite" /> — the same grant that already bounds placing a mailbox
/// owner's long-lived credential, and for the same reason.
/// </para>
/// <para>
/// Plaintext reaches this type as a span and leaves it as a record. It is read once, inside the synchronous call that
/// hashes it, and nothing awaited afterwards has it, so nothing this type holds outlives the call that was handed the
/// span. What it does not do is erase the buffer behind that span, and neither boundary that supplies one can: a
/// password arrives at the HTTP boundary as a deserialized <see langword="string" /> and at the terminal boundary as
/// one too, and a <see langword="string" /> cannot be wiped. The bound worth stating is therefore the reading rather
/// than the memory — the plaintext is read inside one synchronous call and is never copied, retained, awaited over, or
/// written down here. Nothing here logs it, returns it, or puts it in a refusal — a password this deployment refused is
/// described by the rule it broke and never by what was written. A minted key is the one value that travels back out,
/// because it has to reach whoever asked for it and exists nowhere else once it has.
/// </para>
/// <para>
/// The policy and the grant are checked here as well as at the boundary, which is the second enforcement the layering
/// rule asks for. Reaching either is a defect in an entrypoint rather than an operator's mistake, so both raise rather
/// than answering: a boundary that means to report the rule calls <see cref="OwnerPasswordPolicy.FindRefusal" /> or
/// <see cref="FindGrantRefusal" /> and answers with the sentence, and one that did not check has found a way in that
/// this type has to close rather than describe.
/// </para>
/// </remarks>
public sealed class OwnerCredentialAdministration
{
    private readonly AccessAuthorization authorization;
    private readonly IMailOwnerDirectory owners;
    private readonly IOwnerCredentialStore credentials;
    private readonly IPasswordHasher passwordHasher;
    private readonly IOwnerApiKeyMinter apiKeyMinter;
    private readonly IClientPublicKeyReader publicKeyReader;
    private readonly IOwnerCredentialAuditor auditor;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the administration over one deployment's credential records.</summary>
    /// <param name="authorization">What each act is admitted by.</param>
    /// <param name="owners">Where the roster an administrator selects an owner from is read.</param>
    /// <param name="credentials">Where the credentials are kept.</param>
    /// <param name="passwordHasher">What turns a password into the record that is stored.</param>
    /// <param name="apiKeyMinter">What draws a key and reduces one to the value it is resolved by.</param>
    /// <param name="publicKeyReader">What reads a client's public key into what is stored and what resolves it.</param>
    /// <param name="auditor">Where a change to who can reach an owner's mail is written down.</param>
    /// <param name="timeProvider">The clock a record is stamped with.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public OwnerCredentialAdministration(
        AccessAuthorization authorization,
        IMailOwnerDirectory owners,
        IOwnerCredentialStore credentials,
        IPasswordHasher passwordHasher,
        IOwnerApiKeyMinter apiKeyMinter,
        IClientPublicKeyReader publicKeyReader,
        IOwnerCredentialAuditor auditor,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(owners);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(passwordHasher);
        ArgumentNullException.ThrowIfNull(apiKeyMinter);
        ArgumentNullException.ThrowIfNull(publicKeyReader);
        ArgumentNullException.ThrowIfNull(auditor);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.authorization = authorization;
        this.owners = owners;
        this.credentials = credentials;
        this.passwordHasher = passwordHasher;
        this.apiKeyMinter = apiKeyMinter;
        this.publicKeyReader = publicKeyReader;
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
    public async Task<IReadOnlyList<MailOwnerId>> ReadOwnersAsync(int limit, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminRead);

        // The directory answers with each owner's whole record, because that is what the startup roster is reconciled
        // from. What an administrator names every act here by is the identifier alone, so the label is dropped rather
        // than widening what this publishes.
        var records = await this.owners.ReadOwnersAsync(limit, cancellationToken);

        return [.. records.Select(record => record.Owner)];
    }

    /// <summary>Reads the credentials one owner holds, of every method.</summary>
    /// <param name="owner">The owner being asked about.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The credentials, oldest first, empty when the owner holds none.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminRead" />.</exception>
    /// <remarks>An owner this deployment holds no record for is answered with an empty listing rather than a refusal, because "which credentials does this owner hold" has the same answer either way and telling the two apart would report which owner identifiers exist.</remarks>
    public Task<IReadOnlyList<OwnerCredential>> ReadCredentialsAsync(
        MailOwnerId owner,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminRead);

        return this.credentials.ReadForOwnerAsync(owner, cancellationToken);
    }

    /// <summary>Provisions a username and password an owner signs in with.</summary>
    /// <param name="owner">The owner the credential authenticates.</param>
    /// <param name="username">The canonical username it will be resolved by.</param>
    /// <param name="password">The plaintext, read within this call and never retained.</param>
    /// <param name="permissions">What the credential grants, or <see langword="null" /> to grant the whole mail surface.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, or why it did nothing.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody, <paramref name="username" /> is the unspecified struct default, the password breaks <see cref="OwnerPasswordPolicy" />, or the grant names something an owner-facing credential cannot hold.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminCredentialsWrite" />.</exception>
    public Task<OwnerCredentialProvisioning> ProvisionPasswordAsync(
        MailOwnerId owner,
        OwnerCredentialUsername username,
        ReadOnlyMemory<char> password,
        IReadOnlyList<MailFathomPermission>? permissions,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        var grant = GrantOrThrow(permissions);
        var passwordHash = this.HashOrThrow(password.Span);

        return this.ProvisionAsync(
            owner,
            OwnerCredentialMethod.Password,
            OwnerCredentialLookup.ForUsername(username),
            passwordHash,
            grant,
            mintedKey: null,
            cancellationToken);
    }

    /// <summary>Draws a key one of an owner's clients presents, and provisions the credential it resolves.</summary>
    /// <param name="owner">The owner the credential authenticates.</param>
    /// <param name="permissions">What the credential grants, or <see langword="null" /> to grant the whole mail surface.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, and the key to report where it was performed.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody or the grant names something an owner-facing credential cannot hold.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminCredentialsWrite" />.</exception>
    /// <remarks>The key is drawn here rather than accepted from the request, so nothing outside this deployment decides how much entropy a credential carries, and it is reported back because this is the one moment it exists.</remarks>
    public Task<OwnerCredentialProvisioning> ProvisionApiKeyAsync(
        MailOwnerId owner,
        IReadOnlyList<MailFathomPermission>? permissions,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        var grant = GrantOrThrow(permissions);
        var minted = this.apiKeyMinter.Mint();

        return this.ProvisionAsync(
            owner,
            OwnerCredentialMethod.ApiKey,
            minted.Lookup,
            material: null,
            grant,
            minted.Key,
            cancellationToken);
    }

    /// <summary>Registers a public key whose signed assertions authenticate one owner.</summary>
    /// <param name="owner">The owner the credential authenticates.</param>
    /// <param name="writtenPublicKey">The client's public key as the operator supplied it.</param>
    /// <param name="permissions">What the credential grants, or <see langword="null" /> to grant the whole mail surface.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, and the fingerprint the client's assertions must name.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody, the written key is not one this deployment accepts, or the grant names something an owner-facing credential cannot hold.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminCredentialsWrite" />.</exception>
    public Task<OwnerCredentialProvisioning> ProvisionPublicKeyAsync(
        MailOwnerId owner,
        string? writtenPublicKey,
        IReadOnlyList<MailFathomPermission>? permissions,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        var grant = GrantOrThrow(permissions);
        var publicKey = this.ReadPublicKeyOrThrow(writtenPublicKey, nameof(writtenPublicKey));

        return this.ProvisionAsync(
            owner,
            OwnerCredentialMethod.PublicKey,
            publicKey.Lookup,
            publicKey.Material,
            grant,
            mintedKey: null,
            cancellationToken);
    }

    /// <summary>Maps one authorization server's subject onto the owner it stands for.</summary>
    /// <param name="owner">The owner the subject acts for.</param>
    /// <param name="issuer">The issuer exactly as it is configured and as a token carries it.</param>
    /// <param name="subject">The subject claim the server issues for that person.</param>
    /// <param name="permissions">What the mapping grants, or <see langword="null" /> to grant the whole mail surface.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, or why it did nothing.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody, the pair cannot compose a lookup, or the grant names something an owner-facing credential cannot hold.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminCredentialsWrite" />.</exception>
    /// <remarks>What this grants is not what the token may do where the endpoint reads a grant from a token's own scopes; there it is the ceiling the scopes narrow. The owner comes from here either way, because a token cannot carry one.</remarks>
    public Task<OwnerCredentialProvisioning> ProvisionOAuthSubjectAsync(
        MailOwnerId owner,
        string? issuer,
        string? subject,
        IReadOnlyList<MailFathomPermission>? permissions,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        var grant = GrantOrThrow(permissions);

        if (!OwnerCredentialLookup.TryCreateForOAuthSubject(issuer, subject, out var lookup))
        {
            throw new ArgumentException(
                "An OAuth subject mapping names an issuer carrying no whitespace and a subject the server issues, and "
                + "the two together are bounded like every other credential lookup.",
                nameof(issuer));
        }

        return this.ProvisionAsync(
            owner,
            OwnerCredentialMethod.OAuthSubject,
            lookup,
            material: null,
            grant,
            mintedKey: null,
            cancellationToken);
    }

    /// <summary>Replaces one credential's password, which stops the previous one working at that instant.</summary>
    /// <param name="owner">The owner the credential belongs to.</param>
    /// <param name="credentialId">The credential being rotated.</param>
    /// <param name="username">The username the credential already carries, which a password rotation leaves where it is.</param>
    /// <param name="password">The new plaintext, read within this call and never retained.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, or why it did nothing.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody, <paramref name="credentialId" /> is the empty identifier, <paramref name="username" /> is the unspecified struct default, or the password breaks <see cref="OwnerPasswordPolicy" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminCredentialsWrite" />.</exception>
    /// <remarks>The username is stated rather than read back first, because the write names the lookup a credential carries from now on and a password rotation is the one case where that is the value it already had; a mistyped one answers that no such credential exists rather than renaming somebody's sign-in.</remarks>
    public async Task<OwnerCredentialRotation> RotatePasswordAsync(
        MailOwnerId owner,
        Guid credentialId,
        OwnerCredentialUsername username,
        ReadOnlyMemory<char> password,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        var lookup = OwnerCredentialLookup.ForUsername(username);
        var passwordHash = this.HashOrThrow(password.Span);

        var outcome = await this.credentials.ReplaceMaterialAsync(
            owner,
            credentialId,
            OwnerCredentialMethod.Password,
            lookup,
            passwordHash,
            cancellationToken);

        await this.RecordAsync(
            outcome,
            OwnerCredentialAct.MaterialRotated,
            credentialId,
            owner,
            OwnerCredentialMethod.Password,
            cancellationToken);

        return new OwnerCredentialRotation(outcome, lookup, MintedKey: null);
    }

    /// <summary>Draws a new key for one credential, which stops the previous one working at that instant.</summary>
    /// <param name="owner">The owner the credential belongs to.</param>
    /// <param name="credentialId">The credential being rotated.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, and the key to report where it was performed.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody or <paramref name="credentialId" /> is the empty identifier.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminCredentialsWrite" />.</exception>
    /// <remarks>The lookup moves with the key, because it is the key's own digest — which is what makes rotating one a single write rather than a second credential the operator then has to remember to delete.</remarks>
    public async Task<OwnerCredentialRotation> RotateApiKeyAsync(
        MailOwnerId owner,
        Guid credentialId,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        var minted = this.apiKeyMinter.Mint();

        var outcome = await this.credentials.ReplaceMaterialAsync(
            owner,
            credentialId,
            OwnerCredentialMethod.ApiKey,
            minted.Lookup,
            material: null,
            cancellationToken);

        await this.RecordAsync(
            outcome,
            OwnerCredentialAct.MaterialRotated,
            credentialId,
            owner,
            OwnerCredentialMethod.ApiKey,
            cancellationToken);

        return new OwnerCredentialRotation(
            outcome,
            minted.Lookup,
            outcome == OwnerCredentialWriteOutcome.Written ? minted.Key : null);
    }

    /// <summary>Replaces the public key one credential verifies assertions against.</summary>
    /// <param name="owner">The owner the credential belongs to.</param>
    /// <param name="credentialId">The credential being rotated.</param>
    /// <param name="writtenPublicKey">The client's new public key as the operator supplied it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, and the fingerprint the client's assertions must name from now on.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody, <paramref name="credentialId" /> is the empty identifier, or the written key is not one this deployment accepts.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminCredentialsWrite" />.</exception>
    public async Task<OwnerCredentialRotation> ReplacePublicKeyAsync(
        MailOwnerId owner,
        Guid credentialId,
        string? writtenPublicKey,
        CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        var publicKey = this.ReadPublicKeyOrThrow(writtenPublicKey, nameof(writtenPublicKey));

        var outcome = await this.credentials.ReplaceMaterialAsync(
            owner,
            credentialId,
            OwnerCredentialMethod.PublicKey,
            publicKey.Lookup,
            publicKey.Material,
            cancellationToken);

        await this.RecordAsync(
            outcome,
            OwnerCredentialAct.MaterialRotated,
            credentialId,
            owner,
            OwnerCredentialMethod.PublicKey,
            cancellationToken);

        return new OwnerCredentialRotation(outcome, publicKey.Lookup, MintedKey: null);
    }

    /// <summary>Turns one credential on or off while it keeps what it is presented as.</summary>
    /// <param name="owner">The owner the credential belongs to.</param>
    /// <param name="credentialId">The credential being written.</param>
    /// <param name="enabled">Whether it should authenticate requests.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the act did, or why it did nothing.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody or <paramref name="credentialId" /> is the empty identifier.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold <see cref="MailFathomPermission.AdminCredentialsWrite" />.</exception>
    /// <remarks>Disabling is the reversible half of revoking: the credential stops working immediately and its lookup stays claimed, so nothing else can be provisioned under the name somebody is still using.</remarks>
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
            method: null,
            cancellationToken);

        return outcome;
    }

    /// <summary>Removes one credential and frees the lookup it held.</summary>
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

        await this.RecordAsync(outcome, OwnerCredentialAct.Deleted, credentialId, owner, method: null, cancellationToken);

        return outcome;
    }

    /// <summary>Reports why a written grant cannot be held by an owner-facing credential, or that it can.</summary>
    /// <param name="permissions">The permissions the request named, or <see langword="null" /> where it named none.</param>
    /// <returns>The sentence naming what to write instead, or <see langword="null" /> when the grant is one this deployment accepts.</returns>
    /// <remarks>
    /// Published here rather than at each boundary, so an operator provisioning over HTTP and one provisioning from a
    /// terminal are refused in one sentence. An unstated grant is not a refusal: it is the whole mail surface, which is
    /// the reading configuration already gave an entry that wrote no grant, and an empty list is the opposite
    /// statement — a credential that authenticates and may do nothing.
    /// </remarks>
    public static string? FindGrantRefusal(IReadOnlyList<MailFathomPermission>? permissions)
    {
        if (permissions is null)
        {
            return null;
        }

        foreach (var permission in permissions)
        {
            if (!permission.IsSpecified)
            {
                return "A grant names published permissions; one of the values names none.";
            }

            if (permission.Surface != ProtectedSurface.Mail)
            {
                return $"'{permission.Name}' belongs to the administrative surface and grants nothing to an owner's "
                    + "credential, which reaches that owner's mail and nothing else. Write one of "
                    + $"{PublishedMailPermissionNames()}, or leave the grant unwritten to hold all of them.";
            }
        }

        return null;
    }

    /// <summary>Provisions one credential, once its method has drawn or read whatever it needed.</summary>
    /// <remarks>
    /// The identifier is minted here rather than supplied, so nothing outside this deployment decides what a credential
    /// is called, and it is reported back because every later act on the credential names it. It is minted from the
    /// injected clock rather than from the ambient one: a version-7 identifier embeds the instant it was made, and a
    /// listing orders by the stamped instant and then by the identifier, so two clocks would order a credential against
    /// itself.
    /// </remarks>
    private async Task<OwnerCredentialProvisioning> ProvisionAsync(
        MailOwnerId owner,
        OwnerCredentialMethod method,
        OwnerCredentialLookup lookup,
        string? material,
        IReadOnlyList<MailFathomPermission> permissions,
        string? mintedKey,
        CancellationToken cancellationToken)
    {
        var provisionedAt = this.timeProvider.GetUtcNow();
        var credentialId = Guid.CreateVersion7(provisionedAt);

        var outcome = await this.credentials.CreateAsync(
            credentialId,
            owner,
            method,
            lookup,
            material,
            permissions,
            cancellationToken);

        await this.RecordAsync(outcome, OwnerCredentialAct.Provisioned, credentialId, owner, method, cancellationToken);

        return new OwnerCredentialProvisioning(
            outcome,
            credentialId,
            lookup,
            outcome == OwnerCredentialWriteOutcome.Written ? mintedKey : null);
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

    /// <summary>Reads a written public key, refusing one this deployment cannot verify against.</summary>
    /// <exception cref="ArgumentException">Thrown when the written form is unusable, which is an entrypoint that did not check rather than an operator's mistake.</exception>
    private ClientPublicKey ReadPublicKeyOrThrow(string? written, string parameterName) =>
        this.publicKeyReader.TryRead(written, out var publicKey) && publicKey is not null
            ? publicKey
            : throw new ArgumentException(
                "The public key was not checked before it reached the use case. "
                + this.publicKeyReader.DescribeAcceptedForm(),
                parameterName);

    /// <summary>Settles what a credential grants, refusing a grant an owner-facing credential cannot hold.</summary>
    /// <exception cref="ArgumentException">Thrown when the grant was not checked at the boundary that composed it.</exception>
    private static IReadOnlyList<MailFathomPermission> GrantOrThrow(IReadOnlyList<MailFathomPermission>? permissions)
    {
        if (FindGrantRefusal(permissions) is { } refusal)
        {
            throw new ArgumentException(
                $"The grant was not checked before it reached the use case. {refusal}",
                nameof(permissions));
        }

        if (permissions is null)
        {
            return MailFathomPermission.PublishedFor(ProtectedSurface.Mail);
        }

        var granted = permissions.ToHashSet();

        return [.. MailFathomPermission.All.Where(granted.Contains)];
    }

    private static string PublishedMailPermissionNames() => string.Join(
        ", ",
        MailFathomPermission.PublishedFor(ProtectedSurface.Mail).Select(permission => $"'{permission.Name}'"));

    /// <summary>Writes down an act that actually changed something.</summary>
    /// <remarks>
    /// An act that changed nothing is not recorded, because a record of a mistyped identifier is a record of an attempt
    /// rather than of a change and would leave a trail in which "this credential was deleted" and "somebody typed this
    /// identifier" read alike. The record is written after the change commits, so nothing is claimed that the store
    /// refused.
    /// <para>
    /// The acts that name no method are the ones that are the same act whichever credential they reached: turning one
    /// on, turning one off, and removing one are written against the identifier, which is the handle the administrator
    /// used, and the record carries no method rather than an unspecified one. The method is recorded where the act was
    /// about it.
    /// </para>
    /// </remarks>
    private Task RecordAsync(
        OwnerCredentialWriteOutcome outcome,
        OwnerCredentialAct act,
        Guid credentialId,
        MailOwnerId owner,
        OwnerCredentialMethod? method,
        CancellationToken cancellationToken) =>
        outcome == OwnerCredentialWriteOutcome.Written
            ? this.auditor.RecordCredentialChangeAsync(
                new OwnerCredentialChange(
                    act,
                    credentialId,
                    owner,
                    method,

                    // Every act above required a permission, which only an admitted caller holds, so the identity is
                    // there. The fallback is the honest name for the one principal that could reach a use case without
                    // one rather than a guess about who acted.
                    this.authorization.PrincipalIdentity ?? AuthorizedPrincipal.ProcessIdentityName,
                    this.timeProvider.GetUtcNow()),
                cancellationToken)
            : Task.CompletedTask;
}

/// <summary>What provisioning a credential did, and what only the act that performed it can report.</summary>
/// <param name="Outcome">What the act did, or why it did nothing.</param>
/// <param name="CredentialId">The identifier the credential was minted with, which is meaningful only when the act was performed.</param>
/// <param name="Lookup">The value the credential is resolved by, which a client has to be told for two of the four methods.</param>
/// <param name="MintedKey">The key this deployment drew, where the method is one it draws, and <see langword="null" /> otherwise.</param>
/// <remarks>
/// The identifier travels beside the outcome because it is the one thing a caller cannot have known in advance and the
/// one thing every later act on the credential names. <see cref="MintedKey" /> travels the same way for a stronger
/// reason: it exists here and nowhere else, so a caller that drops it has lost the credential rather than mislaid it.
/// <see cref="ToString" /> is redacted so that no diagnostic renders one.
/// </remarks>
public sealed record OwnerCredentialProvisioning(
    OwnerCredentialWriteOutcome Outcome,
    Guid CredentialId,
    OwnerCredentialLookup Lookup,
    string? MintedKey)
{
    /// <inheritdoc />
    public override string ToString() =>
        $"{nameof(OwnerCredentialProvisioning)} {{ {this.Outcome}, {this.CredentialId} }}";
}

/// <summary>What replacing a credential's material did, and what the act reports back.</summary>
/// <param name="Outcome">What the act did, or why it did nothing.</param>
/// <param name="Lookup">The value the credential is resolved by from now on.</param>
/// <param name="MintedKey">The key this deployment drew, where the method is one it draws, and <see langword="null" /> otherwise.</param>
/// <remarks><see cref="ToString" /> is redacted for the reason <see cref="OwnerCredentialProvisioning" />'s is.</remarks>
public sealed record OwnerCredentialRotation(
    OwnerCredentialWriteOutcome Outcome,
    OwnerCredentialLookup Lookup,
    string? MintedKey)
{
    /// <inheritdoc />
    public override string ToString() => $"{nameof(OwnerCredentialRotation)} {{ {this.Outcome} }}";
}
