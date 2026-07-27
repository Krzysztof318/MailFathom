// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Infrastructure.Persistence;
using MailMcp.Infrastructure.Secrets;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

public sealed class PostgresDataSourceProviderTests
{
    private const string ConnectionStringWithoutPassword = "Host=localhost;Database=mailmcp;Username=mailmcp";

    [Fact]
    public void DataSource_BeforeStartup_ThrowsRatherThanBuildingOneFromAnUnresolvedConnectionString()
    {
        // Arrange
        var provider = CreateProvider();

        // Act, Assert
        Assert.Throws<InvalidOperationException>(() => provider.DataSource);
    }

    [Fact]
    public async Task StartingAsync_ResolvablePassword_ComposesTheDataSourceBeforeAnyWorkerStarts()
    {
        // Arrange
        var provider = CreateProvider(new ConfiguredSecret { SecretReference = "plaintext:postgres-password" });

        // Act
        await provider.StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("Username=mailmcp", provider.DataSource.ConnectionString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartingAsync_UnresolvablePassword_FailsStartupInsteadOfDeferringToTheFirstQuery()
    {
        // Arrange
        var provider = CreateProvider(new ConfiguredSecret { SecretReference = "file:/run/secrets/absent" });

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.StartingAsync(TestContext.Current.CancellationToken));
    }

    private static PostgresDataSourceProvider CreateProvider(ConfiguredSecret? password = null) => new(
        new PostgresConnectionSettings(ConnectionStringWithoutPassword, ConnectionStringSecret: null, password),
        new PlaintextOnlySecretReferenceResolver(),
        new SecretResolutionOptions(SecretValueInterpretation.ReferenceOnly),
        NullLogger<PostgresDataSourceProvider>.Instance);

    private sealed class PlaintextOnlySecretReferenceResolver : ISecretReferenceResolver
    {
        public Task<SecretResolutionResult> ResolveAsync(string? configuredValue, CancellationToken cancellationToken)
        {
            if (!SecretReference.TryParse(configuredValue, out var reference, out var grammarFailure))
            {
                return Task.FromResult(SecretResolutionResult.Failed(grammarFailure));
            }

            return Task.FromResult(reference.Scheme == SecretReferenceScheme.Plaintext
                ? SecretResolutionResult.Resolved(
                    ResolvedSecret.FromText(reference.Target),
                    SecretMaterialSource.InlineValue)
                : SecretResolutionResult.Failed(SecretResolutionFailure.MaterialNotFound));
        }
    }
}
