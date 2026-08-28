// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Infrastructure.Security.Passwords;

/// <summary>The outcome of judging one presented username-and-password credential.</summary>
/// <remarks>
/// A refused credential is an expected outcome of serving an open endpoint rather than an exceptional state, so
/// authentication returns this instead of throwing. The successful result carries the credential's identifier and the
/// owner it authenticates, and nothing else: the identifier is what an audit record and a diagnostic correlate on, the
/// owner is what the request will act for, and the username is deliberately absent so nothing downstream can write one
/// down.
/// </remarks>
public sealed record OwnerPasswordAuthenticationResult
{
    private OwnerPasswordAuthenticationResult(
        Guid? authenticatedCredentialId,
        MailOwnerId owner,
        OwnerPasswordRejection? rejection)
    {
        this.AuthenticatedCredentialId = authenticatedCredentialId;
        this.Owner = owner;
        this.Rejection = rejection;
    }

    /// <summary>Gets whether the presented credential authenticated.</summary>
    public bool Succeeded => this.AuthenticatedCredentialId is not null;

    /// <summary>Gets the identifier of the credential that matched, or <see langword="null" /> when the credential was refused.</summary>
    public Guid? AuthenticatedCredentialId { get; }

    /// <summary>Gets the owner the request acts for, which names nobody when the credential was refused.</summary>
    public MailOwnerId Owner { get; }

    /// <summary>Gets why the credential was refused, or <see langword="null" /> when it authenticated.</summary>
    /// <remarks>It reaches the server log only. Every value produces one indistinguishable response.</remarks>
    public OwnerPasswordRejection? Rejection { get; }

    /// <summary>Creates a successful result naming the credential that matched and the owner it authenticates.</summary>
    /// <param name="authenticatedCredentialId">The identifier of the matching credential.</param>
    /// <param name="owner">The owner the request acts for.</param>
    /// <returns>The successful result.</returns>
    /// <exception cref="ArgumentException">Thrown when the identifier is empty or <paramref name="owner" /> names nobody.</exception>
    public static OwnerPasswordAuthenticationResult Authenticated(Guid authenticatedCredentialId, MailOwnerId owner)
    {
        if (authenticatedCredentialId == Guid.Empty)
        {
            throw new ArgumentException(
                "An authenticated result must name the credential that matched.",
                nameof(authenticatedCredentialId));
        }

        if (!owner.IsSpecified)
        {
            throw new ArgumentException(
                "An authenticated result must name the owner the request acts for.",
                nameof(owner));
        }

        return new OwnerPasswordAuthenticationResult(authenticatedCredentialId, owner, rejection: null);
    }

    /// <summary>Creates a refused result.</summary>
    /// <param name="rejection">Why the credential was refused.</param>
    /// <returns>The refused result.</returns>
    public static OwnerPasswordAuthenticationResult Rejected(OwnerPasswordRejection rejection) =>
        new(authenticatedCredentialId: null, owner: default, rejection);
}
