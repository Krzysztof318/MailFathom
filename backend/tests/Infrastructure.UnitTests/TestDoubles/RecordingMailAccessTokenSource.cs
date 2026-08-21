// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Mail.OAuth;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>A token source that hands out a numbered token and records what was asked of it.</summary>
/// <remarks>
/// It preserves the guarantee the real source makes and a stub would lose: a renewal returns a token that is not the
/// one handed back before it. A double returning the same value on every call would let a re-authentication loop that
/// presents the refused token again pass as though it had recovered.
/// </remarks>
internal sealed class RecordingMailAccessTokenSource : IMailAccessTokenSource
{
    private readonly DateTimeOffset expiresAt;
    private int issuedCount;

    public RecordingMailAccessTokenSource(DateTimeOffset expiresAt) => this.expiresAt = expiresAt;

    /// <summary>Gets the tokens this source was told a mail server had refused, in order.</summary>
    internal List<string> RejectedTokens { get; } = [];

    /// <summary>Gets how many times a renewal was requested.</summary>
    internal int RenewCount => this.RejectedTokens.Count;

    /// <summary>Gets or sets what happens before an exchange answers, which a test uses to model a slow authorization server.</summary>
    /// <remarks>Left unset, an exchange answers at once, which is what every test that is not about the exchange's own duration wants.</remarks>
    internal Func<CancellationToken, Task>? WhileExchanging { get; set; }

    /// <inheritdoc />
    public async Task<MailAccessToken> GetAccessTokenAsync(string accountId, CancellationToken cancellationToken)
    {
        await this.ExchangeAsync(cancellationToken);

        return this.IssueNext();
    }

    /// <inheritdoc />
    public async Task<MailAccessToken> RenewAccessTokenAsync(
        string accountId,
        MailAccessToken rejectedToken,
        CancellationToken cancellationToken)
    {
        this.RejectedTokens.Add(rejectedToken.Value);

        await this.ExchangeAsync(cancellationToken);

        return this.IssueNext();
    }

    private async Task ExchangeAsync(CancellationToken cancellationToken)
    {
        if (this.WhileExchanging is { } exchange)
        {
            await exchange(cancellationToken);
        }
    }

    private MailAccessToken IssueNext()
    {
        this.issuedCount++;

        return new MailAccessToken($"access-token-{this.issuedCount}", this.expiresAt);
    }
}
