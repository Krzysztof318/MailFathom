// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Portraits;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>
/// Proves the one statement a person's portrait is written by against a real database: that a second write replaces
/// the octets and leaves the instant the first one recorded, that an owner this deployment does not hold affects no
/// row rather than raising a foreign-key violation, that a removal leaves nothing behind, and that erasing an owner
/// takes their picture with them. None of it is decidable without PostgreSQL, which is why the store carries the
/// integration-coverage marker.
/// </summary>
/// <remarks>
/// Every test here writes against an owner it provisions and erases in a <c>finally</c>, for the reason
/// <c>OrchestratedForeignOwner</c> states and <c>OrchestratedOwnerSettingsDocumentTests</c> follows: the provisioned
/// owner is one row shared by every class in this collection, and a portrait left on it would be state a later class
/// reads without having written it. A test isolates itself through the data it writes.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedOwnerPortraitStoreTests(MailFathomOrchestrationFixture orchestration)
{
    private static readonly byte[] FirstPicture =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03];

    private static readonly byte[] SecondPicture = [0xFF, 0xD8, 0xFF, 0xE0, 0x04, 0x05, 0x06];

    [Fact]
    public async Task SaveAsync_ASecondWriteForOnePerson_ReplacesTheOctetsAndKeepsTheFirstInstant()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var owner = Guid.NewGuid();

        await OrchestratedForeignOwner.ProvisionAsync(services, owner, cancellationToken);

        try
        {
            // Act
            Assert.True(await SaveAsync(services, Named(owner), FirstPicture, cancellationToken));
            var first = await RowAsync(services, owner, cancellationToken);

            Assert.True(await SaveAsync(services, Named(owner), SecondPicture, cancellationToken));
            var replaced = await RowAsync(services, owner, cancellationToken);

            // Assert
            Assert.Equal(SecondPicture, replaced!.Content);
            Assert.Equal(first!.CreatedAt, replaced.CreatedAt);
            Assert.True(replaced.UpdatedAt >= first.UpdatedAt);

            var read = await ReadAsync(services, Named(owner), cancellationToken);

            Assert.Equal(SecondPicture, read!.Value.ToArray());
        }
        finally
        {
            await OrchestratedForeignOwner.EraseAsync(services, owner);
        }
    }

    /// <summary>The caller is a person whose row was erased under a credential that has not yet been withdrawn, so the write reports that there is nothing here of theirs instead of raising a constraint violation.</summary>
    [Fact]
    public async Task SaveAsync_AnOwnerThisDeploymentDoesNotHold_AffectsNoRowAndReportsIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var stranger = Guid.NewGuid();

        // Act
        var written = await SaveAsync(services, Named(stranger), FirstPicture, cancellationToken);

        // Assert
        Assert.False(written);
        Assert.Null(await RowAsync(services, stranger, cancellationToken));
    }

    [Fact]
    public async Task RemoveAsync_APersonTakingTheirPictureDown_LeavesNoRowAndIsSafeToRepeat()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var owner = Guid.NewGuid();

        await OrchestratedForeignOwner.ProvisionAsync(services, owner, cancellationToken);

        try
        {
            Assert.True(await SaveAsync(services, Named(owner), FirstPicture, cancellationToken));

            // Act
            await services.InScopeAsync(
                async (scope, token) =>
                {
                    var portraits = scope.GetRequiredService<IOwnerPortraitStore>();

                    await portraits.RemoveAsync(Named(owner), token);
                    await portraits.RemoveAsync(Named(owner), token);

                    return true;
                },
                cancellationToken);

            // Assert
            Assert.Null(await RowAsync(services, owner, cancellationToken));
            Assert.Null(await ReadAsync(services, Named(owner), cancellationToken));
        }
        finally
        {
            await OrchestratedForeignOwner.EraseAsync(services, owner);
        }
    }

    /// <summary>The cascade is what makes a picture go with the person, without the erasure walk having to know this table exists.</summary>
    [Fact]
    public async Task EraseAsync_AnOwnerWhoSuppliedAPicture_TakesItWithEverythingElseDerivedFromThem()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var owner = Guid.NewGuid();

        await OrchestratedForeignOwner.ProvisionAsync(services, owner, cancellationToken);
        Assert.True(await SaveAsync(services, Named(owner), FirstPicture, cancellationToken));

        // Act
        await OrchestratedForeignOwner.EraseAsync(services, owner);

        // Assert
        Assert.Null(await RowAsync(services, owner, cancellationToken));
    }

    private static MailOwnerId Named(Guid owner) => MailOwnerId.Create(owner);

    private static Task<bool> SaveAsync(
        OrchestratedMailFathomServices services,
        MailOwnerId owner,
        byte[] picture,
        CancellationToken cancellationToken) =>
        services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOwnerPortraitStore>()
                .SaveAsync(owner, OwnerPortrait.Of(picture)!, token),
            cancellationToken);

    private static Task<ReadOnlyMemory<byte>?> ReadAsync(
        OrchestratedMailFathomServices services,
        MailOwnerId owner,
        CancellationToken cancellationToken) =>
        services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOwnerPortraitStore>().ReadAsync(owner, token),
            cancellationToken);

    private static Task<StoredPortrait?> RowAsync(
        OrchestratedMailFathomServices services,
        Guid owner,
        CancellationToken cancellationToken) =>
        services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().OwnerPortraits
                .AsNoTracking()
                .Where(portrait => portrait.OwnerId == owner)
                .Select(portrait => new StoredPortrait(portrait.Content, portrait.CreatedAt, portrait.UpdatedAt))
                .SingleOrDefaultAsync(token),
            cancellationToken);

    private sealed record StoredPortrait(byte[] Content, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
