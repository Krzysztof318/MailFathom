// Copyright © 2026 Krzysztof Kasprowicz

using System.Security.Cryptography.X509Certificates;
using MailMcp.Infrastructure.Certificates;
using Microsoft.Extensions.Logging;

namespace MailMcp.Infrastructure.Security;

/// <summary>Judges the certificate a TLS connection carried against the trust profiles a deployment configured.</summary>
/// <remarks>
/// <para>
/// This identifies a client <em>application</em> and nothing more. It is not end-user authentication, it does not
/// replace an API key, and a deployment that runs unauthenticated stays unauthenticated with every profile in place:
/// a certificate names the program making the request, never the person whose mail is being read.
/// </para>
/// <para>
/// Anchors are loaded per request rather than held, which is what makes an authority rotate without a restart, on the
/// same terms the API keys are resolved on. Nothing here is timing-sensitive the way a credential comparison is: a
/// certificate is public material a client sends in the clear, so profiles are evaluated in order and the first one
/// that accepts ends the walk.
/// </para>
/// <para>
/// A profile whose anchors have all become unloadable refuses the request rather than falling through to the next
/// profile's answer being the deployment's answer. Startup proved every anchor loads, so reaching that state means the
/// deployment changed underneath a running process, and widening what is accepted is never the right response to it.
/// </para>
/// </remarks>
public sealed partial class McpClientCertificateAuthenticator
{
    private readonly TrustAnchorLoader trustAnchorLoader;
    private readonly ILogger<McpClientCertificateAuthenticator> logger;

    /// <summary>Initializes a new client certificate authenticator.</summary>
    /// <param name="trustAnchorLoader">The loader that turns configured material into a trust anchor.</param>
    /// <param name="logger">The log a refusal and an unloadable anchor are recorded in.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="trustAnchorLoader" /> is <see langword="null" />.</exception>
    public McpClientCertificateAuthenticator(
        TrustAnchorLoader trustAnchorLoader,
        ILogger<McpClientCertificateAuthenticator> logger)
    {
        ArgumentNullException.ThrowIfNull(trustAnchorLoader);

        this.trustAnchorLoader = trustAnchorLoader;
        this.logger = logger;
    }

    /// <summary>Judges the certificate a connection presented.</summary>
    /// <param name="profiles">The trust profiles the deployment configured, in configuration order.</param>
    /// <param name="presentedCertificate">The certificate the TLS connection carried, or <see langword="null" /> when it carried none.</param>
    /// <param name="cancellationToken">Cancels the retrieval of the configured anchor material.</param>
    /// <returns>The profile whose client the certificate identified, or the reason it was refused.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="profiles" /> is <see langword="null" />.</exception>
    /// <remarks>The certificate is judged as the connection supplied it. No header is read here or anywhere else: a header naming a certificate is written by whoever sent the request.</remarks>
    public async Task<McpClientCertificateAuthenticationResult> AuthenticateAsync(
        IReadOnlyList<McpClientCertificateTrustProfile> profiles,
        X509Certificate2? presentedCertificate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        if (profiles.Count == 0)
        {
            return McpClientCertificateAuthenticationResult.AcceptedWithoutCertificate;
        }

        if (presentedCertificate is null)
        {
            return profiles.Any(profile => profile.Requirement == McpClientCertificateRequirement.Required)
                ? McpClientCertificateAuthenticationResult.Rejected(McpClientCertificateRejection.CertificateMissing)
                : McpClientCertificateAuthenticationResult.AcceptedWithoutCertificate;
        }

        McpClientCertificateRejection? firstObjection = null;

        // A loop rather than a query: each step awaits the retrieval of that profile's anchor material, and the walk
        // stops at the first profile that accepts.
        foreach (var profile in profiles)
        {
            var rejection = await this.FindRejectionAsync(profile, presentedCertificate, cancellationToken);

            if (rejection is null)
            {
                return McpClientCertificateAuthenticationResult.AcceptedByProfile(profile.Name);
            }

            this.LogProfileRefusedCertificate(profile.Name, rejection.Value, presentedCertificate.Thumbprint);
            firstObjection ??= rejection;
        }

        var reportedRejection = firstObjection ?? McpClientCertificateRejection.ChainNotTrusted;
        this.LogClientCertificateRefused(presentedCertificate.Thumbprint, reportedRejection, profiles.Count);

        return McpClientCertificateAuthenticationResult.Rejected(reportedRejection);
    }

