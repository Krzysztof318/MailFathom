// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Infrastructure.Persistence;
using MailMcp.Infrastructure.Secrets;
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
        var connectionSettings = await ConnectionStringComposer.ComposeAsync(
            ConnectionStringWithoutPassword,
            configuredPassword,
            new PlaintextOnlySecretReferenceResolver(),
            CancellationToken.None);

        // Assert
        Assert.Equal("postgres-password", connectionSettings.Password);
        Assert.Equal("mailmcp", connectionSettings.Database);
    }

    [Fact]
    public async Task ComposeAsync_NoPasswordBlock_LeavesTheConnectionStringUnchanged()
    {
        // Act
        var connectionSettings = await ConnectionStringComposer.ComposeAsync(
            ConnectionStringWithoutPassword,
            configuredPassword: null,
            new PlaintextOnlySecretReferenceResolver(),
            CancellationToken.None);

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
        await Assert.ThrowsAsync<InvalidOperationException>(() => ConnectionStringComposer.ComposeAsync(
            ConnectionStringWithoutPassword,
            configuredPassword,
            new PlaintextOnlySecretReferenceResolver(),
            CancellationToken.None));
    }

    [Fact]
    public async Task ComposeAsync_EmptyPasswordReference_FailsRatherThanSilentlyUsingTheUnchangedConnectionString()
    {
        // Arrange
        var configuredPassword = new ConfiguredSecret();

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => ConnectionStringComposer.ComposeAsync(
            ConnectionStringWithoutPassword,
            configuredPassword,
            new PlaintextOnlySecretReferenceResolver(),
            CancellationToken.None));
    }

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
                    SecretMaterialSource.SchemeAdapter)
                : SecretResolutionResult.Failed(SecretResolutionFailure.MaterialNotFound));
        }
    }
}
