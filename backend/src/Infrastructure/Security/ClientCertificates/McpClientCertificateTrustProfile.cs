// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.RegularExpressions;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Secrets.Discovery;

namespace MailFathom.Infrastructure.Security.ClientCertificates;

/// <summary>One client application a deployment trusts, and the certificate policy that identifies it.</summary>
/// <remarks>
/// <para>
/// A profile is named after the client rather than after the authority that signed for it, because that is what an
/// operator reasons about: the ChatGPT connector, the reporting service, the workstation fleet. Several profiles stand
/// beside each other so one client's authority rotating, widening, or being withdrawn changes what that client may
/// present and nothing about the others — which one global certificate setting could not express.
/// </para>
/// <para>
/// The trust anchors are configured references rather than loaded certificates, so the material is retrieved when a
/// request is judged rather than held for the life of the process. Rotating an authority is then adding its successor
/// beside it and removing the predecessor once clients have moved, with both accepted in between and no restart in the
/// middle. Anchors are public certificates and are recorded by subject and thumbprint; the reference machinery carries
/// them for provisioning and erasure alone, and their validity is the X.509 one rather than a configured lifetime.
/// </para>
/// <para>
/// A profile always names the client it identifies through <see cref="ExpectedDnsNames" />. A trusted authority on its
/// own would accept every certificate that authority has ever issued, which for a public or shared certificate
/// authority is a great many clients that are not this deployment's.
/// </para>
/// </remarks>
public sealed partial class McpClientCertificateTrustProfile
{
    /// <summary>The greatest number of characters a profile name may carry.</summary>
    public const int MaximumNameLength = 64;

    private McpClientCertificateTrustProfile(
        string name,
        McpClientCertificateRequirement requirement,
        IReadOnlyList<ConfiguredSecret> trustAnchors,
        IReadOnlyCollection<string> expectedDnsNames)
    {
        this.Name = name;
        this.Requirement = requirement;
        this.TrustAnchors = trustAnchors;
        this.ExpectedDnsNames = expectedDnsNames;
    }

    /// <summary>Gets the operator-chosen name of the client this profile identifies, which is safe to record.</summary>
    public string Name { get; }

    /// <summary>Gets whether a request that presents no certificate at all is refused.</summary>
    public McpClientCertificateRequirement Requirement { get; }

    /// <summary>Gets the configured certificate authorities a presented certificate must chain to, several of them so an authority can be rotated by overlap.</summary>
    public IReadOnlyList<ConfiguredSecret> TrustAnchors { get; }

    /// <summary>Gets the DNS names of which a presented certificate must carry at least one as a subject alternative name.</summary>
    /// <remarks>Membership is answered without regard to case, which is what a host name is.</remarks>
    public IReadOnlyCollection<string> ExpectedDnsNames { get; }

    /// <summary>Reports whether a configured value may name a profile.</summary>
    /// <param name="configuredValue">The bound name.</param>
    /// <returns><see langword="true" /> when the value is an accepted name; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// The accepted characters are narrow for the reason <see cref="SecretName" /> gives: the name reaches a log line
    /// and an audit record, where escaping or truncation must not be what decides its meaning.
    /// </remarks>
    public static bool IsAcceptedName(string? configuredValue) =>
        configuredValue is { Length: <= MaximumNameLength } && AcceptedName.IsMatch(configuredValue);

    /// <summary>Reports whether a configured value may be an expected subject alternative name.</summary>
    /// <param name="configuredValue">The bound value.</param>
    /// <returns><see langword="true" /> when the value is a DNS name a certificate could carry; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// Only DNS names are compared. A certificate for a client application names it by host name — OpenAI's connector
    /// presents <c>mtls.prod.connectors.openai.com</c> — and accepting an address or a URI here would invite a profile
    /// to be written against a name the comparison never reads.
    /// </remarks>
    public static bool IsAcceptedDnsName(string? configuredValue) =>
        configuredValue is not null && Uri.CheckHostName(configuredValue) == UriHostNameType.Dns;

    /// <summary>Creates a profile from settings that have already been validated.</summary>
    /// <param name="name">The operator-chosen name of the client this profile identifies.</param>
    /// <param name="requirement">Whether a request presenting no certificate is refused.</param>
    /// <param name="trustAnchors">The configured certificate authorities, at least one.</param>
    /// <param name="expectedDnsNames">The subject alternative names expected of a presented certificate, at least one.</param>
    /// <returns>The profile.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="trustAnchors" /> or <paramref name="expectedDnsNames" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when a value would leave the profile unable to identify a client, which means it was mapped before it was validated.</exception>
    public static McpClientCertificateTrustProfile Create(
        string name,
        McpClientCertificateRequirement requirement,
        IEnumerable<ConfiguredSecret> trustAnchors,
        IEnumerable<string> expectedDnsNames)
    {
        ArgumentNullException.ThrowIfNull(trustAnchors);
        ArgumentNullException.ThrowIfNull(expectedDnsNames);

        if (!IsAcceptedName(name))
        {
            throw new ArgumentException("A trust profile must carry an accepted name.", nameof(name));
        }

        var configuredAnchors = trustAnchors.ToArray();

        // A host name is case-insensitive, so the comparer carries that rather than a normalization pass: two spellings
        // of one name are one entry here for the same reason a certificate carrying either satisfies the profile.
        var expectedNames = expectedDnsNames.OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (configuredAnchors.Length == 0)
        {
            throw new ArgumentException(
                "A trust profile must name at least one certificate authority.",
                nameof(trustAnchors));
        }

        return expectedNames.Count > 0 && expectedNames.All(IsAcceptedDnsName)
            ? new McpClientCertificateTrustProfile(name, requirement, configuredAnchors, expectedNames)
            : throw new ArgumentException(
                "A trust profile must name at least one DNS name a presented certificate carries.",
                nameof(expectedDnsNames));
    }

    /// <summary>Reports whether a certificate carrying given DNS names is the client this profile identifies.</summary>
    /// <param name="presentedDnsNames">The subject alternative DNS names the certificate carries.</param>
    /// <returns><see langword="true" /> when at least one expected name is present; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="presentedDnsNames" /> is <see langword="null" />.</exception>
    /// <remarks>DNS names are compared without regard to case, which is what a host name is, and never by suffix: a profile names the client rather than a domain a client happens to sit under.</remarks>
    public bool NamesClient(IEnumerable<string> presentedDnsNames)
    {
        ArgumentNullException.ThrowIfNull(presentedDnsNames);

        return presentedDnsNames.Any(this.ExpectedDnsNames.Contains);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex AcceptedName { get; }
}
