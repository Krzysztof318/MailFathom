// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Domain.Emails.Authentication;
using MimeKit;
using MimeKit.Cryptography;

namespace MailFathom.Infrastructure.Mail.Mime;

/// <summary>Reads a message's <c>Authentication-Results</c> headers with MimeKit, and believes none of them.</summary>
/// <remarks>
/// <para>
/// The adapter exists so that RFC 8601's grammar is parsed by a library while deciding what the parse means happens
/// above it, where it is unit-testable. Nothing here selects a header, weighs a method, or reads a property's meaning:
/// what crosses the boundary is the identifier each producing server stamped and the outcomes it wrote, in the order the
/// message carried them.
/// </para>
/// <para>
/// The ARC form of the header is deliberately not read. <c>ARC-Authentication-Results</c> preserves an upstream hop's
/// findings across forwarding, which is a claim a relay signed rather than something this mailbox's own server observed,
/// and the whole point of the trusted reading above is that only the latter counts. The spam signals read the ARC chain
/// separately, for a purpose that weighs claims instead of trusting one.
/// </para>
/// <para>
/// Everything the headers carry is personal data — an outcome's properties name a sending domain — so nothing here is
/// logged, including where a header fails to parse.
/// </para>
/// </remarks>
internal static class AuthenticationResultsHeaderReader
{
    /// <summary>Reads one message's <c>Authentication-Results</c> headers.</summary>
    /// <param name="message">The parsed message.</param>
    /// <returns>The headers, topmost first, each bounded and none of them interpreted.</returns>
    /// <remarks>
    /// A header longer than the bound is passed over unread rather than truncated, and one MimeKit cannot parse
    /// contributes nothing rather than failing the extraction. Both are hostile input by construction — the header is
    /// what an attacker writes to defeat the check — so the reading that follows sees one header fewer instead of a
    /// parse it has to defend against, and a message left with no trusted header carries the not-established verdict.
    /// </remarks>
    public static IReadOnlyList<AuthenticationResultsHeader> Read(MimeMessage message) =>
    [
        .. message.Headers
            .Where(static header => header.Id is HeaderId.AuthenticationResults)
            .Take(AuthenticationResultsHeader.MaximumHeadersPerMessage)
            .Select(static header => TryRead(header.Value))
            .OfType<AuthenticationResultsHeader>(),
    ];

    /// <summary>Parses one header's value, answering with nothing when it is over-long or malformed.</summary>
    private static AuthenticationResultsHeader? TryRead(string headerValue)
    {
        if (headerValue.Length > AuthenticationResultsHeader.MaximumHeaderValueLength
            || !AuthenticationResults.TryParse(Encoding.UTF8.GetBytes(headerValue), out var results))
        {
            return null;
        }

        return new AuthenticationResultsHeader(
            results.AuthenticationServiceIdentifier ?? string.Empty,
            [
                .. results.Results
                    .Take(AuthenticationResultsHeader.MaximumMethodsPerHeader)
                    .Select(static result => new ReportedAuthenticationMethod(
                        result.Method ?? string.Empty,
                        result.Result ?? string.Empty,
                        [
                            .. result.Properties
                                .Take(AuthenticationResultsHeader.MaximumPropertiesPerMethod)
                                .Select(static property => new ReportedAuthenticationProperty(
                                    property.PropertyType ?? string.Empty,
                                    property.Property ?? string.Empty,
                                    property.Value ?? string.Empty)),
                        ])),
            ]);
    }
}
