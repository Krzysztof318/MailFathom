// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Accounts;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves a refresh token survives its sealed <c>bytea</c> column, that a stale write cannot replace it, and that it refuses to open for anyone else.</summary>
/// <remarks>
/// None of the three is reachable from a unit test. What is stored has to cross a real provider and a real column before
/// the row can be inspected for the plaintext it must not carry; the conflict guard is a <c>WHERE</c> clause only
/// PostgreSQL evaluates; and the account binding only means something once a row can actually be moved between accounts
/// with an <c>UPDATE</c> — which is exactly what a database copy, a restore, or a mistaken repair would do.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedMailboxRefreshTokenStoreTests(MailFathomOrchestrationFixture orchestration)
{
    private const string RoundTrippedAccount = "refresh-token-round-trip";

    private const string StragglerWriteAccount = "refresh-token-straggler";

    private const string MovedRowAccount = "refresh-token-binding";

    private const string OtherAccount = "refresh-token-binding-other";

    /// <summary>One test covers the whole write path, because storing twice is the same operation as storing once.</summary>
    [Fact]
    public async Task SaveTokenAsync_AStoredThenRotatedToken_KeepsOneSealedRowCarryingTheNewestValue()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = MailAccountIdentity.Create(
            SyntheticMailAccount.Owner,
            MailAccountId.Create(RoundTrippedAccount));

        // Act
        await SaveAsync(services, account, "the-seeded-refresh-token", cancellationToken);
        await SaveAsync(services, account, "the-rotated-refresh-token", cancellationToken);

        using var readBack = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailboxRefreshTokenStore>().FindTokenAsync(account, token),
            cancellationToken);

        // Assert
        Assert.NotNull(readBack);
        Assert.Equal("the-rotated-refresh-token", readBack.RevealAsString());

        var rows = await ReadRowsAsync(services, RoundTrippedAccount, cancellationToken);
        var storedRow = Assert.Single(rows);
        Assert.Equal(OrchestratedMailFathomServices.DataEncryptionKeyId, storedRow.KeyId);

        // The point of the column: neither the token that was replaced nor the one that replaced it is in it. Searching
        // for the bytes is what a disclosure of the database would amount to, so it is what the assertion does.
        Assert.DoesNotContain(Encoding.UTF8.GetBytes("the-rotated-refresh-token"), storedRow.SealedToken);
        Assert.DoesNotContain(Encoding.UTF8.GetBytes("the-seeded-refresh-token"), storedRow.SealedToken);
    }

    /// <summary>A write that started before the row it would replace is refused rather than winning by arriving last.</summary>
    /// <remarks>
    /// The guard is a <c>WHERE</c> clause on the conflict update, so only the database can decide it, and the test above
    /// cannot reach it: two sequential saves carry naturally increasing timestamps and take the accepting branch every
    /// time. Moving the stored row's own timestamp forward is what a fresher writer leaves behind, and it is the only
    /// way to make the next real save arrive stale without a clock this suite does not control.
    /// </remarks>
    [Fact]
    public async Task SaveTokenAsync_AWriteOlderThanTheStoredRow_LeavesTheNewerTokenInPlace()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = MailAccountIdentity.Create(
            SyntheticMailAccount.Owner,
            MailAccountId.Create(StragglerWriteAccount));
        await SaveAsync(services, account, "the-current-refresh-token", cancellationToken);

        await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().Database.ExecuteSqlAsync(
                $"""
                 UPDATE mailbox_refresh_tokens
                 SET "UpdatedAt" = "UpdatedAt" + INTERVAL '1 hour'
                 WHERE "MailboxAccountId" = {StragglerWriteAccount}
                 """,
                token),
            cancellationToken);

        // Act — a delayed or retried write carrying a token the authorization server has already replaced.
        await SaveAsync(services, account, "the-already-invalidated-token", cancellationToken);

        // Assert
        using var readBack = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailboxRefreshTokenStore>().FindTokenAsync(account, token),
            cancellationToken);

        Assert.NotNull(readBack);
        Assert.Equal("the-current-refresh-token", readBack.RevealAsString());
    }

    /// <summary>A row moved between accounts fails to open rather than opening as the wrong owner's credential.</summary>
    [Fact]
    public async Task FindTokenAsync_ARowMovedToAnotherAccount_RefusesToOpenIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = MailAccountIdentity.Create(
            SyntheticMailAccount.Owner,
            MailAccountId.Create(MovedRowAccount));
        await SaveAsync(services, account, "a-refresh-token", cancellationToken);

        // Act — what a restored dump, a mistaken repair, or a stolen row copied into another tenant's account looks
        // like from the database's side: the same ciphertext under a different owner.
        await services.InScopeAsync(
            async (scope, token) =>
            {
                var dbContext = scope.GetRequiredService<MailFathomDbContext>();

                return await dbContext.Database.ExecuteSqlAsync(
                    $"""
                     INSERT INTO mailbox_refresh_tokens
                         ("OwnerId", "MailboxAccountId", "SealedRefreshToken", "DataEncryptionKeyId", "UpdatedAt")
                     SELECT "OwnerId", {OtherAccount}, "SealedRefreshToken", "DataEncryptionKeyId", "UpdatedAt"
                     FROM mailbox_refresh_tokens
                     WHERE "MailboxAccountId" = {MovedRowAccount}
                     """,
                    token);
            },
            cancellationToken);

        // Assert
        await Assert.ThrowsAnyAsync<CryptographicException>(() => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailboxRefreshTokenStore>()
                .FindTokenAsync(
                    MailAccountIdentity.Create(SyntheticMailAccount.Owner, MailAccountId.Create(OtherAccount)),
                    token),
            cancellationToken));
    }

    private static async Task SaveAsync(
        OrchestratedMailFathomServices services,
        MailAccountIdentity account,
        string refreshToken,
        CancellationToken cancellationToken) =>
        await services.InScopeAsync(
            async (scope, token) =>
            {
                using var value = MailboxRefreshToken.FromText(refreshToken);
                await scope.GetRequiredService<IMailboxRefreshTokenStore>().SaveTokenAsync(account, value, token);

                return true;
            },
            cancellationToken);

    private static Task<List<StoredRefreshTokenRow>> ReadRowsAsync(
        OrchestratedMailFathomServices services,
        string accountId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .MailboxRefreshTokens
                .AsNoTracking()
                .Where(stored => stored.MailboxAccountId == accountId)
                .Select(stored => new StoredRefreshTokenRow(stored.SealedRefreshToken, stored.DataEncryptionKeyId))
                .ToListAsync(token),
            cancellationToken);

    private sealed record StoredRefreshTokenRow(byte[] SealedToken, string KeyId);
}
