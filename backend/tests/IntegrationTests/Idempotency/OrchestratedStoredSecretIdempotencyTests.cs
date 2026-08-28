// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.OwnerSettings.Administration;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Owners;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Secrets.Database;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Idempotency;

/// <summary>Proves concurrent administrative creation of one named stored secret converges on one row and reference.</summary>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedStoredSecretIdempotencyTests(MailFathomOrchestrationFixture orchestration)
{
    private const int ConcurrentWriters = 6;
    private const string StoredSecretName = "concurrent-stored-secret";

    [Fact]
    public async Task StoreAsync_ManyAdministratorsCreatingOneOwnerAndName_ReturnsOneReference()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        Assert.True(SecretName.TryCreate(StoredSecretName, out var name));
        await DeleteStoredSecretAsync(services, cancellationToken);

        try
        {
            // Act
            var attempts = await ConcurrentIdempotency.RunAsync(
                $"{nameof(StoredSecretAdministration)}.{nameof(StoredSecretAdministration.StoreAsync)}",
                ConcurrentWriters,
                (ordinal, token) => services.AsCallerInScopeAsync(
                    async (scope, inner) =>
                    {
                        using var material = ResolvedSecret.FromText($"concurrent-material-{ordinal}");
                        return await Administration(scope).StoreAsync(
                            services.ServedOwner,
                            name,
                            material,
                            inner);
                    },
                    [MailFathomPermission.AdminConfigurationWrite],
                    token),
                cancellationToken);

            // Assert
            attempts.AssertSingleEffect(await CountStoredSecretsAsync(services, cancellationToken));
            Assert.Empty(attempts.Failures);
            Assert.Equal(ConcurrentWriters, attempts.Results.Count);
            Assert.All(
                attempts.Results,
                result => Assert.Equal(StoredSecretProvisioningOutcome.Stored, result.Outcome));
            Assert.Single(attempts.Results.Select(result => result.Reference).Distinct());
        }
        finally
        {
            await DeleteStoredSecretAsync(services, cancellationToken);
        }
    }

    private static StoredSecretAdministration Administration(IServiceProvider scope) => new(
        scope.GetRequiredService<AccessAuthorization>(),
        scope.GetRequiredService<IOwnerSettingsDocumentReader>(),
        scope.GetRequiredService<IStoredSecretStore>(),
        scope.GetRequiredService<OptimisticConcurrencyRetryPolicy>());

    private static Task<int> CountStoredSecretsAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().StoredSecrets
                .CountAsync(
                    secret => secret.OwnerId == services.ServedOwner.Value && secret.Name == StoredSecretName,
                    token),
            cancellationToken);

    private static Task<int> DeleteStoredSecretAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().StoredSecrets
                .Where(secret => secret.OwnerId == services.ServedOwner.Value && secret.Name == StoredSecretName)
                .ExecuteDeleteAsync(token),
            cancellationToken);
}
