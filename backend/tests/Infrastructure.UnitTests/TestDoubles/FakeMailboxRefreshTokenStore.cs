// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Accounts;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>Holds stored refresh tokens in memory, keyed by the account each belongs to, and records what a caller stored.</summary>
/// <remarks>
/// Hand-written rather than substituted because what the tests assert is the token that came back out, and a substitute
/// configured to return one would only restate the arrangement. The stored text is kept as a <see cref="string" /> so an
/// assertion can read it after the token the caller owned was erased. The lookup is keyed by the whole identity for the
/// reason <see cref="StoredAccounts" /> records one: a caller reading a token under an identity other than the one it
/// saves under finds nothing here, rather than being handed somebody else's credential.
/// </remarks>
internal sealed class FakeMailboxRefreshTokenStore : IMailboxRefreshTokenStore
{
    private readonly Dictionary<MailAccountIdentity, string> tokensByAccount = [];

    /// <summary>Creates a store holding nothing.</summary>
    public FakeMailboxRefreshTokenStore()
    {
    }

    /// <summary>Creates a store already holding one account's refresh token.</summary>
    /// <param name="account">The account the token belongs to, which is the key a lookup has to arrive under.</param>
    /// <param name="storedToken">The token text, or <see langword="null" /> for an account holding none.</param>
    public FakeMailboxRefreshTokenStore(MailAccountIdentity account, string? storedToken)
    {
        if (storedToken is not null)
        {
            this.tokensByAccount[account] = storedToken;
        }
    }

    /// <summary>Gets or sets what a save is made to fail with, or <see langword="null" /> when a save succeeds.</summary>
    public Exception? SaveFailure { get; set; }

    /// <summary>Gets the accounts a token was stored for, in the order the stores happened.</summary>
    /// <remarks>
    /// The whole identity rather than the identifier alone, because the owner half is what decides whose row a
    /// credential lands on: a store that recorded the identifier would let one owner's refresh token be written onto
    /// another owner's account with nothing to assert against.
    /// </remarks>
    public List<MailAccountIdentity> StoredAccounts { get; } = [];

    /// <summary>Gets the token last stored, read as text so it survives the caller erasing its own copy.</summary>
    public string? LastStoredToken { get; private set; }

    /// <inheritdoc />
    public Task<MailboxRefreshToken?> FindTokenAsync(
        MailAccountIdentity account,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.tokensByAccount.TryGetValue(account, out var storedToken)
            ? MailboxRefreshToken.FromText(storedToken)
            : null);

    /// <inheritdoc />
    public Task SaveTokenAsync(
        MailAccountIdentity account,
        MailboxRefreshToken refreshToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(refreshToken);

        if (this.SaveFailure is { } failure)
        {
            return Task.FromException(failure);
        }

        this.StoredAccounts.Add(account);
        this.LastStoredToken = refreshToken.RevealAsString();
        this.tokensByAccount[account] = this.LastStoredToken;

        return Task.CompletedTask;
    }
}
