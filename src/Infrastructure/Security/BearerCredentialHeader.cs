// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Security;

/// <summary>Reads the credential out of an HTTP <c>Authorization</c> header.</summary>
/// <remarks>
/// Both credentials the MCP endpoint accepts arrive the same way, as an RFC 6750 bearer credential, so both are lifted
/// out of the header by this one parser. Keeping it in one place matters more than its size: an API key and an access
/// token that disagreed about what counts as a well-formed header would be two different definitions of "the request
/// presented nothing", and a request could then be malformed to one and a credential to the other.
/// </remarks>
public static class BearerCredentialHeader
{
    private const string BearerScheme = "Bearer";

    /// <summary>Reads the credential an <c>Authorization</c> header value carried.</summary>
    /// <param name="authorizationHeaderValue">The raw header value, or <see langword="null" /> when the request carried none.</param>
    /// <param name="credential">The credential when the header carried one; otherwise an empty string.</param>
    /// <returns><see langword="true" /> when the header carried a bearer credential; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// The scheme is matched ignoring case, as HTTP requires, and at least one space separates it from the credential.
    /// Anything else — another scheme, a scheme with no credential, a bare token — is malformed rather than a credential
    /// worth judging, and the caller refuses it with the same response as every other rejection.
    /// </remarks>
    public static bool TryRead(string? authorizationHeaderValue, out string credential)
    {
        credential = string.Empty;

        if (authorizationHeaderValue is null)
        {
            return false;
        }

        var headerValue = authorizationHeaderValue.AsSpan().Trim();

        if (!headerValue.StartsWith(BearerScheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var presentedCredential = headerValue[BearerScheme.Length..].TrimStart(' ');

        if (presentedCredential.Length == headerValue.Length - BearerScheme.Length || presentedCredential.IsEmpty)
        {
            return false;
        }

        credential = presentedCredential.ToString();

        return true;
    }
}
