// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Security.Cryptography.X509Certificates;

namespace MailMcp.Infrastructure.Certificates;

/// <summary>The outcome of loading a configured trust anchor.</summary>
/// <remarks>
/// Unusable material is an expected configuration failure with a named cause rather than an exceptional state, so
/// loading returns this instead of throwing and startup reports every unusable anchor of a deployment at once. A
/// failed result carries no certificate and no material, which is what keeps it safe to include in an operator
/// message.
/// </remarks>
public sealed record TrustAnchorLoadResult : IDisposable
{
    private TrustAnchorLoadResult(X509Certificate2? trustAnchor, CertificateMaterialFailure? failure)
    {
        this.TrustAnchor = trustAnchor;
        this.Failure = failure;
    }

    /// <summary>Gets the loaded anchor, owned by the caller, or <see langword="null" /> when loading failed.</summary>
    public X509Certificate2? TrustAnchor { get; }

    /// <summary>Gets why no anchor was produced, or <see langword="null" /> when loading succeeded.</summary>
    public CertificateMaterialFailure? Failure { get; }

    /// <summary>Creates a successful result that hands ownership of the anchor to the caller.</summary>
    /// <param name="trustAnchor">The loaded anchor.</param>
    /// <returns>The successful result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="trustAnchor" /> is <see langword="null" />.</exception>
    public static TrustAnchorLoadResult Loaded(X509Certificate2 trustAnchor)
    {
        ArgumentNullException.ThrowIfNull(trustAnchor);

        return new TrustAnchorLoadResult(trustAnchor, failure: null);
    }

    /// <summary>Creates a failed result.</summary>
    /// <param name="failure">Why no anchor was produced.</param>
    /// <returns>The failed result.</returns>
    public static TrustAnchorLoadResult Failed(CertificateMaterialFailure failure) => new(trustAnchor: null, failure);

    /// <inheritdoc />
    /// <remarks>Disposing the result disposes the anchor it owns, so a caller that only needed to prove the material loads does not have to reach through the result to release it.</remarks>
    public void Dispose() => this.TrustAnchor?.Dispose();
}
