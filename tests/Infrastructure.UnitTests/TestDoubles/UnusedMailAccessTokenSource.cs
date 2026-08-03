// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Mail.OAuth;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>A token source for connections whose policy permits no token-bearing mechanism.</summary>
/// <remarks>
/// It fails rather than returning a token, which is what makes it useful: every test using it authenticates with a
/// password, and a call reaching here would mean the adapter chose an access token for an allow-list that permits
/// none. A stub returning a plausible token would let exactly that defect pass.
/// </remarks>
internal sealed class UnusedMailAccessTokenSource : IMailAccessTokenSource
{
    /// <inheritdoc />
    public Task<MailAccessToken> GetAccessTokenAsync(string accountId, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("This connection authenticates with a password and must not request an access token.");

    /// <inheritdoc />
    public Task<MailAccessToken> RenewAccessTokenAsync(
        string accountId,
        MailAccessToken rejectedToken,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("This connection authenticates with a password and must not request an access token.");
}
