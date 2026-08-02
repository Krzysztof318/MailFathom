// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MailFathom.Infrastructure.Secrets;

namespace MailFathom.Infrastructure.Certificates;

/// <summary>Turns deployment-provisioned material into a certificate usable as a trust anchor.</summary>
/// <remarks>
/// <para>
/// This is the only place in MailFathom that knows about X.509, which is what keeps the scheme adapters material-agnostic:
/// a future material kind arrives as another loader over the same resolved bytes rather than as a change to how a
/// secret is retrieved.
/// </para>
/// <para>
/// Every anchor is imported with <see cref="X509KeyStorageFlags.EphemeralKeySet" />. A trust anchor needs no private
/// key, bundles commonly carry one anyway, and the default key-storage behavior would write that key into a key store
/// on disk — outside the buffer whose lifetime the secret machinery controls. An anchor that turns out to carry a
/// private key is rejected rather than silently accepted, because trusting an authority MailFathom could also impersonate
/// is not what an operator configured.
/// </para>
/// </remarks>
public sealed class TrustAnchorLoader
{
    private readonly ISecretReferenceResolver secretReferenceResolver;

    /// <summary>Initializes a new trust anchor loader.</summary>
    /// <param name="secretReferenceResolver">The resolver that turns a configured reference into material.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="secretReferenceResolver" /> is <see langword="null" />.</exception>
    public TrustAnchorLoader(ISecretReferenceResolver secretReferenceResolver)
    {
        ArgumentNullException.ThrowIfNull(secretReferenceResolver);

        this.secretReferenceResolver = secretReferenceResolver;
    }

    /// <summary>Loads the certificate behind a configured secret block.</summary>
    /// <param name="configuredMaterial">The block referencing the anchor material, whose nested password block protects a PKCS#12 bundle when one is used.</param>
    /// <param name="cancellationToken">Cancels the retrieval and the bundle password retrieval.</param>
    /// <returns>The loaded anchor, whose ownership passes to the caller, or a named failure.</returns>
    /// <remarks>
    /// The material is erased before this returns, whether loading succeeded or not, so the anchor outlives the bytes
    /// it was parsed from. The returned certificate is public material and may be logged by subject and thumbprint.
    /// </remarks>
    public async Task<TrustAnchorLoadResult> LoadAsync(
        ConfiguredSecret? configuredMaterial,
        CancellationToken cancellationToken)
    {
        if (configuredMaterial is null || string.IsNullOrWhiteSpace(configuredMaterial.SecretReference))
        {
            return TrustAnchorLoadResult.Failed(CertificateMaterialFailure.MaterialMissing);
        }

        var materialResult = await this.secretReferenceResolver.ResolveAsync(
            configuredMaterial.SecretReference,
            cancellationToken);

        if (materialResult.Secret is not { } material)
        {
            return TrustAnchorLoadResult.Failed(CertificateMaterialFailure.SecretNotResolvable);
        }

        using (material)
        {
            var bundlePasswordResult = await this.ResolveBundlePasswordAsync(
                configuredMaterial.Password,
                cancellationToken);

            using (bundlePasswordResult.BundlePassword)
            {
                return bundlePasswordResult.Resolvable
                    ? LoadTrustAnchor(material, materialResult.Source, bundlePasswordResult.BundlePassword)
                    : TrustAnchorLoadResult.Failed(CertificateMaterialFailure.SecretNotResolvable);
            }
        }
    }

    private async Task<(bool Resolvable, ResolvedSecret? BundlePassword)> ResolveBundlePasswordAsync(
        ConfiguredSecret? configuredBundlePassword,
        CancellationToken cancellationToken)
    {
        if (configuredBundlePassword is null || string.IsNullOrWhiteSpace(configuredBundlePassword.SecretReference))
        {
            return (Resolvable: true, BundlePassword: null);
        }

        var result = await this.secretReferenceResolver.ResolveAsync(
            configuredBundlePassword.SecretReference,
            cancellationToken);

        return (result.Succeeded, result.Secret);
    }

