// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Infrastructure.Persistence;
using MailMcp.Infrastructure.Secrets;
using Npgsql;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

public sealed class ConnectionStringComposerTests
{
    private const string ConnectionStringWithoutPassword = "Host=localhost;Database=mailmcp;Username=mailmcp";

    [Fact]
    public async Task ComposeAsync_ResolvablePasswordReference_ComposesTheResolvedPasswordIntoTheConnectionString()
    {
        // Arrange
        var configuredPassword = new ConfiguredSecret { SecretReference = "plaintext:postgres-password" };

        // Act
        var connectionSettings = await ComposeAsync(configuredPassword: configuredPassword);

        // Assert
        Assert.Equal("postgres-password", connectionSettings.Password);
        Assert.Equal("mailmcp", connectionSettings.Database);
    }

    [Fact]
    public async Task ComposeAsync_NoPasswordBlock_LeavesTheConnectionStringUnchanged()
    {
        // Act
        var connectionSettings = await ComposeAsync();

        // Assert
        Assert.Null(connectionSettings.Password);
        Assert.Equal("mailmcp", connectionSettings.Username);
    }

    [Fact]
    public async Task ComposeAsync_UnresolvablePasswordReference_FailsInsteadOfConnectingWithoutAPassword()
    {
        // Arrange
        var configuredPassword = new ConfiguredSecret { SecretReference = "file:/run/secrets/absent" };

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => ComposeAsync(configuredPassword: configuredPassword));
    }

    [Fact]
    public async Task ComposeAsync_EmptyPasswordReference_FailsRatherThanSilentlyUsingTheUnchangedConnectionString()
    {
        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => ComposeAsync(configuredPassword: new ConfiguredSecret()));
    }

    [Fact]
    public async Task ComposeAsync_ConnectionStringSecret_SuppliesTheWholeConnectionString()
    {
        // Arrange
        var connectionStringSecret = new ConfiguredSecret
        {
            SecretReference = $"plaintext:{ConnectionStringWithoutPassword};Password=from-the-store",
        };

        // Act
        var connectionSettings = await ComposeAsync(
            configuredConnectionString: null,
            connectionStringSecret: connectionStringSecret);

        // Assert
        Assert.Equal("from-the-store", connectionSettings.Password);
        Assert.Equal("mailmcp", connectionSettings.Database);
    }

    [Fact]
    public async Task ComposeAsync_UnresolvableConnectionStringSecret_FailsInsteadOfStartingWithoutADatabase()
    {
        // Arrange
        var connectionStringSecret = new ConfiguredSecret { SecretReference = "file:/run/secrets/absent" };

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => ComposeAsync(
            configuredConnectionString: null,
            connectionStringSecret: connectionStringSecret));
    }

    /// <summary>The provider's own parse failure quotes the offending value, which here is a resolved connection string.</summary>
    [Fact]
    public async Task ComposeAsync_MalformedConnectionStringSecret_FailsWithoutQuotingTheMaterial()
    {
        // Arrange
        var connectionStringSecret = new ConfiguredSecret
        {
            SecretReference = "plaintext:NotAKeyword=value;Password=would-be-quoted",
        };

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ComposeAsync(
            configuredConnectionString: null,
            connectionStringSecret: connectionStringSecret));

        // Assert
        Assert.DoesNotContain("would-be-quoted", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComposeAsync_NeitherSourceSuppliesAConnectionString_FailsAtStartup()
    {
        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => ComposeAsync(configuredConnectionString: null));
    }

    /// <summary>An orchestrator-supplied connection string keeps working; only a second source for the same credential is rejected.</summary>
    [Fact]
    public async Task ComposeAsync_PasswordInTheConnectionStringWithoutABlock_KeepsIt()
    {
        // Act
        var connectionSettings = await ComposeAsync(
            configuredConnectionString: $"{ConnectionStringWithoutPassword};Password=orchestrator-supplied");

        // Assert
        Assert.Equal("orchestrator-supplied", connectionSettings.Password);
    }

    [Fact]
    public async Task ComposeAsync_PasswordInBothTheConnectionStringAndTheBlock_FailsRatherThanPickingOne()
    {
        // Arrange
        var configuredPassword = new ConfiguredSecret { SecretReference = "plaintext:from-the-block" };

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => ComposeAsync(
            configuredConnectionString: $"{ConnectionStringWithoutPassword};Password=from-the-connection-string",
            configuredPassword: configuredPassword));
    }

    [Fact]
    public void CarriesPasswordFromOrdinaryConfiguration_PasswordResolvedThroughABlock_ReportsNothing()
    {
        // Arrange
        var connectionSettings = new NpgsqlConnectionStringBuilder($"{ConnectionStringWithoutPassword};Password=resolved");

        // Act
        var carries = ConnectionStringComposer.CarriesPasswordFromOrdinaryConfiguration(
            connectionSettings,
            connectionStringSecret: null,
            configuredPassword: new ConfiguredSecret { SecretReference = "plaintext:resolved" });

        // Assert
        Assert.False(carries);
    }

    [Fact]
    public void CarriesPasswordFromOrdinaryConfiguration_PasswordWrittenIntoTheConnectionString_ReportsIt()
    {
        // Arrange
        var connectionSettings = new NpgsqlConnectionStringBuilder($"{ConnectionStringWithoutPassword};Password=written");

        // Act
        var carries = ConnectionStringComposer.CarriesPasswordFromOrdinaryConfiguration(
            connectionSettings,
            connectionStringSecret: null,
            configuredPassword: null);

        // Assert
        Assert.True(carries);
    }

    private static Task<NpgsqlConnectionStringBuilder> ComposeAsync(
        string? configuredConnectionString = ConnectionStringWithoutPassword,
        ConfiguredSecret? connectionStringSecret = null,
        ConfiguredSecret? configuredPassword = null) => ConnectionStringComposer.ComposeAsync(
            configuredConnectionString,
            connectionStringSecret,
            configuredPassword,
            new PlaintextOnlySecretReferenceResolver(),
            CancellationToken.None);

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
