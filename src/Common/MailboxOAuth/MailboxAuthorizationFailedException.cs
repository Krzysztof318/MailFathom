// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Common.MailboxOAuth;

/// <summary>Indicates that an operator-driven authorization run did not produce a refresh token to provision.</summary>
/// <remarks>
/// The payload is the authorization server's own error code and nothing else. RFC 6749 and RFC 8628 both define that
/// value as a fixed vocabulary, which is what tells an operator that consent was declined
/// (<c>access_denied</c>), that the device code ran out (<c>expired_token</c>), or that the client registration is
/// wrong (<c>invalid_client</c>). Two values are MailFathom's own and named so they cannot collide with a registered
/// one: <c>no_refresh_token_issued</c> for a grant that authenticates once and would then strand the deployment, and
/// <c>device_authorization_incomplete</c> for a response missing the fields the flow needs.
/// </remarks>
public sealed class MailboxAuthorizationFailedException : MailFathomException
{
    /// <summary>Initializes a new authorization failure.</summary>
    /// <param name="authorizationServerErrorCode">The error code the authorization server returned, or one of the two MailFathom names.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="authorizationServerErrorCode" /> is <see langword="null" />.</exception>
    public MailboxAuthorizationFailedException(string authorizationServerErrorCode)
        : base(DescribeFailedAuthorization(AuthorizationServerErrorText.Sanitize(authorizationServerErrorCode))) =>
        // Sanitized on the way in, so the value an operator is shown carries no line breaks and no unbounded text from
        // a server this process does not own. See AuthorizationServerErrorText for what survives.
        this.AuthorizationServerErrorCode = AuthorizationServerErrorText.Sanitize(authorizationServerErrorCode);

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.MailboxAuthorizationFailed;

    /// <summary>Gets the error code the authorization server returned.</summary>
    public string AuthorizationServerErrorCode { get; }

    private static string DescribeFailedAuthorization(string sanitizedErrorCode) =>
        $"The mailbox authorization did not produce a refresh token [{sanitizedErrorCode}].";
}
