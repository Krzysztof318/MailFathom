// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Infrastructure.Persistence;
using MailMcp.Infrastructure.Secrets;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

public sealed class PostgresConnectionStringProviderTests
{
    private const string ConnectionStringWithoutPassword = "Host=localhost;Database=mailmcp;Username=mailmcp";

    [Fact]
    public void ConnectionString_BeforeStartup_ThrowsRatherThanFallingBackToAnUnresolvedOne()
    {
        // Arrange
        var provider = CreateProvider();

        // Act, Assert
        Assert.Throws<InvalidOperationException>(() => provider.ConnectionString);
    }

    [Fact]
    public async Task StartingAsync_ResolvablePassword_ComposesTheConnectionStringBeforeAnyWorkerStarts()
    {
        // Arrange
        var provider = CreateProvider(new ConfiguredSecret { SecretReference = "plaintext:postgres-password" });

        // Act
        await provider.StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("Username=mailmcp", provider.ConnectionString, StringComparison.Ordinal);
    }

    /// <summary>The credential is retrieved when a physical connection opens, so it must not be baked into the pool's connection string.</summary>
    [Fact]
    public async Task StartingAsync_PasswordBlock_ComposesAConnectionStringThatCarriesNoCredential()
    {
        // Arrange
        var provider = CreateProvider(new ConfiguredSecret { SecretReference = "plaintext:postgres-password" });

        // Act
        await provider.StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain("postgres-password", provider.ConnectionString, StringComparison.Ordinal);
    }

    /// <summary>An unresolvable password fails the connection that needs it; startup fail-fast belongs to the host's secret configuration gate.</summary>
    [Fact]
    public async Task StartingAsync_UnresolvablePassword_StillComposesBecauseTheCredentialIsRetrievedPerConnection()
    {
        // Arrange
        var provider = CreateProvider(new ConfiguredSecret { SecretReference = "file:/run/secrets/absent" });

        // Act
        await provider.StartingAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("Username=mailmcp", provider.ConnectionString, StringComparison.Ordinal);
    }

    /// <summary>A deployment with no rotatable credential source must not have a password provider attached at all.</summary>
    [Fact]
    public async Task SupplyThePasswordPerConnection_NoConfiguredCredentialSource_LeavesTheBuilderUntouched()
    {
        // Arrange
        var provider = CreateProvider();
        await provider.StartingAsync(TestContext.Current.CancellationToken);
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(provider.ConnectionString);

        // Act
        provider.SupplyThePasswordPerConnection(dataSourceBuilder);

        // Assert
        Assert.Equal(provider.ConnectionString, dataSourceBuilder.ConnectionString);
    }

    /// <summary>Repointing the reference is the other half of rotation, and a captured settings object would miss it.</summary>
    [Fact]
    public async Task RetrieveCurrentPasswordAsync_ReferenceRepointedByAReload_AuthenticatesWithTheNewReference()
    {
        // Arrange
        var configuredSettings = new PostgresConnectionSettings(
            ConnectionStringWithoutPassword,
            ConnectionStringSecret: null,
            new ConfiguredSecret { SecretReference = "plaintext:the-original-password" });
        var provider = new PostgresConnectionStringProvider(
            () => configuredSettings,
            new PlaintextOnlySecretReferenceResolver(),
            new SecretResolutionOptions(SecretValueInterpretation.ReferenceOnly),
            NullLogger<PostgresConnectionStringProvider>.Instance);
        await provider.StartingAsync(TestContext.Current.CancellationToken);

        // Act
        var beforeTheReload = await provider.RetrieveCurrentPasswordAsync(TestContext.Current.CancellationToken);
        configuredSettings = configuredSettings with
        {
            Password = new ConfiguredSecret { SecretReference = "plaintext:the-repointed-password" },
        };
        var afterTheReload = await provider.RetrieveCurrentPasswordAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("the-original-password", beforeTheReload);
        Assert.Equal("the-repointed-password", afterTheReload);
    }

