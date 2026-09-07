// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;

namespace MailFathom.Host.Api;

/// <summary>The credential an administrator asks this deployment to provision for a user.</summary>
/// <param name="Method">Which of the published methods the credential is presented by.</param>
/// <param name="Username">The name the user will sign in with, where the method is a password.</param>
/// <param name="Password">The password the user will type, where the method is a password.</param>
/// <param name="PublicKey">The client's public key, where the method verifies signed assertions.</param>
/// <param name="Issuer">The authorization server's issuer identifier, where the method maps a validated subject.</param>
/// <param name="Subject">That server's own identifier for the person, where the method maps a validated subject.</param>
/// <param name="Permissions">The published permission names the credential holds, or <see langword="null" /> to hold the whole mail surface.</param>
/// <remarks>
/// <para>
/// One request shape for four methods, with the method named rather than inferred from which fields arrived. Inferring
/// it would make a mistyped field name the difference between provisioning one kind of credential and another, and the
/// two mistakes an administrator actually makes — naming a method and filling in the wrong half, or filling in a half
/// and forgetting the method — are both worth a sentence rather than a silent reading.
/// </para>
/// <para>
/// Every field is nullable so a body omitting one is refused with a sentence naming what is missing, rather than by the
/// model binder with one naming a property. <see cref="ToString" /> reports none of them: the method alone would be
/// safe, and a record that printed one field is a record somebody eventually printed while believing it printed none.
/// </para>
/// </remarks>
internal sealed record UserCredentialProvisioningRequest(
    string? Method,
    string? Username,
    string? Password,
    string? PublicKey,
    string? Issuer,
    string? Subject,
    IReadOnlyList<string>? Permissions)
{
    /// <inheritdoc />
    public override string ToString() => nameof(UserCredentialProvisioningRequest);
}

/// <summary>The material an administrator asks this deployment to put in place of a credential's current material.</summary>
/// <param name="Method">The method the credential carries, which a rotation never changes.</param>
/// <param name="Username">The username the credential already signs in as, where the method is a password.</param>
/// <param name="Password">The new password the user will type, where the method is a password.</param>
/// <param name="PublicKey">The client's new public key, where the method verifies signed assertions.</param>
/// <remarks>
/// <para>
/// The method is stated rather than read back from the record first, which is what keeps this route reachable by a
/// caller holding only the write permission: a credential of another method answers that no such credential exists,
/// because the store matches on the method as well as on the identifier.
/// </para>
/// <para>
/// A key carries nothing here at all — this deployment mints it — and a validated subject cannot be rotated, because
/// there is nothing about it this deployment issued. <see cref="ToString" /> is redacted, so no diagnostic, log
/// template, or exception message can print the password by rendering the record it arrived in.
/// </para>
/// </remarks>
internal sealed record UserCredentialMaterialRequest(
    string? Method,
    string? Username,
    string? Password,
    string? PublicKey)
{
    /// <inheritdoc />
    public override string ToString() => nameof(UserCredentialMaterialRequest);
}

/// <summary>Whether a credential should go on authenticating requests.</summary>
/// <param name="Enabled">The state to put the credential in.</param>
/// <remarks>Nullable so a body that carries no decision is refused rather than read as <see langword="false" />, which would turn a malformed request into a revocation.</remarks>
internal sealed record UserCredentialEnablementRequest(bool? Enabled);

/// <summary>One credential as an administrator reads it back.</summary>
/// <param name="Id">The identifier every later act on this credential names.</param>
/// <param name="Method">The published name of the method the credential is presented by.</param>
/// <param name="Lookup">What the credential is resolved by, or <see langword="null" /> where that value is derived from the secret.</param>
/// <param name="Permissions">The published permission names the credential holds.</param>
/// <param name="Enabled">Whether it currently authenticates anything.</param>
/// <param name="Version">How many times the record has been written, counting the act that provisioned it.</param>
/// <param name="CreatedAt">When the credential was provisioned.</param>
/// <param name="MaterialChangedAt">When what it is presented as was last replaced.</param>
/// <remarks>
/// There is no password here, no hash, and no key digest, and the absence is what makes the listing safe to serve:
/// every field is a fact about the record rather than about the secret. The one field that could have been both is
/// <paramref name="Lookup" />, which is withheld exactly where <see cref="UserCredentialMethod.LookupIsDerivedFromTheSecret" />
/// says it verifies one — so no answer this surface composes is a way to read stored material back out of the
/// deployment.
/// </remarks>
internal sealed record UserCredentialResponse(
    Guid Id,
    string Method,
    string? Lookup,
    IReadOnlyList<string> Permissions,
    bool Enabled,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset MaterialChangedAt)
{
    /// <summary>Describes one credential for a caller.</summary>
    /// <param name="credential">The credential as the deployment holds it.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="credential" /> is <see langword="null" />.</exception>
    internal static UserCredentialResponse For(UserCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        return new UserCredentialResponse(
            credential.Id,
            credential.Method.Name,
            PublishedLookupOf(credential),
            [.. credential.Permissions.Select(permission => permission.Name)],
            credential.Enabled,
            credential.Version,
            credential.CreatedAt,
            credential.MaterialChangedAt);
    }

    private static string? PublishedLookupOf(UserCredential credential) =>
        credential.Method.LookupIsDerivedFromTheSecret ? null : credential.Lookup.Value;
}

