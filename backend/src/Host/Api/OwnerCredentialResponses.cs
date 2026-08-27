// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;

namespace MailFathom.Host.Api;

/// <summary>The credential an administrator asks this deployment to provision for an owner.</summary>
/// <param name="Username">The name the owner will sign in with, in whatever casing the administrator wrote.</param>
/// <param name="Password">The password the owner will type.</param>
/// <remarks>
/// Both fields are nullable so a body omitting one is refused with a sentence naming what is missing, rather than by
/// the model binder with one naming a property. <see cref="ToString" /> reports neither half: the username alone would
/// be safe, and a record that printed one half is a record somebody eventually printed while believing it printed
/// neither.
/// </remarks>
internal sealed record OwnerCredentialProvisioningRequest(string? Username, string? Password)
{
    /// <inheritdoc />
    public override string ToString() => nameof(OwnerCredentialProvisioningRequest);
}

/// <summary>The password an administrator asks this deployment to put in place of a credential's current one.</summary>
/// <param name="Password">The new password the owner will type.</param>
/// <remarks><see cref="ToString" /> is redacted, so no diagnostic, log template, or exception message can print the password by rendering the record it arrived in.</remarks>
internal sealed record OwnerCredentialPasswordRequest(string? Password)
{
    /// <inheritdoc />
    public override string ToString() => nameof(OwnerCredentialPasswordRequest);
}

/// <summary>Whether a credential should go on authenticating requests.</summary>
/// <param name="Enabled">The state to put the credential in.</param>
/// <remarks>Nullable so a body that carries no decision is refused rather than read as <see langword="false" />, which would turn a malformed request into a revocation.</remarks>
internal sealed record OwnerCredentialEnablementRequest(bool? Enabled);

/// <summary>One credential as an administrator reads it back.</summary>
/// <param name="Id">The identifier every later act on this credential names.</param>
/// <param name="Username">The canonical username the credential is resolved by.</param>
/// <param name="Enabled">Whether it currently authenticates anything.</param>
/// <param name="Version">How many times the record has been written, counting the act that provisioned it.</param>
/// <param name="CreatedAt">When the credential was provisioned.</param>
/// <param name="PasswordChangedAt">When its password was last replaced.</param>
/// <remarks>
/// There is no password here and no hash here, and the absence is what makes the listing safe to serve: every field is
/// a fact about the record rather than about the secret, so no answer this surface composes is a way to read stored
/// material back out of the deployment.
/// </remarks>
internal sealed record OwnerCredentialResponse(
    Guid Id,
    string Username,
    bool Enabled,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset PasswordChangedAt)
{
    /// <summary>Describes one credential for a caller.</summary>
    /// <param name="credential">The credential as the deployment holds it.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="credential" /> is <see langword="null" />.</exception>
    internal static OwnerCredentialResponse For(OwnerPasswordCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        return new OwnerCredentialResponse(
            credential.Id,
            credential.Username.Value,
            credential.Enabled,
            credential.Version,
            credential.CreatedAt,
            credential.PasswordChangedAt);
    }
}

/// <summary>Every credential one owner holds.</summary>
/// <param name="Owner">The owner the listing is about, echoed so a client that walked a list of owners can tell its answers apart.</param>
/// <param name="Credentials">The credentials, oldest first.</param>
/// <remarks>No cursor, because the listing is bounded by <see cref="OwnerPasswordCredential.MaximumListedPerOwner" /> and an owner reaching that has a provisioning mistake to find rather than a page to turn.</remarks>
internal sealed record OwnerCredentialListResponse(Guid Owner, IReadOnlyList<OwnerCredentialResponse> Credentials);

/// <summary>What provisioning a credential produced.</summary>
/// <param name="CredentialId">The identifier the new credential carries, which is what every later act on it names.</param>
/// <remarks>The identifier and nothing else. The username is what the caller sent and the password is what nothing may echo, so the one thing worth answering with is the one thing the caller could not have known.</remarks>
internal sealed record OwnerCredentialProvisionedResponse(Guid CredentialId);

/// <summary>The owners this deployment holds records for.</summary>
/// <param name="Owners">The owner identifiers, in the directory's own stable order.</param>
/// <remarks>
/// The identifiers alone. An owner's identifier is MailFathom's own generated handle for a record and says nothing
/// about the person, which is what makes it safe to list; a display name would be personal data served to answer a
/// question about credentials, so a client that wants one reads the owner's own record instead.
/// </remarks>
internal sealed record MailOwnerListResponse(IReadOnlyList<Guid> Owners)
{
    /// <summary>Describes the owners a directory read produced.</summary>
    /// <param name="owners">The owners the deployment holds.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="owners" /> is <see langword="null" />.</exception>
    internal static MailOwnerListResponse For(IReadOnlyList<MailOwnerId> owners)
    {
        ArgumentNullException.ThrowIfNull(owners);

        return new MailOwnerListResponse([.. owners.Select(owner => owner.Value)]);
    }
}
