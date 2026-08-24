// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the account row a first folder binding creates belongs to the owner the database already holds.</summary>
/// <remarks>
/// <para>
/// The row is written by whichever synchronization run first binds one of an account's folders, which is the one place
/// a mailbox enters the mail graph — so it is the one place the owner could be invented instead of read. What settles
/// it is the row the database ends up with, and that needs a real database: the value is not in the resolution the
/// caller passed and nothing in a substitute would have refused the wrong one.
/// </para>
/// <para>
/// The two refusals are here for the same reason and are arranged the same way: the owner rows the binding reads are
/// changed inside the binding's own transaction, and the transaction is left to roll back. A deployment holding several
/// owners or none is a state this suite's database cannot be committed into — the folder bindings every later class
/// arranges are resolved against exactly one owner record — and it is the state the refusals exist for, so the
/// arrangement lives for the length of one transaction and is undone by not committing it.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedAccountOwnershipTests(MailFathomOrchestrationFixture orchestration)
{
    private const string BoundAccount = "account-ownership-binding";

    private const string RefusedAccountUnderSeveralOwners = "account-ownership-several-owners";

    private const string RefusedAccountUnderNoOwner = "account-ownership-no-owner";

    [Fact]
    public async Task SaveResolutionAsync_TheFirstBindingOfAnAccount_HangsTheAccountOnTheDeploymentsOwner()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var accountId = MailAccountId.Create(BoundAccount);
        var binding = MailFolderResolution.FirstBindingOf(
            MailFolderAlias.Create("account-ownership-inbox"),
            RemoteFolderPath.Create("INBOX", hierarchyDelimiter: '.'));

        // Act
        var commitResult = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IMailFolderResolutionStore>()
                .SaveResolutionAsync(session, accountId, binding, token),
            cancellationToken);

        // Assert
        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        await services.InScopeAsync(
            async (scope, token) =>
            {
                var context = scope.GetRequiredService<MailFathomDbContext>();
                var ownerId = await context.OwnerAccounts.AsNoTracking().Select(owner => owner.Id).SingleAsync(token);
                var boundAccount = await context.MailboxAccounts
                    .AsNoTracking()
                    .SingleAsync(account => account.Id == BoundAccount, token);

                Assert.Equal(ownerId, boundAccount.OwnerId);

                return 0;
            },
            cancellationToken);
    }

    /// <summary>Several owners are refused rather than resolved to whichever row the read returned first.</summary>
    /// <remarks>
    /// This is the failure the ordering and the <c>Take(2)</c> in the resolver exist for: a first-match read would hand
    /// the binding an owner, the mailbox would be attributed to whichever record happened to sort first, and nothing
    /// afterwards would report that the boundary its mail is judged against had been chosen rather than read.
    /// </remarks>
    [Fact]
    public async Task SaveResolutionAsync_ADeploymentHoldingASecondOwner_RefusesToAttributeTheAccount()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        // Act
        var refusal = await BindWithoutCommittingAsync(
            services,
            RefusedAccountUnderSeveralOwners,
            async (context, token) =>
            {
                context.OwnerAccounts.Add(new OwnerAccountEntity
                {
                    Id = Guid.CreateVersion7(),
                    Document = "{}",
                    Version = 1,
                    CreatedAt = DateTimeOffset.UnixEpoch,
                    UpdatedAt = DateTimeOffset.UnixEpoch,
                });

                await context.SaveChangesAsync(token);
            },
            cancellationToken);

        // Assert
        Assert.Contains("more than one owner record", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>No owner is refused rather than minted, which is what keeps the boundary something a run reads.</summary>
    [Fact]
    public async Task SaveResolutionAsync_ADeploymentHoldingNoOwner_RefusesRatherThanProvisioningOne()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        // Act
        var refusal = await BindWithoutCommittingAsync(
            services,
            RefusedAccountUnderNoOwner,
            async (context, token) => await context.OwnerAccounts.ExecuteDeleteAsync(token),
            cancellationToken);

        // Assert
        Assert.Contains("holds no owner record", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>Arranges the owner rows and binds a folder in one transaction, and rolls the whole thing back.</summary>
    /// <param name="services">The composed services the binding and the arrangement both run against.</param>
    /// <param name="accountId">An account no earlier test bound, so the binding reaches the row-creating path.</param>
    /// <param name="arrangeOwners">Writes the owner state the refusal is about, inside the binding's transaction.</param>
    /// <param name="cancellationToken">Cancels the arrangement and the binding.</param>
    /// <returns>The refusal the binding raised, for the caller to state which of the two it is.</returns>
    /// <remarks>
    /// The session is disposed without being committed, which rolls back both the arrangement and whatever the binding
    /// wrote before it refused — including the cascade the deletion of the sole owner record stages, which is why that
    /// arrangement is safe to make against the database this whole collection shares.
    /// </remarks>
    private static Task<InvalidOperationException> BindWithoutCommittingAsync(
        OrchestratedMailFathomServices services,
        string accountId,
        Func<MailFathomDbContext, CancellationToken, Task> arrangeOwners,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                await using var session = await scope.GetRequiredService<IPersistenceSessionFactory>()
                    .BeginSessionAsync(token);

                await arrangeOwners(await EfCorePersistenceSessionAccessor.JoinAsync(session, token), token);

                var binding = MailFolderResolution.FirstBindingOf(
                    MailFolderAlias.Create("account-ownership-inbox"),
                    RemoteFolderPath.Create("INBOX", hierarchyDelimiter: '.'));

                return await Assert.ThrowsAsync<InvalidOperationException>(
                    () => scope.GetRequiredService<IMailFolderResolutionStore>()
                        .SaveResolutionAsync(session, MailAccountId.Create(accountId), binding, token));
            },
            cancellationToken);
}
