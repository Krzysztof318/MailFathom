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
/// row rather than raising a foreign-key violation, and that a removal leaves nothing behind. None of it is decidable
/// without PostgreSQL, which is why the store carries the integration-coverage marker.
/// </summary>
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

        // Act
        Assert.True(await SaveAsync(services, services.ServedOwner, FirstPicture, cancellationToken));
        var first = await RowAsync(services, services.ServedOwner, cancellationToken);

        Assert.True(await SaveAsync(services, services.ServedOwner, SecondPicture, cancellationToken));
        var replaced = await RowAsync(services, services.ServedOwner, cancellationToken);

        // Assert
        Assert.Equal(SecondPicture, replaced!.Content);
        Assert.Equal(first!.CreatedAt, replaced.CreatedAt);
        Assert.True(replaced.UpdatedAt >= first.UpdatedAt);

        var read = await ReadAsync(services, services.ServedOwner, cancellationToken);

        Assert.Equal(SecondPicture, read!.Value.ToArray());
    }

    /// <summary>The caller is a person whose row was erased under a credential that has not yet been withdrawn, so the write reports that there is nothing here of theirs instead of raising a constraint violation.</summary>
    [Fact]
    public async Task SaveAsync_AnOwnerThisDeploymentDoesNotHold_AffectsNoRowAndReportsIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var stranger = MailOwnerId.Create(new Guid("1f6f31d0-5cf8-4a2f-93d9-4d4e2f5a7b61"));

        // Act
        var written = await SaveAsync(services, stranger, FirstPicture, cancellationToken);

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

        Assert.True(await SaveAsync(services, services.ServedOwner, FirstPicture, cancellationToken));

        // Act
        await services.InScopeAsync(
            async (scope, token) =>
            {
                var portraits = scope.GetRequiredService<IOwnerPortraitStore>();

                await portraits.RemoveAsync(services.ServedOwner, token);
                await portraits.RemoveAsync(services.ServedOwner, token);

                return true;
            },
            cancellationToken);

        // Assert
        Assert.Null(await RowAsync(services, services.ServedOwner, cancellationToken));
        Assert.Null(await ReadAsync(services, services.ServedOwner, cancellationToken));
    }

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
        MailOwnerId owner,
        CancellationToken cancellationToken)
    {
        var ownerValue = owner.Value;

        return services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().OwnerPortraits
                .AsNoTracking()
                .Where(portrait => portrait.OwnerId == ownerValue)
                .Select(portrait => new StoredPortrait(portrait.Content, portrait.CreatedAt, portrait.UpdatedAt))
                .SingleOrDefaultAsync(token),
            cancellationToken);
    }

    private sealed record StoredPortrait(byte[] Content, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
