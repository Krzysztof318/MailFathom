// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the account row a first folder binding creates belongs to the owner the database already holds.</summary>
/// <remarks>
/// The row is written by whichever synchronization run first binds one of an account's folders, which is the one place
/// a mailbox enters the mail graph — so it is the one place the owner could be invented instead of read. What settles
/// it is the row the database ends up with, and that needs a real database: the value is not in the resolution the
/// caller passed and nothing in a substitute would have refused the wrong one.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedAccountOwnershipTests(MailFathomOrchestrationFixture orchestration)
{
    private const string BoundAccount = "account-ownership-binding";

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
}
