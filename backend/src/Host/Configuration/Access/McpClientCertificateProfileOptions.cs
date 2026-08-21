// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Security.ClientCertificates;

namespace MailFathom.Host.Configuration.Access;

/// <summary>One client application the endpoint accepts a certificate from, and what that certificate must be.</summary>
/// <remarks>
/// <para>
/// Mutual TLS is off unless profiles are configured, and it composes with whatever <see cref="McpEndpointOptions.Authentication" />
/// states rather than replacing it. A certificate names the program making a request; the API key, and later an OAuth
/// 2.1 token, is what names the person on whose behalf it is made.
/// </para>
/// <para>
/// The ChatGPT connector is configured as an ordinary profile rather than as a built-in mode: OpenAI publishes the
/// authority, the usage, and the name to expect, so the deployment states them and rotating that authority stays an
/// operator change. No third-party certificate ships in this repository, which is what keeps a rotation from becoming
/// a release.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class McpClientCertificateProfileOptions
{
    /// <summary>Gets or sets the operator-chosen name of the client this profile identifies, for example <c>chatgpt-connector</c>.</summary>
    /// <remarks>It is the identity a refusal, a rotation, and an audit record name, so it is required and unique within the section.</remarks>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets whether a request that presents no certificate at all is refused.</summary>
    /// <remarks>
    /// Nullable rather than defaulted, for the reason the authentication mode is: both candidate defaults are postures.
    /// Assuming the permissive one would leave a deployment believing certificates are required when they are not, and
    /// assuming the strict one would lock out every client of a deployment that added a profile for one of them.
    /// </remarks>
    public McpClientCertificateRequirement? Requirement { get; set; }

    /// <summary>Gets the certificate authorities a presented certificate must chain to.</summary>
    /// <remarks>
    /// Several entries so an authority rotates by overlap: add its successor, let clients move across, remove the
    /// predecessor. Each is an ordinary named secret block, which is how the material is provisioned and erased —
    /// the certificate itself is public, and its expiry is the X.509 one rather than the configured lifetime.
    /// </remarks>
    public IList<ConfiguredSecret> TrustAnchors { get; } = [];

    /// <summary>Gets the DNS names of which a presented certificate must carry at least one, for example <c>mtls.prod.connectors.openai.com</c>.</summary>
    public IList<string> SubjectAlternativeNames { get; } = [];

    /// <summary>Finds everything an operator must fix before this profile can judge a certificate.</summary>
    /// <returns>One message per faulty setting, relative to this profile, empty when the profile is usable.</returns>
    public IReadOnlyList<string> FindConfigurationErrors() =>
    [
        .. this.FindNameErrors(),
        .. this.FindRequirementErrors(),
        .. this.FindTrustAnchorErrors(),
        .. this.FindSubjectAlternativeNameErrors(),
    ];

    /// <summary>Maps the configured settings onto the profile a presented certificate is judged against.</summary>
    /// <returns>The trust profile.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the settings have not passed <see cref="FindConfigurationErrors" />.</exception>
    public McpClientCertificateTrustProfile ToTrustProfile()
    {
        try
        {
            return McpClientCertificateTrustProfile.Create(
                this.Name,
                this.Requirement ?? McpClientCertificateRequirement.Required,
                this.TrustAnchors,
                this.SubjectAlternativeNames);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                "A client certificate profile was mapped before it was validated, so it cannot identify a client.",
                exception);
        }
    }

    private IEnumerable<string> FindNameErrors()
    {
        if (!McpClientCertificateTrustProfile.IsAcceptedName(this.Name))
        {
            yield return $"{nameof(this.Name)} — a profile is named after the client it identifies; write up to {McpClientCertificateTrustProfile.MaximumNameLength} letters, digits, dots, dashes, and underscores, beginning with a letter or a digit.";
        }
    }

    private IEnumerable<string> FindRequirementErrors()
    {
        if (this.Requirement is not { } requirement)
        {
            yield return $"{nameof(this.Requirement)} — state '{nameof(McpClientCertificateRequirement.Required)}' when every client of this deployment presents a certificate, or '{nameof(McpClientCertificateRequirement.Optional)}' when this client does and others authenticate otherwise.";

            yield break;
        }

        // The binder accepts any number for an enum, and a value no member declares would be neither requirement while
        // reading as one, so a request presenting no certificate would be judged by a rule nobody wrote.
        if (!Enum.IsDefined(requirement))
        {
            yield return $"{nameof(this.Requirement)} — '{(int)requirement}' names no requirement; state '{nameof(McpClientCertificateRequirement.Required)}' or '{nameof(McpClientCertificateRequirement.Optional)}'.";
        }
    }

    private IEnumerable<string> FindTrustAnchorErrors()
    {
        if (this.TrustAnchors.Count == 0)
        {
            yield return $"{nameof(this.TrustAnchors)} — a profile trusts the certificate authority that signed for its client; configure at least one.";
        }
    }

    /// <summary>Reports a profile that would accept every certificate its authority ever issued.</summary>
    /// <remarks>
    /// At least one name is required rather than optional. An authority alone identifies no client — for a public or
    /// shared certificate authority it identifies most of the internet — so a profile that named none would be trusting
    /// whoever else that authority signs for, which is never what listing it meant.
    /// </remarks>
    private IEnumerable<string> FindSubjectAlternativeNameErrors()
    {
        if (this.SubjectAlternativeNames.Count == 0)
        {
            yield return $"{nameof(this.SubjectAlternativeNames)} — a profile names the client it identifies; configure at least one DNS name its certificate carries, because the authority alone would accept every certificate it has ever issued.";

            yield break;
        }

        foreach (var (index, configuredName) in this.SubjectAlternativeNames.Index())
        {
            if (!McpClientCertificateTrustProfile.IsAcceptedDnsName(configuredName))
            {
                yield return $"{nameof(this.SubjectAlternativeNames)}:{index} — '{configuredName}' is not a DNS name; write the host name a certificate carries as a subject alternative name, and nothing else.";
            }
        }
    }
}
