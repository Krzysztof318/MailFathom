// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Accounts;

/// <summary>Holds the refresh token MailFathom stores for one mail account, under MailFathom's own protection.</summary>
/// <remarks>
/// <para>
/// The port names the operation in domain terms for the reason <see cref="EmailContent.Storage.IEmailContentStore" />
/// does: what protects the value and where it is written are the adapter's decisions, and both are expected to change
/// without a caller noticing. Sealing, the key ring, the identifier of the key that sealed a value, and every PostgreSQL
/// type involved stay inside the adapter; nothing above it ever sees ciphertext.
/// </para>
/// <para>
/// It takes no persistence session and joins no transaction, unlike the repositories that write mail. A token is stored
/// during a token request rather than during a unit of work over the mailbox, so there is no transaction of the caller's
/// to participate in, and a store that took a session it could not use would guarantee nothing while appearing to.
/// </para>
/// </remarks>
public interface IMailboxRefreshTokenStore
{
    /// <summary>Reads the stored refresh token for one account.</summary>
    /// <param name="account">The account the token acts for.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The token, which the caller owns and must dispose, or <see langword="null" /> when none is stored.</returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// Thrown when the stored value does not open under the key it names — a row restored from another deployment, a
    /// value moved between accounts, or an altered ciphertext.
    /// </exception>
    /// <remarks>
    /// An absent token is an ordinary answer rather than a failure: an account whose grant has never been stored is
    /// served from the refresh token its configuration references, which is what keeps a deployment that predates this
    /// store working unchanged.
    /// </remarks>
    Task<MailboxRefreshToken?> FindTokenAsync(MailAccountIdentity account, CancellationToken cancellationToken);

    /// <summary>Stores the refresh token for one account, replacing whatever was stored before.</summary>
    /// <param name="account">The account the token acts for.</param>
    /// <param name="refreshToken">The token to store. The caller keeps ownership of it.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes after durable storage.</returns>
    /// <remarks>
    /// Only the newest token is kept. An authorization server that rotates invalidates the token it replaced, so a
    /// previous one is not a fallback but a credential that no longer works, and keeping it would widen what a disclosure
    /// of the database exposes for nothing. The write is idempotent in the account, so storing the same token twice — two
    /// replicas refreshing at once, a retried request — leaves one row rather than a conflict.
    /// </remarks>
    Task SaveTokenAsync(MailAccountIdentity account, MailboxRefreshToken refreshToken, CancellationToken cancellationToken);
}