    private static TrustAnchorLoadResult LoadTrustAnchor(
        ResolvedSecret material,
        SecretMaterialSource? source,
        ResolvedSecret? bundlePassword)
    {
        var encoding = CertificateMaterialEncodingDetector.Detect(material.RevealBytes());

        if (encoding == CertificateMaterialEncoding.Unrecognized)
        {
            return TrustAnchorLoadResult.Failed(CertificateMaterialFailure.EncodingNotRecognized);
        }

        // DER and PKCS#12 are binary and have no faithful representation in a configuration value, so material that
        // arrived as the configured value itself can only be PEM. Reporting the encoding here is what turns an
        // otherwise cryptic parse failure into the mistake an operator actually made.
        if (source == SecretMaterialSource.InlineValue && encoding != CertificateMaterialEncoding.Pem)
        {
            return TrustAnchorLoadResult.Failed(CertificateMaterialFailure.InlineEncodingNotSupported);
        }

        return encoding == CertificateMaterialEncoding.Pkcs12
            ? LoadFromBundle(material, bundlePassword)
            : LoadFromCertificate(material);
    }

    private static TrustAnchorLoadResult LoadFromCertificate(ResolvedSecret material)
    {
        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadCertificate(material.RevealBytes());
        }
        catch (CryptographicException)
        {
            return TrustAnchorLoadResult.Failed(CertificateMaterialFailure.MaterialNotReadable);
        }

        return RejectPrivateKeyBearingAnchor(certificate);
    }

    /// <summary>Extracts the anchor from a PKCS#12 bundle, whose password is supplied as characters rather than as a string.</summary>
    /// <remarks>
    /// The password buffer is allocated pinned and erased in the same method that fills it, so the one secret this
    /// loader touches never reaches an un-erasable <see cref="string" />. A bundle that holds several certificates is
    /// read for its first one: trust comes from a single anchor, and an intermediate a server needs is supplied by the
    /// handshake rather than by the anchor bundle.
    /// </remarks>
    private static TrustAnchorLoadResult LoadFromBundle(ResolvedSecret material, ResolvedSecret? bundlePassword)
    {
        var passwordBuffer = bundlePassword is null
            ? []
            : GC.AllocateArray<char>(bundlePassword.TextLength, pinned: true);

        try
        {
            bundlePassword?.RevealTextInto(passwordBuffer);

            return LoadFromBundle(material.RevealBytes(), passwordBuffer, bundlePassword is not null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(passwordBuffer.AsSpan()));
        }
    }

    /// <summary>Opens the bundle and names the configured password as the cause when one was supplied.</summary>
    /// <remarks>
    /// The platform reports a wrong password, a missing one, and corrupt contents through the same exception, so the
    /// distinction is drawn from what was configured rather than from what was reported. It is a diagnostic
    /// refinement, not a claim about the material: an operator who supplied a password reads that the password did not
    /// open the bundle, and one who supplied none reads that the bundle wanted one.
    /// </remarks>
    private static TrustAnchorLoadResult LoadFromBundle(
        ReadOnlySpan<byte> material,
        ReadOnlySpan<char> bundlePassword,
        bool bundlePasswordConfigured)
    {
        X509Certificate2Collection bundle;
        try
        {
            bundle = X509CertificateLoader.LoadPkcs12Collection(
                material,
                bundlePassword,
                X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (CryptographicException)
        {
            return TrustAnchorLoadResult.Failed(bundlePasswordConfigured
                ? CertificateMaterialFailure.BundlePasswordIncorrect
                : CertificateMaterialFailure.BundlePasswordMissing);
        }

        if (bundle.Count == 0)
        {
            return TrustAnchorLoadResult.Failed(CertificateMaterialFailure.BundleCarriesNoCertificate);
        }

        foreach (var surplusCertificate in bundle.Skip(1))
        {
            surplusCertificate.Dispose();
        }

        return RejectPrivateKeyBearingAnchor(bundle[0]);
    }

    private static TrustAnchorLoadResult RejectPrivateKeyBearingAnchor(X509Certificate2 candidateAnchor)
    {
        if (!candidateAnchor.HasPrivateKey)
        {
            return TrustAnchorLoadResult.Loaded(candidateAnchor);
        }

        candidateAnchor.Dispose();

        return TrustAnchorLoadResult.Failed(CertificateMaterialFailure.TrustAnchorCarriesPrivateKey);
    }
}
