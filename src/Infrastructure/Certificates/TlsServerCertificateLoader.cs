// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MailMcp.Infrastructure.Secrets;

namespace MailMcp.Infrastructure.Certificates;

/// <summary>Turns deployment-provisioned material into the TLS identity an endpoint presents, or into a named refusal.</summary>
/// <remarks>
/// <para>
/// This is the server-identity counterpart of <see cref="TrustAnchorLoader" /> and shares its rules: X.509 knowledge
/// stays here rather than spreading into the scheme adapters, material is erased as soon as it has been parsed, and an
/// unusable configuration produces a named failure rather than an exception.
/// </para>
/// <para>
/// Every private key is imported with <see cref="X509KeyStorageFlags.EphemeralKeySet" />, so provisioning a certificate
/// never leaves a copy of its key in an operating-system key store. That is the one place where this loader inverts the
/// trust-anchor rule: an anchor carrying a private key is rejected, while a server identity without one is, because a
/// server that cannot sign the handshake cannot prove it is the domain it claims.
/// </para>
/// </remarks>
public sealed class TlsServerCertificateLoader
{
    private readonly ISecretReferenceResolver secretReferenceResolver;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a new server certificate loader.</summary>
    /// <param name="secretReferenceResolver">The resolver that turns a configured reference into material.</param>
    /// <param name="timeProvider">The clock the certificate's validity period is read against.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public TlsServerCertificateLoader(ISecretReferenceResolver secretReferenceResolver, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(secretReferenceResolver);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.secretReferenceResolver = secretReferenceResolver;
        this.timeProvider = timeProvider;
    }

    /// <summary>Loads and validates the identity an endpoint presents for one domain.</summary>
    /// <param name="configuredMaterial">The block naming where the bundle, or the chain and its private key, come from.</param>
    /// <param name="domain">The exact DNS domain the endpoint publishes, in its ASCII form.</param>
    /// <param name="cancellationToken">Cancels the material retrieval and every password retrieval it needs.</param>
    /// <returns>The loaded identity, whose ownership passes to the caller, or a named failure.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="domain" /> is <see langword="null" />, empty, or whitespace.</exception>
    /// <remarks>
    /// The material is erased before this returns, whether loading succeeded or not, so the identity outlives the bytes
    /// it was parsed from. A returned identity has already been proven usable for <paramref name="domain" />: nothing
    /// downstream re-checks its validity period, its names, or its key usage.
    /// </remarks>
    public async Task<TlsServerCertificateLoadResult> LoadAsync(
        TlsServerCertificateOptions? configuredMaterial,
        string domain,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        var bundleConfigured = IsConfigured(configuredMaterial?.Bundle);
        var pemConfigured = IsConfigured(configuredMaterial?.CertificateChain)
            || IsConfigured(configuredMaterial?.PrivateKey);

        if (bundleConfigured && pemConfigured)
        {
            return TlsServerCertificateLoadResult.Failed(CertificateMaterialFailure.MaterialKindAmbiguous);
        }

        if (bundleConfigured)
        {
            return await this.LoadFromBundleAsync(configuredMaterial!.Bundle!, domain, cancellationToken);
        }

        if (pemConfigured)
        {
            return await this.LoadFromPemAsync(configuredMaterial!, domain, cancellationToken);
        }

        return TlsServerCertificateLoadResult.Failed(CertificateMaterialFailure.MaterialMissing);
    }

    private static bool IsConfigured(ConfiguredSecret? block) =>
        block is not null && !string.IsNullOrWhiteSpace(block.SecretReference);

    private async Task<TlsServerCertificateLoadResult> LoadFromBundleAsync(
        ConfiguredSecret configuredBundle,
        string domain,
        CancellationToken cancellationToken)
    {
        var bundleResult = await this.secretReferenceResolver.ResolveAsync(
            configuredBundle.SecretReference,
            cancellationToken);

        if (bundleResult.Secret is not { } bundleMaterial)
        {
            return TlsServerCertificateLoadResult.Failed(CertificateMaterialFailure.SecretNotResolvable);
        }

        using (bundleMaterial)
        {
            // A PKCS#12 bundle is binary and has no faithful representation in a configuration value, so material that
            // arrived as the configured value itself cannot be one however it parses.
            if (bundleResult.Source == SecretMaterialSource.InlineValue)
            {
                return TlsServerCertificateLoadResult.Failed(CertificateMaterialFailure.InlineEncodingNotSupported);
            }

            var encoding = CertificateMaterialEncodingDetector.Detect(bundleMaterial.RevealBytes());

            if (encoding == CertificateMaterialEncoding.Unrecognized)
            {
                return TlsServerCertificateLoadResult.Failed(CertificateMaterialFailure.EncodingNotRecognized);
            }

            if (encoding != CertificateMaterialEncoding.Pkcs12)
            {
                return TlsServerCertificateLoadResult.Failed(CertificateMaterialFailure.EncodingNotSupportedForRole);
            }

            var passwordResult = await this.ResolvePasswordAsync(configuredBundle.Password, cancellationToken);

            using (passwordResult.Password)
            {
                if (!passwordResult.Resolvable)
                {
                    return TlsServerCertificateLoadResult.Failed(CertificateMaterialFailure.SecretNotResolvable);
                }

                return this.OpenBundle(bundleMaterial, passwordResult.Password, domain);
            }
        }
    }

    /// <summary>Opens the bundle with the password revealed into a buffer this method erases before it returns.</summary>
    /// <remarks>
    /// The buffer is allocated pinned and erased in the same method that fills it, so the bundle password never reaches
    /// an un-erasable <see cref="string" />. The platform reports a wrong password, a missing one, and corrupt contents
    /// through one exception, so the two failures are told apart by what was configured rather than by what was
    /// reported.
    /// </remarks>
    private TlsServerCertificateLoadResult OpenBundle(
        ResolvedSecret bundleMaterial,
        ResolvedSecret? bundlePassword,
        string domain)
    {
        var passwordBuffer = bundlePassword is null
            ? []
            : GC.AllocateArray<char>(bundlePassword.TextLength, pinned: true);

        try
        {
            bundlePassword?.RevealTextInto(passwordBuffer);

            X509Certificate2Collection bundle;
            try
            {
                bundle = X509CertificateLoader.LoadPkcs12Collection(
                    bundleMaterial.RevealBytes(),
                    passwordBuffer,
                    X509KeyStorageFlags.EphemeralKeySet);
            }
            catch (CryptographicException)
            {
                return TlsServerCertificateLoadResult.Failed(bundlePassword is not null
                    ? CertificateMaterialFailure.BundlePasswordIncorrect
                    : CertificateMaterialFailure.BundlePasswordMissing);
            }

            return this.AssembleFromBundle(bundle, domain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(passwordBuffer.AsSpan()));
        }
    }

    /// <summary>Picks the identity out of a bundle by which certificate holds a private key.</summary>
    /// <remarks>
    /// Position is not used, because export tools disagree about ordering. Exactly one key-bearing certificate is the
    /// identity and the rest are the chain; several make which identity the endpoint presents depend on parse order.
    /// </remarks>
    private TlsServerCertificateLoadResult AssembleFromBundle(X509Certificate2Collection bundle, string domain)
    {
        if (bundle.Count == 0)
        {
            return DisposeAndFail(bundle, CertificateMaterialFailure.BundleCarriesNoCertificate);
        }

        var identities = bundle.Where(static candidate => candidate.HasPrivateKey).ToArray();

        if (identities.Length == 0)
        {
            return DisposeAndFail(bundle, CertificateMaterialFailure.PrivateKeyMissing);
        }

        if (identities.Length > 1)
        {
            return DisposeAndFail(bundle, CertificateMaterialFailure.ChainCarriesSeveralLeaves);
        }

        var leaf = identities[0];

        return this.Accept(leaf, [.. bundle.Where(candidate => !ReferenceEquals(candidate, leaf))], domain);
    }

    private async Task<TlsServerCertificateLoadResult> LoadFromPemAsync(
        TlsServerCertificateOptions configuredMaterial,
        string domain,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured(configuredMaterial.CertificateChain))
        {
            return TlsServerCertificateLoadResult.Failed(CertificateMaterialFailure.MaterialMissing);
        }

        if (!IsConfigured(configuredMaterial.PrivateKey))
        {
            return TlsServerCertificateLoadResult.Failed(CertificateMaterialFailure.PrivateKeyMissing);
        }

        var chainResult = await this.secretReferenceResolver.ResolveAsync(
            configuredMaterial.CertificateChain!.SecretReference,
            cancellationToken);

        if (chainResult.Secret is not { } chainMaterial)
        {
            return TlsServerCertificateLoadResult.Failed(CertificateMaterialFailure.SecretNotResolvable);
        }

        using (chainMaterial)
        {
            var keyResult = await this.secretReferenceResolver.ResolveAsync(
                configuredMaterial.PrivateKey!.SecretReference,
                cancellationToken);

            if (keyResult.Secret is not { } keyMaterial)
            {
                return TlsServerCertificateLoadResult.Failed(CertificateMaterialFailure.SecretNotResolvable);
            }

            using (keyMaterial)
            {
                var keyPasswordResult = await this.ResolvePasswordAsync(
                    configuredMaterial.PrivateKey.Password,
                    cancellationToken);

                using (keyPasswordResult.Password)
                {
                    if (!keyPasswordResult.Resolvable)
                    {
                        return TlsServerCertificateLoadResult.Failed(CertificateMaterialFailure.SecretNotResolvable);
                    }

                    return this.AssembleFromPem(chainMaterial, keyMaterial, keyPasswordResult.Password, domain);
                }
            }
        }
    }

    /// <summary>Parses the PEM chain, pairs its leaf with the configured private key, and validates the result.</summary>
    /// <remarks>
    /// The chain is public material and is decoded into an ordinary buffer, while the key and its password are decoded
    /// into pinned buffers this method erases before it returns.
    /// </remarks>
    private TlsServerCertificateLoadResult AssembleFromPem(
        ResolvedSecret chainMaterial,
        ResolvedSecret keyMaterial,
        ResolvedSecret? keyPassword,
        string domain)
    {
        if (CertificateMaterialEncodingDetector.Detect(chainMaterial.RevealBytes()) is var chainEncoding
            && chainEncoding != CertificateMaterialEncoding.Pem)
        {
            return TlsServerCertificateLoadResult.Failed(chainEncoding == CertificateMaterialEncoding.Unrecognized
                ? CertificateMaterialFailure.EncodingNotRecognized
                : CertificateMaterialFailure.EncodingNotSupportedForRole);
        }

        var chainText = new char[chainMaterial.TextLength];
        chainMaterial.RevealTextInto(chainText);

        var chain = new X509Certificate2Collection();
        try
        {
            chain.ImportFromPem(chainText);
        }
        catch (CryptographicException)
        {
            return TlsServerCertificateLoadResult.Failed(CertificateMaterialFailure.MaterialNotReadable);
        }

        if (chain.Count == 0)
        {
            return TlsServerCertificateLoadResult.Failed(CertificateMaterialFailure.MaterialNotReadable);
        }

        var parsedLeaf = chain[0];

        if (chain.Count(candidate => string.Equals(candidate.Subject, parsedLeaf.Subject, StringComparison.Ordinal)) > 1)
        {
            return DisposeAndFail(chain, CertificateMaterialFailure.ChainCarriesSeveralLeaves);
        }

        var pairing = this.PairWithPrivateKey(chainText, keyMaterial, keyPassword, domain, chain);

        return pairing;
    }

    /// <summary>Attaches the configured private key to the parsed leaf, revealing the key only into buffers it erases.</summary>
    /// <remarks>
    /// The platform reports a malformed key and a key belonging to a different certificate through one exception, so
    /// the key is parsed on its own afterwards to tell the two apart. That distinction is worth the second parse: one
    /// failure means the file is damaged and the other means the wrong file was provisioned, and an operator fixes them
    /// differently.
    /// </remarks>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the paired leaf passes to the accepting call, which disposes it when the identity it built is rejected.")]
    private TlsServerCertificateLoadResult PairWithPrivateKey(
        char[] chainText,
        ResolvedSecret keyMaterial,
        ResolvedSecret? keyPassword,
        string domain,
        X509Certificate2Collection chain)
    {
        var keyText = GC.AllocateArray<char>(keyMaterial.TextLength, pinned: true);
        var keyPasswordText = keyPassword is null
            ? []
            : GC.AllocateArray<char>(keyPassword.TextLength, pinned: true);

        try
        {
            keyMaterial.RevealTextInto(keyText);
            keyPassword?.RevealTextInto(keyPasswordText);

            X509Certificate2 leafWithPrivateKey;
            try
            {
                leafWithPrivateKey = keyPassword is null
                    ? X509Certificate2.CreateFromPem(chainText, keyText)
                    : X509Certificate2.CreateFromEncryptedPem(chainText, keyText, keyPasswordText);
            }
            catch (Exception failure) when (failure is CryptographicException or ArgumentException)
            {
                // Both are caught because the platform reaches the same outcome through two paths: material it cannot
                // read is a cryptographic failure, while a well-formed key belonging elsewhere surfaces from
                // CopyWithPrivateKey as an argument failure. Neither exception says which happened, so the key is
                // parsed on its own to decide.
                return DisposeAndFail(chain, PrivateKeyParses(keyText, keyPasswordText, keyPassword is not null)
                    ? CertificateMaterialFailure.PrivateKeyDoesNotMatchCertificate
                    : CertificateMaterialFailure.PrivateKeyNotReadable);
            }

            var intermediates = chain.Skip(1).ToArray();
            chain[0].Dispose();

            return this.Accept(leafWithPrivateKey, intermediates, domain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(keyText.AsSpan()));
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(keyPasswordText.AsSpan()));
        }
    }

    /// <summary>Tells a damaged private key apart from one that simply belongs elsewhere.</summary>
    /// <remarks>
    /// The three algorithms are tried rather than inferred from the certificate, because a key that parses as any of
    /// them is a well-formed key and the pairing failure was therefore a mismatch. Each import is attempted on its own
    /// instance, since a failed import leaves the algorithm object in an unspecified state.
    /// </remarks>
    private static bool PrivateKeyParses(
        ReadOnlySpan<char> keyText,
        ReadOnlySpan<char> keyPassword,
        bool encrypted)
    {
        return ImportsInto(RSA.Create(), keyText, keyPassword, encrypted)
            || ImportsInto(ECDsa.Create(), keyText, keyPassword, encrypted)
            || ImportsInto(DSA.Create(), keyText, keyPassword, encrypted);
    }

    private static bool ImportsInto(
        AsymmetricAlgorithm algorithm,
        ReadOnlySpan<char> keyText,
        ReadOnlySpan<char> keyPassword,
        bool encrypted)
    {
        using (algorithm)
        {
            try
            {
                if (encrypted)
                {
                    algorithm.ImportFromEncryptedPem(keyText, keyPassword);
                }
                else
                {
                    algorithm.ImportFromPem(keyText);
                }

                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }
    }

    /// <summary>Publishes the assembled identity, or disposes it when it cannot serve the configured domain.</summary>
    /// <remarks>
    /// The chain is ordered here rather than taken as it arrived, because neither source states an order a client can
    /// rely on: a PKCS#12 bundle states none at all, and a PEM file states whichever one it was concatenated in.
    /// </remarks>
    private TlsServerCertificateLoadResult Accept(
        X509Certificate2 leaf,
        X509Certificate2[] intermediates,
        string domain)
    {
        var evaluatedAt = this.timeProvider.GetUtcNow();
        var chainOrder = TlsServerCertificateSuitability.OrderTowardsRoot(leaf, intermediates, evaluatedAt);

        if (chainOrder.Unsuitability is { } chainFailure)
        {
            // Released here rather than through an identity, because a chain that was refused never became one.
            DisposeAll(leaf, intermediates);

            return TlsServerCertificateLoadResult.Failed(chainFailure);
        }

        var certificate = new TlsServerCertificate(leaf, chainOrder.Intermediates);
        var unsuitability = TlsServerCertificateSuitability.FindUnsuitability(leaf, domain, evaluatedAt);

        if (unsuitability is not { } failure)
        {
            return TlsServerCertificateLoadResult.Loaded(certificate);
        }

        certificate.Dispose();

        return TlsServerCertificateLoadResult.Failed(failure);
    }

    private static void DisposeAll(X509Certificate2 leaf, X509Certificate2[] intermediates)
    {
        leaf.Dispose();

        foreach (var intermediate in intermediates)
        {
            intermediate.Dispose();
        }
    }

    private async Task<(bool Resolvable, ResolvedSecret? Password)> ResolvePasswordAsync(
        ConfiguredSecret? configuredPassword,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured(configuredPassword))
        {
            return (Resolvable: true, Password: null);
        }

        var result = await this.secretReferenceResolver.ResolveAsync(
            configuredPassword!.SecretReference,
            cancellationToken);

        return (result.Succeeded, result.Secret);
    }

    private static TlsServerCertificateLoadResult DisposeAndFail(
        X509Certificate2Collection parsed,
        CertificateMaterialFailure failure)
    {
        foreach (var certificate in parsed)
        {
            certificate.Dispose();
        }

        return TlsServerCertificateLoadResult.Failed(failure);
    }
}
