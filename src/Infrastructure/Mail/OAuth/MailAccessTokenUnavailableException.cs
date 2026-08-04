// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Common.OAuth;
using MailFathom.Domain.Failures;

namespace MailFathom.Infrastructure.Mail.OAuth;

/// <summary>Indicates that an account's authorization server did not issue an access token its OAuth mechanisms require.</summary>
/// <remarks>
/// <para>
/// The payload names the account and the authorization server's own <c>error</c> code — <c>invalid_grant</c>,
/// <c>invalid_client</c>, and their siblings — which is what an operator needs to tell a revoked refresh token from a
/// mistyped client secret. It arrives from a machine this process does not own and RFC 6749 bounds neither its length
/// nor its content, so <see cref="AuthorizationServerErrorText" /> reduces it before it reaches a message. Nothing
/// else from the response travels: the <c>error_description</c> is free text an authorization server may populate with
/// the request it rejected, so it is read by nobody here.
/// </para>
/// <para>
/// The token endpoint address is deliberately absent. It is a host name, which
/// <see cref="MailFathomException" /> excludes from a message, and the account alias already identifies which
/// configured endpoint was called.
/// </para>
/// </remarks>
public sealed class MailAccessTokenUnavailableException : MailFathomException
{
    /// <summary>Initializes a failure the authorization server stated in its response.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <param name="authorizationServerErrorCode">The authorization server's RFC 6749 error code.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailAccessTokenUnavailableException(string accountId, string authorizationServerErrorCode)
        : base(DescribeUnavailableToken(accountId, AuthorizationServerErrorText.Sanitize(authorizationServerErrorCode)))
    {
        ArgumentNullException.ThrowIfNull(authorizationServerErrorCode);

        this.AccountId = accountId;

        // Sanitized on the way in rather than on the way out, so the payload a caller reads and the message an
        // operator reads cannot disagree, and neither can carry unbounded text from a server this process does not own.
        this.AuthorizationServerErrorCode = AuthorizationServerErrorText.Sanitize(authorizationServerErrorCode);
    }

    /// <summary>Initializes a failure that happened before any response could state one.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <param name="innerException">The transport or parsing failure underneath.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>The two constructors are the two outcomes: a refusal the server decided, and a request that never reached one. Retry classification reads exactly that distinction.</remarks>
    public MailAccessTokenUnavailableException(string accountId, Exception innerException)
        : base(DescribeUnavailableToken(accountId, authorizationServerErrorCode: null), innerException) =>
        this.AccountId = accountId;

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.MailAccessTokenUnavailable;

    /// <summary>Gets the local account identifier.</summary>
    public string AccountId { get; }

    /// <summary>Gets the authorization server's RFC 6749 error code, or <see langword="null" /> when it returned none.</summary>
    /// <remarks>Absence has its own meaning here: the request failed before any response body could be read, which is a transport fault rather than a rejected grant.</remarks>
    public string? AuthorizationServerErrorCode { get; }

    private static string DescribeUnavailableToken(string accountId, string? authorizationServerErrorCode)
    {
        ArgumentNullException.ThrowIfNull(accountId);

        return authorizationServerErrorCode is { } errorCode
            ? $"Account '{accountId}' was refused an access token by its authorization server [{errorCode}]."
            : $"Account '{accountId}' could not reach its authorization server for an access token.";
    }
}
