// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Secrets.Database;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves database secret references cross their real table sealed, remain replaceable, and follow user erasure.</summary>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedStoredSecretTests(MailFathomOrchestrationFixture orchestration)
{
    private static readonly DatabaseSecretReference RoundTripReference =
        DatabaseSecretReference.Create(new Guid("a1134be4-8c83-4820-a82b-8a8b20fcd61d"));

    private static readonly DatabaseSecretReference TamperedReference =
        DatabaseSecretReference.Create(new Guid("d813db78-92e6-4b29-9ab2-551db043ab94"));

    private static readonly DatabaseSecretReference ErasedReference =
        DatabaseSecretReference.Create(new Guid("2a105151-e39f-445e-ad77-a471764bdd36"));

    private static readonly DatabaseSecretReference CrossUserReference =
        DatabaseSecretReference.Create(new Guid("f43f4252-8485-4463-9176-3e415a6510aa"));

    [Fact]
    public async Task StoreAsync_AStoredThenRewrittenSecret_ResolvesOnlyTheNewestMaterialUnderTheActiveKey()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var name = SecretNamed("stored-secret-round-trip");

        // Act
        Assert.Equal(
            PersistenceCommitResult.Committed,
            await StoreAsync(services, RoundTripReference, services.ServedUser, name, "the-seeded-secret", cancellationToken));
        Assert.Equal(
            PersistenceCommitResult.Committed,
            await StoreAsync(services, RoundTripReference, services.ServedUser, name, "the-rotated-secret", cancellationToken));
        var resolution = await ResolveAsync(services, RoundTripReference, cancellationToken);
        using var resolved = resolution.Secret;

        // Assert
        Assert.NotNull(resolved);
        Assert.Equal("the-rotated-secret", resolved.RevealAsString());

        var row = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().StoredSecrets
                .AsNoTracking()
                .Where(secret => secret.Id == RoundTripReference.Id)
                .Select(secret => new { secret.SealedMaterial, secret.DataEncryptionKeyId })
                .SingleAsync(token),
            cancellationToken);
        Assert.Equal(OrchestratedMailFathomServices.DataEncryptionKeyId, row.DataEncryptionKeyId);
        Assert.DoesNotContain(Encoding.UTF8.GetBytes("the-seeded-secret"), row.SealedMaterial);
        Assert.DoesNotContain(Encoding.UTF8.GetBytes("the-rotated-secret"), row.SealedMaterial);

        var references = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredSecretStore>()
                .ReadReferencesSealedByKeyAsync(OrchestratedMailFathomServices.DataEncryptionKeyId, 100, token),
            cancellationToken);
        Assert.Contains(references, stored => stored.Reference == RoundTripReference);
    }

    [Fact]
    public async Task ResolveAsync_AStoredSecretWhoseBoundNameWasChanged_RefusesToOpenIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var name = SecretNamed("stored-secret-binding");
        Assert.Equal(
            PersistenceCommitResult.Committed,
            await StoreAsync(services, TamperedReference, services.ServedUser, name, "bound-material", cancellationToken));

        await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().Database.ExecuteSqlAsync(
                $"""
                 UPDATE stored_secrets
                 SET "Name" = {"stored-secret-moved"}
                 WHERE "Id" = {TamperedReference.Id}
                 """,
                token),
            cancellationToken);

        // Act
        var resolution = await ResolveAsync(services, TamperedReference, cancellationToken);

        // Assert
        Assert.False(resolution.Succeeded);
        Assert.Equal(SecretResolutionFailure.ProtectedMaterialUnavailable, resolution.Failure);
    }

    [Fact]
    public async Task UserErasure_AStoredSecret_RemovesTheMaterialItsReferenceNamed()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var userId = new Guid("083737f1-fe8f-4525-b4ea-477ac9431e51");
        var user = MailUserId.Create(userId);

        try
        {
            Assert.Equal(
                PersistenceCommitResult.Committed,
                await OrchestratedForeignUser.ProvisionAsync(services, userId, cancellationToken));
            Assert.Equal(
                PersistenceCommitResult.Committed,
                await StoreAsync(
                    services,
                    ErasedReference,
                    user,
                    SecretNamed("stored-secret-erasure"),
                    "material-to-erase",
                    cancellationToken));

            // Act
            await OrchestratedForeignUser.EraseAsync(services, userId);
            var resolution = await ResolveAsync(services, ErasedReference, cancellationToken);

            // Assert
            Assert.False(resolution.Succeeded);
            Assert.Equal(SecretResolutionFailure.MaterialNotFound, resolution.Failure);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, userId);
        }
    }

    [Fact]
    public async Task StoreAsync_AReferenceOwnedByAnotherUser_RefusesWithoutChangingTheRow()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var otherUserId = new Guid("89cc409d-da55-472f-b156-bc18c3583c54");
        var otherUser = MailUserId.Create(otherUserId);

        try
        {
            Assert.Equal(
                PersistenceCommitResult.Committed,
                await OrchestratedForeignUser.ProvisionAsync(services, otherUserId, cancellationToken));
            Assert.Equal(
                PersistenceCommitResult.Committed,
                await StoreAsync(
                    services,
                    CrossUserReference,
                    services.ServedUser,
                    SecretNamed("cross-user-original"),
                    "the-original-material",
                    cancellationToken));

            // Act
            var storing = () => StoreAsync(
                services,
                CrossUserReference,
                otherUser,
                SecretNamed("cross-user-replacement"),
                "the-replacement-material",
                cancellationToken);

            // Assert
            await Assert.ThrowsAsync<InvalidOperationException>(storing);
            var storedUserId = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<MailFathomDbContext>().StoredSecrets
                    .Where(secret => secret.Id == CrossUserReference.Id)
                    .Select(secret => secret.UserId)
                    .SingleAsync(token),
                cancellationToken);
            Assert.Equal(services.ServedUser.Value, storedUserId);
        }
        finally
        {
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<MailFathomDbContext>().StoredSecrets
                    .Where(secret => secret.Id == CrossUserReference.Id)
                    .ExecuteDeleteAsync(token),
                cancellationToken);
            await OrchestratedForeignUser.EraseAsync(services, otherUserId);
        }
    }

    private static Task<PersistenceCommitResult> StoreAsync(
        OrchestratedMailFathomServices services,
        DatabaseSecretReference reference,
        MailUserId user,
        SecretName name,
        string material,
        CancellationToken cancellationToken) => services.CommitAsync(
            async (scope, session, token) =>
            {
                using var resolved = ResolvedSecret.FromText(material);
                await scope.GetRequiredService<IStoredSecretStore>()
                    .StoreAsync(session, reference, user, name, resolved, token);
            },
            cancellationToken);

    private static Task<SecretResolutionResult> ResolveAsync(
        OrchestratedMailFathomServices services,
        DatabaseSecretReference reference,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<ISecretReferenceResolver>()
                .ResolveAsync(reference.ConfigurationValue, token),
            cancellationToken);

    private static SecretName SecretNamed(string value) =>
        SecretName.TryCreate(value, out var name)
            ? name
            : throw new InvalidOperationException("The test secret name is malformed.");
}
