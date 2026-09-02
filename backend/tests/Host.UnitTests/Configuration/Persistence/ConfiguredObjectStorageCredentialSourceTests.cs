// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Host.Configuration.Persistence;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Persistence;

/// <summary>Covers the access key one request to the object-storage endpoint is signed with, resolved per request from the references the section declared.</summary>
public sealed class ConfiguredObjectStorageCredentialSourceTests
{
    [Fact]
    public async Task ResolveAsync_ADeclarationNamingBothHalves_ResolvesEachFromItsOwnReference()
    {
        // Arrange
        var resolver = ResolverAnswering();
        var source = SourceOver(resolver);

        // Act
        using var credential = await source.ResolveAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("AKIAEXAMPLEIDENTIFIER", credential.AccessKeyId);
        Assert.Equal("an-example-signing-secret", credential.SecretAccessKey);
    }

    /// <summary>
    /// Resolved per request rather than once, so a key rotated behind an unchanged reference takes effect on the next
    /// call with no cache to invalidate.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_TwoRequests_ReadsTheReferencesAgainRatherThanServingACachedKey()
    {
        // Arrange
        var resolver = ResolverAnswering();
        var source = SourceOver(resolver);

        // Act
        using (await source.ResolveAsync(TestContext.Current.CancellationToken))
        {
        }

        using (await source.ResolveAsync(TestContext.Current.CancellationToken))
        {
        }

        // Assert
        await resolver.Received(2).ResolveAsync("file:key-id", Arg.Any<CancellationToken>());
        await resolver.Received(2).ResolveAsync("file:secret", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Either half failing refuses the operation rather than falling back, which is the whole point: the AWS client's
    /// own chain would otherwise sign as whatever identity the host happens to carry.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_AnIdentifierThatCannotBeResolved_RefusesNamingTheSettingAndNothingElse()
    {
        // Arrange
        var resolver = ResolverAnswering();
        resolver.ResolveAsync("file:key-id", Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(SecretResolutionResult.Failed(SecretResolutionFailure.MaterialNotFound)));

        var source = SourceOver(resolver);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.ResolveAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("ContentStorage:ObjectStorage:AccessKeyId", failure.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(SecretResolutionFailure.MaterialNotFound), failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("file:key-id", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_ASecretThatCannotBeResolved_RefusesNamingThatSetting()
    {
        // Arrange
        var resolver = ResolverAnswering();
        resolver.ResolveAsync("file:secret", Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(SecretResolutionResult.Failed(SecretResolutionFailure.MaterialEmpty)));

        var source = SourceOver(resolver);

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.ResolveAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("ContentStorage:ObjectStorage:SecretAccessKey", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Material resolved for a credential that was never built would otherwise stay in memory until the collector reclaimed it.</summary>
    [Fact]
    public async Task ResolveAsync_ASecretThatCannotBeResolved_ReleasesTheIdentifierItHadAlreadyResolved()
    {
        // Arrange
        var accessKeyIdMaterial = ResolvedSecret.FromText("AKIAEXAMPLEIDENTIFIER");
        var resolver = Substitute.For<ISecretReferenceResolver>();
        resolver.ResolveAsync("file:key-id", Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(SecretResolutionResult.Resolved(accessKeyIdMaterial, SecretMaterialSource.SchemeAdapter)));
        resolver.ResolveAsync("file:secret", Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(SecretResolutionResult.Failed(SecretResolutionFailure.MaterialNotFound)));

        var source = SourceOver(resolver);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.ResolveAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Throws<ObjectDisposedException>(() => accessKeyIdMaterial.RevealAsString());
    }

    private static ConfiguredObjectStorageCredentialSource SourceOver(ISecretReferenceResolver resolver) => new(
        Options.Create(new ContentStorageOptions
        {
            Backend = ContentStorageBackend.ObjectStorage,
            ObjectStorage = ContentStorageOptionsTests.UsableEndpoint(),
        }),
        resolver);

    private static ISecretReferenceResolver ResolverAnswering()
    {
        var resolver = Substitute.For<ISecretReferenceResolver>();
        resolver.ResolveAsync("file:key-id", Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(SecretResolutionResult.Resolved(
                ResolvedSecret.FromText("AKIAEXAMPLEIDENTIFIER"),
                SecretMaterialSource.SchemeAdapter)));
        resolver.ResolveAsync("file:secret", Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromResult(SecretResolutionResult.Resolved(
                ResolvedSecret.FromText("an-example-signing-secret"),
                SecretMaterialSource.SchemeAdapter)));

        return resolver;
    }
}
