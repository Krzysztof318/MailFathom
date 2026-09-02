// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Security.ClientCertificates;

/// <summary>The outcome of judging the certificate a TLS connection carried against the configured trust profiles.</summary>
/// <remarks>
/// Two questions are answered rather than one, and they are independent. <see cref="Succeeded" /> says whether the
/// request is served, and <see cref="MatchedProfileName" /> says which client application was identified — which is
/// nobody when a served request presented no certificate and every profile was content without one. Reading the second
/// as the answer to the first would report an unidentified client as a refused one.
/// </remarks>
public sealed record McpClientCertificateAuthenticationResult
{
    private McpClientCertificateAuthenticationResult(string? matchedProfileName, McpClientCertificateRejection? rejection)
    {
        this.MatchedProfileName = matchedProfileName;
        this.Rejection = rejection;
    }

    /// <summary>Gets the result of a request no profile required a certificate of, which identifies no client application.</summary>
    public static McpClientCertificateAuthenticationResult AcceptedWithoutCertificate { get; } =
        new(matchedProfileName: null, rejection: null);

    /// <summary>Gets whether the request is served.</summary>
    public bool Succeeded => this.Rejection is null;

    /// <summary>Gets the name of the profile whose client the certificate identified, or <see langword="null" /> when no certificate identified one.</summary>
    public string? MatchedProfileName { get; }

    /// <summary>Gets why the certificate was refused, or <see langword="null" /> when the request is served.</summary>
    /// <remarks>It reaches the server log only. Every value produces the same response to the caller.</remarks>
    public McpClientCertificateRejection? Rejection { get; }

    /// <summary>Creates a successful result naming the profile the certificate matched.</summary>
    /// <param name="matchedProfileName">The name of the matching profile.</param>
    /// <returns>The successful result.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="matchedProfileName" /> names no profile.</exception>
    public static McpClientCertificateAuthenticationResult AcceptedByProfile(string matchedProfileName) =>
        string.IsNullOrEmpty(matchedProfileName)
            ? throw new ArgumentException(
                "A result accepted by a profile must name that profile.",
                nameof(matchedProfileName))
            : new McpClientCertificateAuthenticationResult(matchedProfileName, rejection: null);

    /// <summary>Creates a refused result.</summary>
    /// <param name="rejection">Why the certificate was refused.</param>
    /// <returns>The refused result.</returns>
    public static McpClientCertificateAuthenticationResult Rejected(McpClientCertificateRejection rejection) =>
        new(matchedProfileName: null, rejection);
}
