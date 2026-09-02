// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography.X509Certificates;

namespace MailFathom.Infrastructure.Certificates;

/// <summary>The certificates presented after a leaf, ordered towards a root, or the reason they form no chain.</summary>
/// <remarks>
/// The order is the whole point of the type: a client builds a path by following each certificate's issuer, so what a
/// server sends has to lead from the leaf outwards. Material that cannot be ordered that way is an expected
/// configuration failure with a named cause rather than an exceptional state, which is why this carries one instead of
/// throwing.
/// </remarks>
internal sealed record TlsServerCertificateChainOrder
{
    private TlsServerCertificateChainOrder(
        X509Certificate2[] intermediates,
        CertificateMaterialFailure? unsuitability)
    {
        this.Intermediates = intermediates;
        this.Unsuitability = unsuitability;
    }

    /// <summary>Gets the intermediates in the order they are presented, empty when the material supplied none or forms no chain.</summary>
    /// <remarks>Ownership stays with the caller that supplied the certificates; nothing here disposes them.</remarks>
    internal X509Certificate2[] Intermediates { get; }

    /// <summary>Gets why the supplied certificates form no chain, or <see langword="null" /> when they were ordered.</summary>
    internal CertificateMaterialFailure? Unsuitability { get; }

    /// <summary>Creates the ordered result.</summary>
    /// <param name="intermediates">The intermediates, leading from the leaf's issuer towards a root.</param>
    /// <returns>The ordered result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="intermediates" /> is <see langword="null" />.</exception>
    internal static TlsServerCertificateChainOrder Ordered(X509Certificate2[] intermediates)
    {
        ArgumentNullException.ThrowIfNull(intermediates);

        return new TlsServerCertificateChainOrder(intermediates, unsuitability: null);
    }

    /// <summary>Creates the result of material that forms no chain.</summary>
    /// <param name="unsuitability">Why the supplied certificates form none.</param>
    /// <returns>The failed result.</returns>
    internal static TlsServerCertificateChainOrder Unusable(CertificateMaterialFailure unsuitability) =>
        new([], unsuitability);
}
