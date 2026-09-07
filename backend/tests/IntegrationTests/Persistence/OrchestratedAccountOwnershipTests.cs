// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the rows a first folder binding creates belong to the user the caller resolved the account through.</summary>
/// <remarks>
/// <para>
/// Those rows are written by whichever synchronization run first binds one of an account's folders, which is the one
/// place a mailbox enters the mail graph — so it is the one place the user could be dropped instead of carried. What
/// settles it is the rows the database ends up with, and that needs a real database: nothing in a substitute would
/// have reported a user column left at its default.
/// </para>
/// <para>
/// The user arrives with the account rather than being read here, so what refuses a user this deployment does not
/// hold is the foreign key onto the user record rather than a decision in the write path. Which user a configured
/// account belongs to, and the refusals a deployment holding none or several meets, are settled once while the host
/// starts — <c>DeploymentMailUserStartupGate</c> — and are asserted there.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedAccountOwnershipTests(MailFathomOrchestrationFixture orchestration)
{
    private const string BoundAccount = "account-ownership-binding";

    private const string RefusedAccountUnderAnUnheldUser = "account-ownership-unheld-user";

    private const string InboxAlias = "account-ownership-inbox";

    [Fact]
    public async Task SaveResolutionAsync_TheFirstBindingOfAnAccount_HangsTheAccountAndItsFolderOnThatUser()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = MailAccountIdentity.Create(SyntheticMailAccount.User, MailAccountId.Create(BoundAccount));

        // Act
        var commitResult = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IMailFolderResolutionStore>()
                .SaveResolutionAsync(session, account, Binding(), token),
            cancellationToken);

        // Assert
        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        await services.InScopeAsync(
            async (scope, token) =>
            {
                var context = scope.GetRequiredService<MailFathomDbContext>();
                var userId = await context.UserAccounts.AsNoTracking().Select(user => user.Id).SingleAsync(token);
                var boundAccount = await context.MailboxAccounts
                    .AsNoTracking()
                    .SingleAsync(row => row.Id == BoundAccount, token);
                var boundFolder = await context.MailFolders
                    .AsNoTracking()
                    .SingleAsync(row => row.MailboxAccountId == BoundAccount && row.Alias == InboxAlias, token);

                Assert.Equal(userId, boundAccount.UserId);
                Assert.Equal(userId, boundFolder.UserId);

                return 0;
            },
            cancellationToken);
    }

    /// <summary>A user this deployment holds no record of is refused by the database rather than written down.</summary>
    /// <remarks>
    /// The write path takes the user from the identity it was handed, so nothing above the database inspects it. What
    /// keeps a mailbox from being attributed to somebody who does not exist is therefore the foreign key the account
    /// row carries onto the user record, and a run that reached this state would leave no mail behind rather than mail
    /// nobody can be shown to own.
    /// </remarks>
    [Fact]
    public async Task SaveResolutionAsync_AnUserTheDeploymentHoldsNoRecordOf_IsRefusedRatherThanWrittenDown()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var unheldUser = MailAccountIdentity.Create(
            MailUserId.Create(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301")),
            MailAccountId.Create(RefusedAccountUnderAnUnheldUser));

        // Act
        await Assert.ThrowsAsync<DbUpdateException>(() => services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IMailFolderResolutionStore>()
                .SaveResolutionAsync(session, unheldUser, Binding(), token),
            cancellationToken));

        // Assert
        await services.InScopeAsync(
            async (scope, token) =>
            {
                var context = scope.GetRequiredService<MailFathomDbContext>();

                Assert.False(await context.MailboxAccounts
                    .AsNoTracking()
                    .AnyAsync(row => row.Id == RefusedAccountUnderAnUnheldUser, token));

                return 0;
            },
            cancellationToken);
    }

    private static MailFolderResolution Binding() => MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create(InboxAlias),
        RemoteFolderPath.Create("INBOX", hierarchyDelimiter: '.'));
}
