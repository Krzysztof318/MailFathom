// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Credentials;

/// <summary>One owner's username-and-password credential, as an administrator reads it back.</summary>
/// <param name="Id">The stable identifier every later act on this credential names.</param>
/// <param name="Owner">The owner a request authenticated by this credential acts for.</param>
/// <param name="Username">The canonical username the credential is resolved by.</param>
/// <param name="Enabled">Whether the credential currently authenticates anything.</param>
/// <param name="Version">How many times the record has been written, counting the act that provisioned it.</param>
/// <param name="CreatedAt">When the credential was provisioned.</param>
/// <param name="PasswordChangedAt">When the stored password was last replaced, which is the provisioning instant until it is rotated.</param>
/// <remarks>
/// <para>
/// There is no password here and there is no hash here, and the absence is the point: this is the whole of what the
/// administrative surface publishes about a credential, so no answer it composes can be a way to read stored material
/// back out of the deployment. What an operator needs is which credentials exist, whose they are, whether they still
/// work, and how old the password is — each of which is a fact about the record rather than about the secret.
/// </para>
/// <para>
/// <see cref="PasswordChangedAt" /> is carried rather than derived from the row's general update instant, because the
/// two answer different questions the moment a credential is disabled and enabled again: an operator asking how long a
/// password has been in use is not asking when the row was last touched. A rehash the deployment performed on a
/// successful sign-in therefore moves <see cref="Version" /> and leaves that instant where it was.
/// </para>
/// </remarks>
public sealed record OwnerPasswordCredential(
    Guid Id,
    MailOwnerId Owner,
    OwnerCredentialUsername Username,
    bool Enabled,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset PasswordChangedAt)
{
    /// <summary>The most credentials one owner's listing returns.</summary>
    /// <remarks>
    /// A listing is bounded like every other query this system publishes, and this is where that bound is stated rather
    /// than in the store that applies it. The number is far above what a deployment has any use for — one credential
    /// per device an owner signs in from, plus whatever a rotation left standing — so an owner reaching it has a
    /// provisioning mistake to find rather than a page to turn, which is why the listing carries no cursor.
    /// </remarks>
    public const int MaximumListedPerOwner = 100;
}
