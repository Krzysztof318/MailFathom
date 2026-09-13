// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>
/// Proves the store a user's binary files are kept in against a real database: that a written file reads back for its
/// owner and for nobody else, that a user this deployment does not hold is answered with no file rather than a
/// foreign-key violation, that a removal leaves nothing behind, that erasing a user takes their files with them, and
/// that the candidate query names a file its owner's record does not mention and leaves one it does. None of it is
/// decidable without PostgreSQL, which is why the store carries the integration-coverage marker.
/// </summary>
/// <remarks>
/// Every test here writes against users it provisions and erases in a <c>finally</c>, for the reason
/// <c>OrchestratedForeignUser</c> states: the shared rows are read by every class in this collection, and a file left on
/// one would be state a later class reads without having written it.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedStoredFileStoreTests(MailFathomOrchestrationFixture orchestration)
{
    private static readonly byte[] Picture =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03];

    [Fact]
    public async Task WriteAsync_AFileForOnePerson_ReadsBackForThemAndForNobodyElse()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(services, owner, cancellationToken);
        await OrchestratedForeignUser.ProvisionAsync(services, other, cancellationToken);

        try
        {
            // Act
            var written = await WriteAsync(services, Named(owner), cancellationToken);

            // Assert
            Assert.NotNull(written);
            Assert.Equal(Picture, (await ReadAsync(services, Named(owner), written.Value, cancellationToken))!.Value.ToArray());
            Assert.Null(await ReadAsync(services, Named(other), written.Value, cancellationToken));
            Assert.False(await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IStoredFileStore>().HoldsAsync(Named(other), written.Value, token),
                cancellationToken));
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, owner);
            await OrchestratedForeignUser.EraseAsync(services, other);
        }
    }

    /// <summary>The caller is a person whose row was erased under a credential that has not yet been withdrawn, so the write reports that there is nothing here of theirs instead of raising a constraint violation.</summary>
    [Fact]
    public async Task WriteAsync_AUserThisDeploymentDoesNotHold_WritesNoFileAndReportsIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var stranger = Guid.NewGuid();

        // Act
        var written = await WriteAsync(services, Named(stranger), cancellationToken);

        // Assert
        Assert.Null(written);
        Assert.Equal(0, await CountAsync(services, stranger, cancellationToken));
    }

    [Fact]
    public async Task RemoveAsync_AFileItsOwnerRemoves_LeavesNoRowAndIsSafeToRepeat()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var owner = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(services, owner, cancellationToken);

        try
        {
            var written = (await WriteAsync(services, Named(owner), cancellationToken))!.Value;

            // Act
            await services.InScopeAsync(
                async (scope, token) =>
                {
                    var files = scope.GetRequiredService<IStoredFileStore>();

                    await files.RemoveAsync(Named(owner), written, token);
                    await files.RemoveAsync(Named(owner), written, token);

                    return true;
                },
                cancellationToken);

            // Assert
            Assert.Equal(0, await CountAsync(services, owner, cancellationToken));
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, owner);
        }
    }

    /// <summary>The cascade is what makes a file go with the person, without the erasure walk having to know this table exists.</summary>
    [Fact]
    public async Task EraseAsync_AUserWhoStoredAFile_TakesItWithEverythingElseDerivedFromThem()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var owner = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(services, owner, cancellationToken);
        Assert.NotNull(await WriteAsync(services, Named(owner), cancellationToken));

        // Act
        await OrchestratedForeignUser.EraseAsync(services, owner);

        // Assert
        Assert.Equal(0, await CountAsync(services, owner, cancellationToken));
    }

    /// <summary>The containment test runs over the rendering of a <c>jsonb</c> column, which only PostgreSQL can answer.</summary>
    [Fact]
    public async Task FindUnmentionedAsync_TwoFilesOfWhichTheRecordLinksOne_NamesOnlyTheOtherOne()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var owner = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(services, owner, cancellationToken);

        try
        {
            var linked = (await WriteAsync(services, Named(owner), cancellationToken))!.Value;
            var unlinked = (await WriteAsync(services, Named(owner), cancellationToken))!.Value;

            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<MailFathomDbContext>().Database.ExecuteSqlAsync(
                    $$"""UPDATE settings_accounts SET "Document" = jsonb_set("Document", '{Portrait}', to_jsonb({{linked.ToString()}})) WHERE "Id" = {{owner}}""",
                    token),
                cancellationToken);

            // Act
            var candidates = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IStoredFileStore>()
                    .FindUnmentionedAsync(DateTimeOffset.MaxValue, 1000, token),
                cancellationToken);

            // Assert
            var owned = candidates.Where(candidate => candidate.Owner == Named(owner)).Select(candidate => candidate.File);

            Assert.Equal([unlinked], owned);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, owner);
        }
    }

    private static MailUserId Named(Guid user) => MailUserId.Create(user);

    private static Task<StoredFileId?> WriteAsync(
        OrchestratedMailFathomServices services,
        MailUserId owner,
        CancellationToken cancellationToken) =>
        services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredFileStore>().WriteAsync(owner, "image/png", Picture, token),
            cancellationToken);

    private static Task<ReadOnlyMemory<byte>?> ReadAsync(
        OrchestratedMailFathomServices services,
        MailUserId owner,
        StoredFileId file,
        CancellationToken cancellationToken) =>
        services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredFileStore>().ReadAsync(owner, file, token),
            cancellationToken);

    private static Task<int> CountAsync(
        OrchestratedMailFathomServices services,
        Guid owner,
        CancellationToken cancellationToken) =>
        services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().StoredFiles
                .CountAsync(file => file.UserId == owner, token),
            cancellationToken);
}
