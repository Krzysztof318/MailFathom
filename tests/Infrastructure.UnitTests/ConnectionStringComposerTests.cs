// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Secrets;
using Npgsql;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests;

public sealed class ConnectionStringComposerTests
{
    private const string ConnectionStringWithoutPassword = "Host=localhost;Database=mailfathom;Username=mailfathom";

    [Fact]
    public async Task ComposeAsync_PasswordBlock_KeepsTheCredentialOutOfTheConnectionStringAndNamesItsSource()
    {
        // Arrange
        var configuredPassword = new ConfiguredSecret { SecretReference = "plaintext:postgres-password" };

        // Act
        var composed = await ComposeAsync(configuredPassword: configuredPassword);

        // Assert
        Assert.Null(composed.ConnectionSettings.Password);
        Assert.Equal("mailfathom", composed.ConnectionSettings.Database);
        Assert.Equal(DatabasePasswordSource.PasswordSecret, composed.PasswordSource);
    }

    [Fact]
    public async Task ComposeAsync_NoPasswordBlock_LeavesTheConnectionStringUnchanged()
    {
        // Act
        var composed = await ComposeAsync();

        // Assert
        Assert.Null(composed.ConnectionSettings.Password);
        Assert.Equal("mailfathom", composed.ConnectionSettings.Username);
        Assert.Equal(DatabasePasswordSource.None, composed.PasswordSource);
    }

    [Fact]
    public async Task ComposeAsync_ConnectionStringSecret_SuppliesEverythingButTheCredential()
    {
        // Arrange
        var connectionStringSecret = new ConfiguredSecret
        {
            SecretReference = $"plaintext:{ConnectionStringWithoutPassword};Password=from-the-store",
        };

        // Act
        var composed = await ComposeAsync(
            configuredConnectionString: null,
            connectionStringSecret: connectionStringSecret);

        // Assert
        Assert.Null(composed.ConnectionSettings.Password);
        Assert.Equal("mailfathom", composed.ConnectionSettings.Database);
        Assert.Equal(DatabasePasswordSource.ConnectionStringSecret, composed.PasswordSource);
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

    /// <summary>An orchestrator-supplied connection string keeps working; it simply has no source to rotate from.</summary>
    [Fact]
    public async Task ComposeAsync_PasswordInTheConnectionStringWithoutABlock_KeepsIt()
    {
        // Act
        var composed = await ComposeAsync(
            configuredConnectionString: $"{ConnectionStringWithoutPassword};Password=orchestrator-supplied");

        // Assert
        Assert.Equal("orchestrator-supplied", composed.ConnectionSettings.Password);
        Assert.Equal(DatabasePasswordSource.None, composed.PasswordSource);
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
    public async Task ResolveCurrentPasswordAsync_PasswordSecret_ReturnsTheMaterialBehindTheReference()
    {
        // Arrange
        var configuredPassword = new ConfiguredSecret { SecretReference = "plaintext:postgres-password" };

        // Act
        var password = await ConnectionStringComposer.ResolveCurrentPasswordAsync(
            DatabasePasswordSource.PasswordSecret,
            connectionStringSecret: null,
            configuredPassword,
            new PlaintextOnlySecretReferenceResolver(),
            CancellationToken.None);

        // Assert
        Assert.Equal("postgres-password", password);
    }

    [Fact]
    public async Task ResolveCurrentPasswordAsync_ConnectionStringSecret_TakesThePasswordOutOfTheRotatedMaterial()
    {
        // Arrange
        var connectionStringSecret = new ConfiguredSecret
        {
            SecretReference = $"plaintext:{ConnectionStringWithoutPassword};Password=from-the-store",
        };

        // Act
        var password = await ConnectionStringComposer.ResolveCurrentPasswordAsync(
            DatabasePasswordSource.ConnectionStringSecret,
            connectionStringSecret,
            configuredPassword: null,
            new PlaintextOnlySecretReferenceResolver(),
            CancellationToken.None);

        // Assert
        Assert.Equal("from-the-store", password);
    }

    /// <summary>Rotation behind an unchanged reference is what the per-connection retrieval exists for.</summary>
    [Fact]
    public async Task ResolveCurrentPasswordAsync_MaterialRotatedBehindAnUnchangedReference_ReturnsTheRotatedPassword()
    {
        // Arrange
        var configuredPassword = new ConfiguredSecret { SecretReference = "rotating:postgres-password" };
        var resolver = new RotatingSecretReferenceResolver("first-password");

        // Act
        var beforeRotation = await ResolveCurrentPasswordAsync(configuredPassword, resolver);
        resolver.Rotate("second-password");
        var afterRotation = await ResolveCurrentPasswordAsync(configuredPassword, resolver);

        // Assert
        Assert.Equal("first-password", beforeRotation);
        Assert.Equal("second-password", afterRotation);
    }

    [Fact]
    public async Task ResolveCurrentPasswordAsync_UnresolvableReference_FailsTheConnectionInsteadOfAuthenticatingWithoutAPassword()
    {
        // Arrange
        var configuredPassword = new ConfiguredSecret { SecretReference = "file:/run/secrets/absent" };

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ConnectionStringComposer.ResolveCurrentPasswordAsync(
            DatabasePasswordSource.PasswordSecret,
            connectionStringSecret: null,
            configuredPassword,
            new PlaintextOnlySecretReferenceResolver(),
            CancellationToken.None));

        // Assert
        Assert.Contains(nameof(SecretResolutionFailure.MaterialNotFound), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveCurrentPasswordAsync_RotatedConnectionStringWithoutAPassword_FailsRatherThanReturningNothing()
    {
        // Arrange
        var connectionStringSecret = new ConfiguredSecret
        {
            SecretReference = $"plaintext:{ConnectionStringWithoutPassword}",
        };

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => ConnectionStringComposer.ResolveCurrentPasswordAsync(
            DatabasePasswordSource.ConnectionStringSecret,
            connectionStringSecret,
            configuredPassword: null,
            new PlaintextOnlySecretReferenceResolver(),
            CancellationToken.None));
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

    private static Task<ComposedConnectionSettings> ComposeAsync(
        string? configuredConnectionString = ConnectionStringWithoutPassword,
        ConfiguredSecret? connectionStringSecret = null,
        ConfiguredSecret? configuredPassword = null) => ConnectionStringComposer.ComposeAsync(
            configuredConnectionString,
            connectionStringSecret,
            configuredPassword,
            new PlaintextOnlySecretReferenceResolver(),
            CancellationToken.None);

    private static Task<string> ResolveCurrentPasswordAsync(
        ConfiguredSecret configuredPassword,
        ISecretReferenceResolver resolver) => ConnectionStringComposer.ResolveCurrentPasswordAsync(
            DatabasePasswordSource.PasswordSecret,
            connectionStringSecret: null,
            configuredPassword,
            resolver,
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

    /// <summary>Stands in for a credential file or vault entry whose contents change while the reference does not.</summary>
    private sealed class RotatingSecretReferenceResolver(string initialMaterial) : ISecretReferenceResolver
    {
        private string material = initialMaterial;

        public void Rotate(string rotatedMaterial) => this.material = rotatedMaterial;

        public Task<SecretResolutionResult> ResolveAsync(string? configuredValue, CancellationToken cancellationToken) =>
            Task.FromResult(SecretResolutionResult.Resolved(
                ResolvedSecret.FromText(this.material),
                SecretMaterialSource.SchemeAdapter));
    }
}
