// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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

/// <summary>Proves the rows a first folder binding creates are keyed by the account's identifier and nothing beside it.</summary>
/// <remarks>
/// <para>
/// Those rows are written by whichever synchronization run first binds one of an account's folders, which is the one
/// place a mailbox enters the mail graph — so it is the one place a second key column could reappear. What settles it
/// is the rows the database ends up with, and that needs a real database: nothing in a substitute would have reported
/// a column the model carries and the write path leaves at its default.
/// </para>
/// <para>
/// Which users reach the mailbox is the assignment relation and is written elsewhere, so a binding names no user at
/// all — a mailbox bound before anybody is assigned it is an ordinary state rather than an orphan, and it is what a
/// deployment that configured an account and has not yet assigned it holds.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedAccountOwnershipTests(MailFathomOrchestrationFixture orchestration)
{
    private const string BoundAccount = "account-ownership-binding";

    private const string InboxAlias = "account-ownership-inbox";

    [Fact]
    public async Task SaveResolutionAsync_TheFirstBindingOfAnAccount_KeysTheAccountAndItsFolderByTheIdentifierAlone()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = MailAccountId.Create(BoundAccount);

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
                var boundAccount = await context.MailboxAccounts
                    .AsNoTracking()
                    .SingleAsync(row => row.Id == BoundAccount, token);

                // The alias as the domain stores it rather than as it is typed here: an alias is compared in one case
                // and is written down in that case, so a row is sought by what the binding actually wrote.
                var storedAlias = MailFolderAlias.Create(InboxAlias).Value;
                var boundFolder = await context.MailFolders
                    .AsNoTracking()
                    .SingleAsync(row => row.MailboxAccountId == BoundAccount && row.Alias == storedAlias, token);

                Assert.Equal(BoundAccount, boundAccount.Id);
                Assert.Equal(BoundAccount, boundFolder.MailboxAccountId);

                return 0;
            },
            cancellationToken);
    }

    private static MailFolderResolution Binding() => MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create(InboxAlias),
        RemoteFolderPath.Create("INBOX", hierarchyDelimiter: '.'));
}
