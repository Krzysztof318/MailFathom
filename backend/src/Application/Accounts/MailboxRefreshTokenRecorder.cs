// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Accounts;

/// <summary>Takes the refresh token an operator authorized for one mailbox and stores it against that account.</summary>
/// <remarks>
/// <para>
/// This is the use case behind the administrative write route, and the only path by which a grant enters the store from
/// outside a token request. It exists as a use case rather than as endpoint code for the reason the account check
/// exists at all: what a grant may be written for is a rule about this deployment's accounts, not about HTTP.
/// </para>
/// <para>
/// The token is checked against the served accounts before anything is written. A grant stored for an account no
/// configuration names is a credential for a named mailbox owner that nothing will ever read and nobody knows is there,
/// so it is refused rather than kept — which is also what turns a mistyped account identifier into a failure at the
/// terminal rather than into an account that silently keeps failing to authenticate.
/// </para>
/// <para>
/// The caller keeps ownership of the token, as <see cref="IMailboxRefreshTokenStore.SaveTokenAsync" /> requires, and
/// nothing here copies it into a value with a longer life than the request.
/// </para>
/// </remarks>
public sealed class MailboxRefreshTokenRecorder
{
    private readonly IDeploymentMailAccountCatalog accountCatalog;
    private readonly IMailboxRefreshTokenStore refreshTokenStore;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the use case over the accounts this deployment serves and the store that seals a token.</summary>
    /// <param name="accountCatalog">Names the accounts a grant may be recorded for.</param>
    /// <param name="refreshTokenStore">Where the token is written.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailboxRefreshTokenRecorder(
        IDeploymentMailAccountCatalog accountCatalog,
        IMailboxRefreshTokenStore refreshTokenStore,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(accountCatalog);
        ArgumentNullException.ThrowIfNull(refreshTokenStore);
        ArgumentNullException.ThrowIfNull(authorization);

        this.accountCatalog = accountCatalog;
        this.refreshTokenStore = refreshTokenStore;
        this.authorization = authorization;
    }

    /// <summary>Records the refresh token for one served account, replacing whatever was stored for it before.</summary>
    /// <param name="accountId">The account the grant acts for.</param>
    /// <param name="refreshToken">The token to record. The caller keeps ownership of it.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes after durable storage.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="refreshToken" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.AdminCredentialsWrite" />.</exception>
    /// <exception cref="MailAccountNotAccessibleException">Thrown when this deployment serves no account with that identifier.</exception>
    /// <remarks>
    /// Recording is idempotent in the account, because re-authorizing a mailbox is what an operator does after a
    /// revocation and a second grant beside the first would leave the account holding a credential nothing chose
    /// between. The store owns that guarantee; this use case adds only the account check in front of it.
    /// <para>
    /// The grant is asked for first, and before the account check, so that a caller who may not write a credential
    /// cannot learn from the refusal which accounts this deployment serves.
    /// </para>
    /// </remarks>
    public async Task RecordAsync(
        MailAccountId accountId,
        MailboxRefreshToken refreshToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(refreshToken);

        this.authorization.RequirePermission(MailFathomPermission.AdminCredentialsWrite);

        if (this.accountCatalog.ServedAccounts.FirstOrDefault(served => served.Id == accountId) is not { } account)
        {
            throw new MailAccountNotAccessibleException(accountId);
        }

        await this.refreshTokenStore.SaveTokenAsync(account.Identity, refreshToken, cancellationToken);
    }
}
