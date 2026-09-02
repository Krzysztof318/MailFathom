// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Transport;
using MailFathom.Infrastructure.Mail.OAuth;
using MailKit.Security;

namespace MailFathom.Infrastructure.Mail.MailKit;

/// <summary>Presents an account's access token to a mail server, renewing it once when the server rejects one.</summary>
/// <remarks>
/// Both protocols authenticate the same way and neither may present a refused token twice, so the sequence is written
/// once here rather than in each adapter. What the two do differ on is how a single presentation is bounded, which is
/// why the presentation arrives as a delegate: the mailbox adapter authenticates under the establishment attempt
/// budget alone, while the submission adapter gives every round trip to its server a stage budget of its own.
/// </remarks>
internal static class MailKitAccessTokenAuthentication
{
    /// <summary>Authenticates with the account's access token, renewing it once when the server refuses one this process believed was valid.</summary>
    /// <param name="accessTokenSource">Issues the token and replaces a rejected one.</param>
    /// <param name="tokenMechanism">The mechanism chosen from what the server advertised.</param>
    /// <param name="accountId">The account whose token is being presented.</param>
    /// <param name="userName">The mailbox the token acts for.</param>
    /// <param name="presentCredential">Runs one authentication round trip against the server, under whatever budget the caller bounds it with.</param>
    /// <param name="cancellationToken">Cancels acquiring the token and presenting it.</param>
    /// <returns>A task that completes when the server has accepted the account's token.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is <see langword="null" />.</exception>
    /// <exception cref="AuthenticationException">Thrown when the server refuses a token this call has already renewed once.</exception>
    /// <remarks>
    /// <para>
    /// The single retry is what separates an expired token from a rejected credential. A cached token can be refused
    /// for reasons no expiry instant predicts — it was revoked, the mailbox password changed, an administrator
    /// withdrew consent — and repeating the same value would spend the caller's budget on an answer that cannot
    /// change. Renewing once distinguishes the two: a fresh token that is also refused is a decision the authorization
    /// server and the mail server agree on, and it fails the attempt.
    /// </para>
    /// <para>
    /// Renewal is also the only thing that replaces the cached entry, so without it a rotated token would keep being
    /// presented until that entry expired on its own — which on a submission path means every send failing in the
    /// meantime.
    /// </para>
    /// <para>
    /// This is bounded and non-recursive by construction. There is exactly one renewal per call, and the token source
    /// has its own retry budget under a different dependency class, so no retry layer nests inside another.
    /// </para>
    /// </remarks>
    internal static async Task AuthenticateAsync(
        IMailAccessTokenSource accessTokenSource,
        MailAuthenticationMechanism tokenMechanism,
        string accountId,
        string userName,
        Func<SaslMechanism, CancellationToken, Task> presentCredential,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accessTokenSource);
        ArgumentNullException.ThrowIfNull(presentCredential);

        var accessToken = await accessTokenSource.GetAccessTokenAsync(accountId, cancellationToken);

        try
        {
            await presentCredential(
                MailKitTransportSecurityMapping.ToSaslMechanism(tokenMechanism, userName, accessToken.Value),
                cancellationToken);
        }
        catch (AuthenticationException)
        {
            var renewedToken = await accessTokenSource.RenewAccessTokenAsync(
                accountId,
                accessToken,
                cancellationToken);

            await presentCredential(
                MailKitTransportSecurityMapping.ToSaslMechanism(tokenMechanism, userName, renewedToken.Value),
                cancellationToken);
        }
    }
}