    /// <summary>Judges one certificate against one profile.</summary>
    /// <remarks>
    /// The two checks the certificate answers on its own come first, so a certificate meant for a different profile is
    /// turned away before that profile's anchor material is retrieved at all.
    /// </remarks>
    private async Task<McpClientCertificateRejection?> FindRejectionAsync(
        McpClientCertificateTrustProfile profile,
        X509Certificate2 presentedCertificate,
        CancellationToken cancellationToken)
    {
        if (!McpClientCertificateChainValidator.CarriesClientAuthenticationUsage(presentedCertificate))
        {
            return McpClientCertificateRejection.ClientAuthenticationUsageMissing;
        }

        if (!profile.NamesClient(McpClientCertificateChainValidator.ReadSubjectAlternativeDnsNames(presentedCertificate)))
        {
            return McpClientCertificateRejection.SubjectAlternativeNameMismatch;
        }

        var loadedAnchors = await this.LoadTrustAnchorsAsync(profile, cancellationToken);

        try
        {
            return loadedAnchors.Count > 0
                ? McpClientCertificateChainValidator.FindChainRejection(
                    [.. loadedAnchors.Select(anchor => anchor.TrustAnchor!)],
                    presentedCertificate)
                : McpClientCertificateRejection.TrustAnchorUnavailable;
        }
        finally
        {
            foreach (var anchor in loadedAnchors)
            {
                anchor.Dispose();
            }
        }
    }

    /// <summary>Loads the anchors of one profile, keeping the ones that loaded.</summary>
    /// <remarks>
    /// An anchor that fails to load is recorded and skipped rather than failing the profile outright, because a profile
    /// carries several anchors precisely so an authority can be replaced by overlap, and half a rotation must not stop
    /// the certificates the other half still signs for. A profile whose anchors all fail refuses every certificate,
    /// which the caller reports.
    /// </remarks>
    private async Task<IReadOnlyList<TrustAnchorLoadResult>> LoadTrustAnchorsAsync(
        McpClientCertificateTrustProfile profile,
        CancellationToken cancellationToken)
    {
        var loadedAnchors = new List<TrustAnchorLoadResult>(profile.TrustAnchors.Count);

        foreach (var configuredAnchor in profile.TrustAnchors)
        {
            var loadResult = await this.trustAnchorLoader.LoadAsync(configuredAnchor, cancellationToken);

            if (loadResult.TrustAnchor is not null)
            {
                loadedAnchors.Add(loadResult);

                continue;
            }

            loadResult.Dispose();
            this.LogTrustAnchorUnavailable(profile.Name, loadResult.Failure);
        }

        return loadedAnchors;
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "The client certificate {ClientCertificateThumbprint} was not accepted by MCP trust profile "
            + "{TrustProfileName} [{Rejection}].")]
    private partial void LogProfileRefusedCertificate(
        string trustProfileName,
        McpClientCertificateRejection rejection,
        string clientCertificateThumbprint);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A request presented the client certificate {ClientCertificateThumbprint}, which none of the "
            + "{TrustProfileCount} configured MCP trust profiles accepted [{Rejection}]. The request was refused with "
            + "the same response as any other refusal.")]
    private partial void LogClientCertificateRefused(
        string clientCertificateThumbprint,
        McpClientCertificateRejection rejection,
        int trustProfileCount);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "A trust anchor configured for MCP trust profile {TrustProfileName} could not be loaded [{Failure}], "
            + "so it trusts nothing until the material is readable again. Startup validates this, which means the "
            + "configuration changed underneath the running process.")]
    private partial void LogTrustAnchorUnavailable(string trustProfileName, CertificateMaterialFailure? failure);
}