/// <summary>Every credential one user holds.</summary>
/// <param name="User">The user the listing is about, echoed so a client that walked a list of users can tell its answers apart.</param>
/// <param name="Credentials">The credentials, oldest first.</param>
/// <remarks>No cursor, because the listing is bounded by <see cref="UserCredential.MaximumListedPerUser" /> and a user reaching that has a provisioning mistake to find rather than a page to turn.</remarks>
internal sealed record UserCredentialListResponse(Guid User, IReadOnlyList<UserCredentialResponse> Credentials);

/// <summary>What provisioning a credential produced.</summary>
/// <param name="CredentialId">The identifier the new credential carries, which is what every later act on it names.</param>
/// <param name="Lookup">What the credential will be resolved by, where that is a value the administrator can act on.</param>
/// <param name="Key">The key this deployment minted, where the method is one it mints — reported here and never again.</param>
/// <remarks>
/// The key is the one field that exists for a single response. It is drawn during the write, stored only as a digest,
/// and is therefore unrecoverable the moment this body is discarded, which is what an administrator has to be told
/// rather than left to discover. <see cref="ToString" /> is redacted for the same reason the requests' are.
/// </remarks>
internal sealed record UserCredentialProvisionedResponse(Guid CredentialId, string? Lookup, string? Key)
{
    /// <summary>Describes what a provisioning act produced.</summary>
    /// <param name="method">The method the credential was provisioned for, which decides whether its lookup is publishable.</param>
    /// <param name="provisioning">What the use case did.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="provisioning" /> is <see langword="null" />.</exception>
    internal static UserCredentialProvisionedResponse For(
        UserCredentialMethod method,
        UserCredentialProvisioning provisioning)
    {
        ArgumentNullException.ThrowIfNull(provisioning);

        return new UserCredentialProvisionedResponse(
            provisioning.CredentialId,
            method.LookupIsDerivedFromTheSecret ? null : provisioning.Lookup.Value,
            provisioning.MintedKey);
    }

    /// <inheritdoc />
    public override string ToString() => $"{nameof(UserCredentialProvisionedResponse)} {{ {this.CredentialId} }}";
}

/// <summary>What replacing a credential's material produced.</summary>
/// <param name="Lookup">What the credential is resolved by from now on, where that is a value the administrator can act on.</param>
/// <param name="Key">The key this deployment minted, where the method is one it mints — reported here and never again.</param>
/// <remarks>An answer with a body rather than an empty one, because rotating a key is the second moment a key exists and there is nowhere else to read it. <see cref="ToString" /> is redacted for the reason the provisioning answer's is.</remarks>
internal sealed record UserCredentialRotatedResponse(string? Lookup, string? Key)
{
    /// <summary>Describes what a rotation produced.</summary>
    /// <param name="method">The method the credential carries, which decides whether its lookup is publishable.</param>
    /// <param name="rotation">What the use case did.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rotation" /> is <see langword="null" />.</exception>
    internal static UserCredentialRotatedResponse For(
        UserCredentialMethod method,
        UserCredentialRotation rotation)
    {
        ArgumentNullException.ThrowIfNull(rotation);

        return new UserCredentialRotatedResponse(
            method.LookupIsDerivedFromTheSecret ? null : rotation.Lookup.Value,
            rotation.MintedKey);
    }

    /// <inheritdoc />
    public override string ToString() => nameof(UserCredentialRotatedResponse);
}