    /// <summary>Startup composition validates the first settings itself, so there is nothing to answer before it runs.</summary>
    [Fact]
    public async Task FindConfigurationFailuresAsync_BeforeStartupComposedTheConnection_ReportsNothing()
    {
        // Arrange
        var provider = CreateProvider();

        // Act
        var failures = await provider.FindConfigurationFailuresAsync(
            new PostgresConnectionSettings(ConnectionStringWithoutPassword, ConnectionStringSecret: null, Password: null),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(failures);
    }

    [Fact]
    public async Task FindConfigurationFailuresAsync_UnchangedShapeAndUsableMaterial_ReportsNothing()
    {
        // Arrange
        var password = new ConfiguredSecret { SecretReference = "plaintext:postgres-password" };
        var provider = CreateProvider(password);
        await provider.StartingAsync(TestContext.Current.CancellationToken);

        // Act
        var failures = await provider.FindConfigurationFailuresAsync(
            new PostgresConnectionSettings(
                ConnectionStringWithoutPassword,
                ConnectionStringSecret: null,
                new ConfiguredSecret { SecretReference = "plaintext:the-rotated-password" }),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(failures);
    }

    /// <summary>The pool attaches its password provider once, so adding a credential source later would be adopted without effect.</summary>
    [Fact]
    public async Task FindConfigurationFailuresAsync_CredentialSourceAddedByAReload_ReportsThatItRequiresARestart()
    {
        // Arrange
        var provider = CreateProvider();
        await provider.StartingAsync(TestContext.Current.CancellationToken);

        // Act
        var failures = await provider.FindConfigurationFailuresAsync(
            new PostgresConnectionSettings(
                ConnectionStringWithoutPassword,
                ConnectionStringSecret: null,
                new ConfiguredSecret { SecretReference = "plaintext:added-later" }),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([DatabaseConnectionConfigurationFailure.CredentialSourceChangeRequiresRestart], failures);
    }

    [Fact]
    public async Task FindConfigurationFailuresAsync_CredentialSourceRemovedByAReload_ReportsThatItRequiresARestart()
    {
        // Arrange
        var provider = CreateProvider(new ConfiguredSecret { SecretReference = "plaintext:postgres-password" });
        await provider.StartingAsync(TestContext.Current.CancellationToken);

        // Act
        var failures = await provider.FindConfigurationFailuresAsync(
            new PostgresConnectionSettings(ConnectionStringWithoutPassword, ConnectionStringSecret: null, Password: null),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([DatabaseConnectionConfigurationFailure.CredentialSourceChangeRequiresRestart], failures);
    }

    /// <summary>A reference that resolves says nothing about whether the bytes behind it are a connection string.</summary>
    [Fact]
    public async Task FindConfigurationFailuresAsync_ConnectionStringSecretThatDoesNotParse_ReportsIt()
    {
        // Arrange
        var connectionStringSecret = new ConfiguredSecret
        {
            SecretReference = $"plaintext:{ConnectionStringWithoutPassword};Password=composed-at-startup",
        };
        var provider = new PostgresConnectionStringProvider(
            () => new PostgresConnectionSettings(ConfiguredConnectionString: null, connectionStringSecret, Password: null),
            new PlaintextOnlySecretReferenceResolver(),
            new SecretResolutionOptions(SecretValueInterpretation.ReferenceOnly),
            NullLogger<PostgresConnectionStringProvider>.Instance);
        await provider.StartingAsync(TestContext.Current.CancellationToken);

        // Act
        var failures = await provider.FindConfigurationFailuresAsync(
            new PostgresConnectionSettings(
                ConfiguredConnectionString: null,
                new ConfiguredSecret { SecretReference = "plaintext:NotAKeyword=value" },
                Password: null),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([DatabaseConnectionConfigurationFailure.ConnectionStringNotParsable], failures);
    }

    [Fact]
    public async Task FindConfigurationFailuresAsync_RotatedConnectionStringWithoutAPassword_ReportsIt()
    {
        // Arrange
        var connectionStringSecret = new ConfiguredSecret
        {
            SecretReference = $"plaintext:{ConnectionStringWithoutPassword};Password=composed-at-startup",
        };
        var provider = new PostgresConnectionStringProvider(
            () => new PostgresConnectionSettings(ConfiguredConnectionString: null, connectionStringSecret, Password: null),
            new PlaintextOnlySecretReferenceResolver(),
            new SecretResolutionOptions(SecretValueInterpretation.ReferenceOnly),
            NullLogger<PostgresConnectionStringProvider>.Instance);
        await provider.StartingAsync(TestContext.Current.CancellationToken);

        // Act
        var failures = await provider.FindConfigurationFailuresAsync(
            new PostgresConnectionSettings(
                ConfiguredConnectionString: null,
                new ConfiguredSecret { SecretReference = $"plaintext:{ConnectionStringWithoutPassword}" },
                Password: null),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([DatabaseConnectionConfigurationFailure.ConnectionStringCarriesNoPassword], failures);
    }

    private static PostgresConnectionStringProvider CreateProvider(ConfiguredSecret? password = null) => new(
        () => new PostgresConnectionSettings(ConnectionStringWithoutPassword, ConnectionStringSecret: null, password),
        new PlaintextOnlySecretReferenceResolver(),
        new SecretResolutionOptions(SecretValueInterpretation.ReferenceOnly),
        NullLogger<PostgresConnectionStringProvider>.Instance);

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
