// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Certificates;

/// <summary>The outcome of turning configured material into the TLS identity an endpoint presents.</summary>
/// <remarks>
/// Material an endpoint cannot serve with is an expected configuration failure with a named cause rather than an
/// exceptional state, so loading returns this instead of throwing and startup reports every unusable endpoint of a
/// deployment at once. A failed result carries no certificate and no material, which is what keeps it safe to include
/// in an operator message.
/// </remarks>
public sealed record TlsServerCertificateLoadResult : IDisposable
{
    private TlsServerCertificateLoadResult(TlsServerCertificate? certificate, CertificateMaterialFailure? failure)
    {
        this.Certificate = certificate;
        this.Failure = failure;
    }

    /// <summary>Gets the loaded identity, owned by the caller, or <see langword="null" /> when loading failed.</summary>
    public TlsServerCertificate? Certificate { get; }

    /// <summary>Gets why no identity was produced, or <see langword="null" /> when loading succeeded.</summary>
    public CertificateMaterialFailure? Failure { get; }

    /// <summary>Creates a successful result that hands ownership of the identity to the caller.</summary>
    /// <param name="certificate">The loaded identity.</param>
    /// <returns>The successful result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="certificate" /> is <see langword="null" />.</exception>
    public static TlsServerCertificateLoadResult Loaded(TlsServerCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        return new TlsServerCertificateLoadResult(certificate, failure: null);
    }

    /// <summary>Creates a failed result.</summary>
    /// <param name="failure">Why no identity was produced.</param>
    /// <returns>The failed result.</returns>
    public static TlsServerCertificateLoadResult Failed(CertificateMaterialFailure failure) =>
        new(certificate: null, failure);

    /// <inheritdoc />
    /// <remarks>Disposing the result disposes the identity it owns, so a caller that only needed to prove the material loads does not have to reach through the result to release it.</remarks>
    public void Dispose() => this.Certificate?.Dispose();
}
