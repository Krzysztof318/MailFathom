// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace MailFathom.Infrastructure.Secrets;

/// <summary>The outcome of resolving one secret-bearing configuration value.</summary>
/// <remarks>
/// An unresolved reference is an expected configuration failure with a named cause rather than an exceptional state, so
/// resolution returns this instead of throwing and the host reports every failure of a startup at once. A failed result
/// carries no material and no target, which is what keeps it safe to include in an operator message.
/// </remarks>
public sealed record SecretResolutionResult
{
    private SecretResolutionResult(
        ResolvedSecret? secret,
        SecretMaterialSource? source,
        SecretResolutionFailure? failure)
    {
        this.Secret = secret;
        this.Source = source;
        this.Failure = failure;
    }

    /// <summary>Gets whether material was produced.</summary>
    public bool Succeeded => this.Secret is not null;

    /// <summary>Gets the resolved material, owned by the caller, or <see langword="null" /> when resolution failed.</summary>
    public ResolvedSecret? Secret { get; }

    /// <summary>Gets where the material came from, or <see langword="null" /> when resolution failed.</summary>
    public SecretMaterialSource? Source { get; }

    /// <summary>Gets why no material was produced, or <see langword="null" /> when resolution succeeded.</summary>
    public SecretResolutionFailure? Failure { get; }

    /// <summary>Creates a successful result that hands ownership of the material to the caller.</summary>
    /// <param name="secret">The resolved material.</param>
    /// <param name="source">Where the material came from.</param>
    /// <returns>The successful result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="secret" /> is <see langword="null" />.</exception>
    public static SecretResolutionResult Resolved(ResolvedSecret secret, SecretMaterialSource source)
    {
        ArgumentNullException.ThrowIfNull(secret);

        return new SecretResolutionResult(secret, source, failure: null);
    }

    /// <summary>Creates a failed result.</summary>
    /// <param name="failure">Why no material was produced.</param>
    /// <returns>The failed result.</returns>
    public static SecretResolutionResult Failed(SecretResolutionFailure failure) =>
        new(secret: null, source: null, failure);
}
