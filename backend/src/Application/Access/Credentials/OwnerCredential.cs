// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Credentials;

/// <summary>One credential an owner is admitted by, as an administrator reads it back.</summary>
/// <param name="Id">The stable identifier every later act on this credential names.</param>
/// <param name="Owner">The owner a request authenticated by this credential acts for.</param>
/// <param name="Method">How the credential is presented, and therefore what <paramref name="Lookup" /> holds.</param>
/// <param name="Lookup">The value a presented credential is resolved by.</param>
/// <param name="Permissions">What a request this credential admits may do, in the published order.</param>
/// <param name="Enabled">Whether the credential currently authenticates anything.</param>
/// <param name="Version">How many times the record has been written, counting the act that provisioned it.</param>
/// <param name="CreatedAt">When the credential was provisioned.</param>
/// <param name="MaterialChangedAt">When what the credential is presented as was last replaced, which is the provisioning instant until it is.</param>
/// <remarks>
/// <para>
/// There is no password here, no key, and no stored record of either, and the absence is the point: this is the whole
/// of what the administrative surface publishes about a credential, so no answer it composes can be a way to read
/// stored material back out of the deployment. What an operator needs is which credentials exist, whose they are, what
/// each may do, whether they still work, and how old the material is — each of which is a fact about the record rather
/// than about the secret.
/// </para>
/// <para>
/// <see cref="Lookup" /> is published for three of the four methods, which is what lets an operator tell two credentials
/// apart without the identifier being the only handle they have: a username is what an owner types, a public key's
/// fingerprint is what that client's assertions have to name, and an issuer and subject are what an administrator wrote.
/// A minted key's is withheld and answered as absent, for the reason
/// <see cref="OwnerCredentialMethod.LookupIsDerivedFromTheSecret" /> gives — the digest verifies a presented key, so
/// serving it to whoever may read a listing would be serving a verifier for material this deployment never stored.
/// </para>
/// <para>
/// <see cref="MaterialChangedAt" /> is carried rather than derived from the row's general update instant, because the
/// two answer different questions the moment a credential is disabled and enabled again: an operator asking how long a
/// password has been in use is not asking when the row was last touched. A rehash the deployment performed on a
/// successful sign-in therefore moves <see cref="Version" /> and leaves that instant where it was.
/// </para>
/// </remarks>
public sealed record OwnerCredential(
    Guid Id,
    MailOwnerId Owner,
    OwnerCredentialMethod Method,
    OwnerCredentialLookup Lookup,
    IReadOnlyList<MailFathomPermission> Permissions,
    bool Enabled,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset MaterialChangedAt)
{
    /// <summary>The most credentials one owner may hold, which is therefore the most a listing returns.</summary>
    /// <remarks>
    /// A listing is bounded like every other query this system publishes, and this is where that bound is stated rather
    /// than in the store that applies it. The number is far above what a deployment has any use for — one credential
    /// per device or agent an owner is reached by, plus whatever a rotation left standing — so an owner reaching it has
    /// a provisioning mistake to find rather than a page to turn, which is why the listing carries no cursor. That is
    /// only sound because provisioning refuses the credential past this count rather than writing a row the listing
    /// would then never show: a credential nothing lists is one nothing can be revoked from the listing either, and it
    /// would go on authenticating unseen.
    /// </remarks>
    public const int MaximumListedPerOwner = 100;
}
