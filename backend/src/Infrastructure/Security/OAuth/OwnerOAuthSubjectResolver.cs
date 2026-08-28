// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;

namespace MailFathom.Infrastructure.Security.OAuth;

/// <summary>Resolves the owner a validated access token acts for, from the subject its issuer signed.</summary>
/// <remarks>
/// <para>
/// A token proves which person an authorization server signed in and can prove nothing about whose mail that person
/// reaches, because the server knows nothing about this deployment's owners. The mapping is what supplies the missing
/// half, and it is a credential row like every other: an owner, a lookup composed from the issuer and the subject
/// together, a grant, and an enabled state.
/// </para>
/// <para>
/// It is one indexed read rather than a scan of a document, which is what makes it usable on the path every token
/// takes. The issuer travels with the subject because a subject is only meaningful beside the server that minted it:
/// two servers may name two different people identically, and a mapping keyed by the subject alone would let one of
/// them act for the other's owner.
/// </para>
/// <para>
/// A subject nothing maps is refused exactly as an unknown credential is, and so is one whose mapping is disabled.
/// That is the whole of what this decides — it never widens a token to whoever the deployment serves, because a token
/// admitted for nobody in particular is the arrangement the owner axis exists to remove.
/// </para>
/// </remarks>
public sealed class OwnerOAuthSubjectResolver
{
    private readonly IOwnerCredentialStore credentials;

    /// <summary>Initializes a new resolver over one deployment's credential records.</summary>
    /// <param name="credentials">Where the owners' credentials are kept.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="credentials" /> is <see langword="null" />.</exception>
    public OwnerOAuthSubjectResolver(IOwnerCredentialStore credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        this.credentials = credentials;
    }

    /// <summary>Reports which owner one validated subject acts for.</summary>
    /// <param name="issuer">The issuer the validated token named.</param>
    /// <param name="subject">The subject claim the validated token carried.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>What the request is admitted as, or <see langword="null" /> when no enabled mapping names the subject.</returns>
    /// <remarks>The grant it reports is the mapping's own, which is the ceiling wherever the endpoint reads a token's scopes as narrowing one and the whole grant otherwise.</remarks>
    public async Task<AdmittedOwnerCredential?> ResolveAsync(
        string? issuer,
        string? subject,
        CancellationToken cancellationToken)
    {
        if (!OwnerCredentialLookup.TryCreateForOAuthSubject(issuer, subject, out var lookup))
        {
            return null;
        }

        var credential = await this.credentials.FindAsync(
            OwnerCredentialMethod.OAuthSubject,
            lookup,
            cancellationToken);

        return credential is { Enabled: true } ? AdmittedOwnerCredential.For(credential) : null;
    }
}
